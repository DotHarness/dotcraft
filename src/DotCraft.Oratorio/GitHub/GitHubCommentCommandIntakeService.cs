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

    public Task<GitHubCommentCommandIntakeResult> IntakeWebhookAsync(
        string eventName,
        string body,
        string? deliveryId,
        CancellationToken ct)
    {
        var read = GitHubCommentWebhookReader.Read(eventName, body);
        if (read.IsMalformed)
        {
            return Task.FromResult(new GitHubCommentCommandIntakeResult(
                GitHubCommentCommandIntakeDisposition.Malformed,
                ErrorCode: read.ErrorCode,
                ErrorMessage: read.ErrorMessage));
        }

        return read.Candidate is null
            ? Task.FromResult(new GitHubCommentCommandIntakeResult(GitHubCommentCommandIntakeDisposition.NotApplicable))
            : IntakeAsync(read.Candidate, deliveryId, ct);
    }

    public async Task<GitHubCommentCommandIntakeResult> IntakeAsync(
        GitHubCommentCandidate candidate,
        string? deliveryId,
        CancellationToken ct)
    {
        if (candidate.IsBotAuthor)
        {
            return new GitHubCommentCommandIntakeResult(GitHubCommentCommandIntakeDisposition.NotApplicable);
        }

        var parsed = parser.Parse(candidate.Body);
        switch (parsed.Status)
        {
            case GitHubCommentCommandParseStatus.NotCommand:
                return new GitHubCommentCommandIntakeResult(GitHubCommentCommandIntakeDisposition.NotCommand);
            case GitHubCommentCommandParseStatus.Invalid:
                return new GitHubCommentCommandIntakeResult(
                    GitHubCommentCommandIntakeDisposition.InvalidCommand,
                    ErrorCode: parsed.ErrorCode);
        }

        var sourceCommentId = candidate.SourceCommentId;
        var existing = await FindExistingAsync(sourceCommentId, ct);
        if (existing is not null)
        {
            return FromExisting(existing);
        }

        var rejection = ResolveRejection(candidate);
        if (rejection is null && candidate.TargetKind == ItemKind.Issue && parsed.CommandKind == GitHubCommentCommandKind.Review)
        {
            rejection = ("githubCommentCommandUnsupportedForIssue", "Review commands require a pull request.");
        }
        var now = clock.UtcNow;
        var command = new OratorioGitHubCommentCommand
        {
            DeliveryId = string.IsNullOrWhiteSpace(deliveryId) ? null : deliveryId.Trim(),
            SourceCommentId = sourceCommentId,
            CommentKind = candidate.Kind,
            CommentId = candidate.CommentId,
            CommentUrl = candidate.HtmlUrl,
            CommentCreatedAt = candidate.CreatedAt,
            ThreadRootCommentId = candidate.Kind == GitHubCommentKind.ReviewComment
                ? candidate.InReplyToId ?? candidate.CommentId
                : null,
            Path = candidate.Path,
            Line = candidate.Line,
            Repository = candidate.Repository,
            TargetNumber = candidate.TargetNumber,
            TargetKind = candidate.TargetKind,
            ActorLogin = candidate.AuthorLogin,
            AuthorAssociation = candidate.AuthorAssociation,
            CommandKind = parsed.CommandKind!.Value,
            Focus = parsed.Focus,
            Question = parsed.Question,
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
            existing = await FindExistingAsync(sourceCommentId, ct);
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

    public static bool HasWorkspaceMapping(DotCraftOptions options, string repository) =>
        options.RepositoryWorkspaceRoutes.Any(route =>
            SourceProjectKey.AreEquivalent(route.Project, repository) &&
            !string.IsNullOrWhiteSpace(route.WorkspacePath));

    private Task<OratorioGitHubCommentCommand?> FindExistingAsync(string sourceCommentId, CancellationToken ct) =>
        db.GitHubCommentCommands
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourceCommentId == sourceCommentId, ct);

    private (string Code, string Message)? ResolveRejection(GitHubCommentCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.AuthorLogin) || !AllowedAssociations.Contains(candidate.AuthorAssociation))
        {
            return (
                "githubCommentCommandActorUnauthorized",
                "GitHub comment commands require an OWNER, MEMBER, or COLLABORATOR actor.");
        }

        if (!gitHubOptions.CurrentValue.Repositories.Any(x => SourceProjectKey.AreEquivalent(x, candidate.Repository)))
        {
            return (
                "githubCommentCommandRepositoryNotConfigured",
                $"Repository '{candidate.Repository}' is not configured in Oratorio.");
        }

        if (!HasWorkspaceMapping(dotCraftOptions.CurrentValue, candidate.Repository))
        {
            return (
                "githubCommentCommandWorkspaceNotConfigured",
                $"Repository '{candidate.Repository}' does not have an Oratorio workspace mapping.");
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
}
