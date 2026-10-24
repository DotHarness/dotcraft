using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DotCraft.Oratorio.Data;
using DotCraft.Oratorio.Domain;

namespace DotCraft.Oratorio.Integrations;

public sealed record ConversationThread(
    string ThreadId,
    string AppServerEndpoint,
    string ContextJson,
    string Origin,
    DateTimeOffset At);

public sealed class ConversationThreadLocator(OratorioDbContext db)
{
    private const int CandidateLimit = 10;

    public async Task<IReadOnlyList<ConversationThread>> ListAsync(string itemId, string? excludeRunId, CancellationToken ct)
    {
        var runs = await db.Runs.AsNoTracking()
            .Where(x =>
                x.ItemId == itemId &&
                x.RunId != excludeRunId &&
                x.RunnerKind == "appServer" &&
                x.Status == RunStatus.Succeeded &&
                x.ThreadId != null &&
                x.AppServerEndpoint != null &&
                x.PromptContextJson != null)
            .OrderByDescending(x => x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue)
            .Take(CandidateLimit)
            .Select(x => new { x.RunId, x.ThreadId, x.AppServerEndpoint, x.PromptContextJson, At = x.CompletedAt ?? x.StartedAt ?? DateTimeOffset.MinValue })
            .ToListAsync(ct);
        var turns = await db.DiscussionTurns.AsNoTracking()
            .Where(x =>
                x.ItemId == itemId &&
                x.Status != DiscussionTurnStatus.Pending &&
                x.ThreadId != null &&
                x.AppServerEndpoint != null &&
                x.ThreadContextJson != null)
            .OrderByDescending(x => x.StartedAt ?? x.CreatedAt)
            .Take(CandidateLimit)
            .Select(x => new { x.DiscussionTurnId, x.ThreadId, x.AppServerEndpoint, x.ThreadContextJson, At = x.StartedAt ?? x.CreatedAt })
            .ToListAsync(ct);

        return runs
            .Select(x => new ConversationThread(x.ThreadId!, x.AppServerEndpoint!, x.PromptContextJson!, $"run {ShortId(x.RunId)}", x.At))
            .Concat(turns.Select(x => new ConversationThread(x.ThreadId!, x.AppServerEndpoint!, x.ThreadContextJson!, $"discussion turn {ShortId(x.DiscussionTurnId)}", x.At)))
            .OrderByDescending(x => x.At)
            .ToArray();
    }

    public async Task<ConversationThread?> FindDiscussionThreadAsync(string itemId, CancellationToken ct) =>
        (await ListAsync(itemId, excludeRunId: null, ct)).FirstOrDefault(x => ConversationThreadContext.CarriesDiscussionReply(x.ContextJson));

    private static string ShortId(string value) => value.Length <= 12 ? value : value[..12];
}

public static class ConversationThreadContext
{
    private const string ImplementationMode = "implementation";
    private const string ReviewMode = "readOnlyReviewAnalysis";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Create(RunPurpose purpose, string workspacePath, string? baseWorkspacePath, IReadOnlyList<string> requiredDynamicTools) =>
        JsonSerializer.Serialize(new
        {
            promptMode = "compact",
            runtimeContextVersion = AppServerPromptBuilder.RuntimeContextVersion,
            threadStateWorkspaceMode = AppServerPromptBuilder.ThreadStateWorkspaceMode,
            mode = purpose == RunPurpose.Implementation ? ImplementationMode : ReviewMode,
            requiredDynamicTools,
            workspace = new { path = workspacePath, basePath = baseWorkspacePath }
        }, JsonOptions);

    public static string Reduce(string contextJson)
    {
        using var document = JsonDocument.Parse(contextJson);
        var root = document.RootElement;
        return Create(
            Purpose(contextJson),
            ReadWorkspace(root, "path", "Path") ?? "",
            ReadWorkspace(root, "basePath", "BasePath"),
            ReadTools(root));
    }

    public static bool UsesManagedWorktree(string contextJson, out string baseWorkspacePath)
    {
        using var document = JsonDocument.Parse(contextJson);
        var root = document.RootElement;
        baseWorkspacePath = ReadWorkspace(root, "basePath", "BasePath") ?? "";
        return baseWorkspacePath.Length > 0 && !SamePath(ReadWorkspace(root, "path", "Path"), baseWorkspacePath);
    }

    public static RunPurpose Purpose(string contextJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contextJson);
            return TryGetProperty(document.RootElement, "mode", "Mode", out var mode) &&
                string.Equals(mode.GetString(), ImplementationMode, StringComparison.Ordinal)
                    ? RunPurpose.Implementation
                    : RunPurpose.ReviewAnalysis;
        }
        catch (JsonException)
        {
            return RunPurpose.ReviewAnalysis;
        }
    }

    public static bool CarriesDiscussionReply(string contextJson)
    {
        try
        {
            using var document = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            return IsCurrentCompact(root) &&
                ReadTools(root).Contains(OratorioDynamicToolCatalog.SubmitDiscussionReplyId, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool IsReusable(
        string contextJson,
        string baseWorkspacePath,
        string executionWorkspacePath,
        IReadOnlyList<string> requiredDynamicTools)
    {
        try
        {
            using var document = JsonDocument.Parse(contextJson);
            var root = document.RootElement;
            if (!IsCurrentCompact(root) ||
                !TryGetProperty(root, "threadStateWorkspaceMode", "ThreadStateWorkspaceMode", out var workspaceMode) ||
                !string.Equals(workspaceMode.GetString(), AppServerPromptBuilder.ThreadStateWorkspaceMode, StringComparison.Ordinal))
            {
                return false;
            }

            if (!SamePath(ReadWorkspace(root, "path", "Path"), executionWorkspacePath) ||
                !SamePath(ReadWorkspace(root, "basePath", "BasePath"), baseWorkspacePath))
            {
                return false;
            }

            var previousTools = ReadTools(root).Order(StringComparer.Ordinal).ToArray();
            var currentTools = requiredDynamicTools.Order(StringComparer.Ordinal).ToArray();
            return previousTools.SequenceEqual(currentTools, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsCurrentCompact(JsonElement root) =>
        TryGetProperty(root, "promptMode", "PromptMode", out var promptMode) &&
        string.Equals(promptMode.GetString(), "compact", StringComparison.Ordinal) &&
        TryGetProperty(root, "runtimeContextVersion", "RuntimeContextVersion", out var version) &&
        version.ValueKind == JsonValueKind.String &&
        string.Equals(version.GetString(), AppServerPromptBuilder.RuntimeContextVersion, StringComparison.Ordinal);

    private static string[] ReadTools(JsonElement root) =>
        TryGetProperty(root, "requiredDynamicTools", "RequiredDynamicTools", out var tools) && tools.ValueKind == JsonValueKind.Array
            ? tools.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToArray()
            : [];

    private static string? ReadWorkspace(JsonElement root, string camelName, string pascalName)
    {
        if (!TryGetProperty(root, "workspace", "Workspace", out var workspace) ||
            workspace.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(workspace, camelName, pascalName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool TryGetProperty(JsonElement element, string camelName, string pascalName, out JsonElement value) =>
        element.TryGetProperty(camelName, out value) || element.TryGetProperty(pascalName, out value);

    private static bool SamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            left = Path.GetFullPath(left);
            right = Path.GetFullPath(right);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
