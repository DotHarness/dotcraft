using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;
using DotCraft.Oratorio.Services;
using DotCraft.Oratorio.Sources;

namespace DotCraft.Oratorio.GitHub;

public enum GitHubCommentCommandIntakeDisposition
{
    NotApplicable,
    NotCommand,
    InvalidCommand,
    UnsupportedCommand,
    Queued,
    Rejected,
    Existing,
    Malformed
}

public sealed record GitHubCommentCommandIntakeResult(
    GitHubCommentCommandIntakeDisposition Disposition,
    OratorioGitHubCommentCommand? Command = null,
    bool Duplicate = false,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record GitHubCommentCommandWebhookResponse(
    string CommandId,
    GitHubCommentCommandStatus Status,
    bool Duplicate,
    string? ErrorCode);

public sealed class GitHubCommentCommandIntakeService(
    OratorioDbContext db,
    GitHubCommentCommandParser parser,
    IOptionsMonitor<GitHubOptions> gitHubOptions,
    IOptionsMonitor<DotCraftOptions> dotCraftOptions,
    IClock clock)
{
    private static readonly HashSet<string> AllowedAssociations = new(
        ["OWNER", "MEMBER", "COLLABORATOR"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<GitHubCommentCommandIntakeResult> IntakeAsync(
        string body,
        string? deliveryId,
        CancellationToken ct)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return Malformed("githubWebhookPayloadInvalid", "GitHub issue_comment payload was not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            var action = ReadString(root, "action");
            if (!string.Equals(action, "created", StringComparison.OrdinalIgnoreCase))
            {
                return new GitHubCommentCommandIntakeResult(GitHubCommentCommandIntakeDisposition.NotApplicable);
            }

            if (!TryGetObject(root, "issue", out var issue) || !issue.TryGetProperty("pull_request", out _))
            {
                return new GitHubCommentCommandIntakeResult(GitHubCommentCommandIntakeDisposition.NotApplicable);
            }

            if (!TryGetObject(root, "comment", out var comment))
            {
                return Malformed("githubCommentMissing", "GitHub issue_comment payload did not include a comment.");
            }

            var commentBody = ReadString(comment, "body");
            if (commentBody is null)
            {
                return Malformed("githubCommentBodyMissing", "GitHub issue_comment payload did not include comment.body.");
            }

            var parsed = parser.Parse(commentBody);
            if (parsed.Status != GitHubCommentCommandParseStatus.Parsed)
            {
                return new GitHubCommentCommandIntakeResult(parsed.Status switch
                {
                    GitHubCommentCommandParseStatus.NotCommand => GitHubCommentCommandIntakeDisposition.NotCommand,
                    GitHubCommentCommandParseStatus.Invalid => GitHubCommentCommandIntakeDisposition.InvalidCommand,
                    GitHubCommentCommandParseStatus.Unsupported => GitHubCommentCommandIntakeDisposition.UnsupportedCommand,
                    _ => throw new InvalidOperationException($"Unexpected command parse status: {parsed.Status}")
                }, ErrorCode: parsed.ErrorCode);
            }

            var sourceCommentId = ReadIdentifier(comment, "id");
            var repository = ReadNestedString(root, "repository", "full_name");
            var pullRequestNumber = ReadInt32(issue, "number");
            if (string.IsNullOrWhiteSpace(sourceCommentId) ||
                string.IsNullOrWhiteSpace(repository) ||
                pullRequestNumber is null or <= 0)
            {
                return Malformed(
                    "githubCommentCommandTargetInvalid",
                    "GitHub review command requires comment.id, repository.full_name, and a positive issue.number.");
            }

            var normalizedRepository = SourceProjectKey.NormalizeGitHubRepository(repository);
            if (normalizedRepository is null)
            {
                return Malformed(
                    "githubCommentCommandRepositoryInvalid",
                    "GitHub review command repository must use owner/name form.");
            }

            var existing = await db.GitHubCommentCommands
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.SourceCommentId == sourceCommentId, ct);
            if (existing is not null)
            {
                return FromExisting(existing);
            }

            var actorLogin = ReadNestedString(root, "sender", "login")
                ?? ReadNestedString(comment, "user", "login")
                ?? "";
            var senderType = ReadNestedString(root, "sender", "type")
                ?? ReadNestedString(comment, "user", "type");
            var association = ReadString(comment, "author_association") ?? "";
            var rejection = ResolveRejection(normalizedRepository, actorLogin, senderType, association);
            var now = clock.UtcNow;
            var command = new OratorioGitHubCommentCommand
            {
                DeliveryId = EmptyToNull(deliveryId),
                SourceCommentId = sourceCommentId,
                Repository = normalizedRepository,
                PullRequestNumber = pullRequestNumber.Value,
                ActorLogin = actorLogin,
                AuthorAssociation = association,
                CommandKind = parsed.CommandKind!.Value,
                Focus = parsed.Focus,
                Status = rejection is null ? GitHubCommentCommandStatus.Queued : GitHubCommentCommandStatus.Rejected,
                ErrorCode = rejection?.Code,
                ErrorMessage = rejection?.Message,
                CreatedAt = now,
                UpdatedAt = now,
                CompletedAt = rejection is null ? null : now
            };

            db.GitHubCommentCommands.Add(command);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                existing = await db.GitHubCommentCommands
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.SourceCommentId == sourceCommentId, ct);
                if (existing is null)
                {
                    throw;
                }

                return FromExisting(existing);
            }

            return new GitHubCommentCommandIntakeResult(
                rejection is null
                    ? GitHubCommentCommandIntakeDisposition.Queued
                    : GitHubCommentCommandIntakeDisposition.Rejected,
                command,
                ErrorCode: command.ErrorCode,
                ErrorMessage: command.ErrorMessage);
        }
    }

    private (string Code, string Message)? ResolveRejection(
        string repository,
        string actorLogin,
        string? senderType,
        string association)
    {
        if (string.Equals(senderType, "Bot", StringComparison.OrdinalIgnoreCase))
        {
            return ("githubCommentCommandBotRejected", "GitHub bot comments cannot trigger Oratorio review.");
        }

        if (string.IsNullOrWhiteSpace(actorLogin) || !AllowedAssociations.Contains(association))
        {
            return (
                "githubCommentCommandActorUnauthorized",
                "GitHub review command requires an OWNER, MEMBER, or COLLABORATOR actor.");
        }

        if (!gitHubOptions.CurrentValue.Repositories.Any(x => SourceProjectKey.AreEquivalent(x, repository)))
        {
            return (
                "githubCommentCommandRepositoryNotConfigured",
                $"Repository '{repository}' is not configured in Oratorio.");
        }

        if (!dotCraftOptions.CurrentValue.RepositoryWorkspaceRoutes.Any(route =>
                SourceProjectKey.AreEquivalent(route.Project, repository) &&
                !string.IsNullOrWhiteSpace(route.WorkspacePath)))
        {
            return (
                "githubCommentCommandWorkspaceNotConfigured",
                $"Repository '{repository}' does not have an Oratorio workspace mapping.");
        }

        return null;
    }

    private static GitHubCommentCommandIntakeResult FromExisting(OratorioGitHubCommentCommand command) =>
        new(
            command.Status switch
            {
                GitHubCommentCommandStatus.Queued => GitHubCommentCommandIntakeDisposition.Queued,
                GitHubCommentCommandStatus.Rejected => GitHubCommentCommandIntakeDisposition.Rejected,
                _ => GitHubCommentCommandIntakeDisposition.Existing
            },
            command,
            Duplicate: true,
            ErrorCode: command.ErrorCode,
            ErrorMessage: command.ErrorMessage);

    private static GitHubCommentCommandIntakeResult Malformed(string errorCode, string message) =>
        new(GitHubCommentCommandIntakeDisposition.Malformed, ErrorCode: errorCode, ErrorMessage: message);

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedString(JsonElement element, string objectName, string propertyName) =>
        TryGetObject(element, objectName, out var nested)
            ? ReadString(nested, propertyName)
            : null;

    private static string? ReadIdentifier(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static int? ReadInt32(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
