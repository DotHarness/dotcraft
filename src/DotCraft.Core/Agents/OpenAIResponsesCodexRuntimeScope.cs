using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;
using DotCraft.Tracing;

namespace DotCraft.Agents;

internal static class OpenAIResponsesCodexRequestKinds
{
    public const string Turn = "turn";
    public const string Compaction = "compaction";
    public const string Memory = "memory";
}

internal sealed class OpenAIResponsesCodexRuntimeContext
{
    private readonly object _gate = new();
    private string? _turnState;

    public required string ThreadId { get; init; }

    public string? TurnId { get; init; }

    public required string WindowId { get; set; }

    public string RequestKind { get; set; } = OpenAIResponsesCodexRequestKinds.Turn;

    public long TurnStartedAtUnixMs { get; init; }

    public string? ParentThreadId { get; init; }

    public string? ForkedFromThreadId { get; init; }

    public string? SubagentKind { get; init; }

    public string? ThreadSource { get; init; }

    public string? TurnState
    {
        get
        {
            lock (_gate)
                return _turnState;
        }
    }

    public bool TryCaptureTurnState(string? value)
    {
        var normalized = NormalizeHeaderValue(value);
        if (string.IsNullOrEmpty(normalized))
            return false;

        lock (_gate)
        {
            if (!string.IsNullOrEmpty(_turnState))
                return false;

            _turnState = normalized;
            return true;
        }
    }

    private static string? NormalizeHeaderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        var builder = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (ch is >= ' ' and <= '~')
                builder.Append(ch);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}

internal static class OpenAIResponsesCodexRuntimeScope
{
    private static readonly AsyncLocal<OpenAIResponsesCodexRuntimeContext?> CurrentContext = new();

    public static OpenAIResponsesCodexRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(OpenAIResponsesCodexRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(OpenAIResponsesCodexRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}

internal sealed record OpenAIResponsesCodexMetadataSnapshot(
    string InstallationId,
    string? SessionId,
    string? ThreadId,
    string? TurnId,
    string? WindowId,
    string? ParentThreadId,
    string? SubagentKind,
    string? TurnMetadataJson,
    string? TurnState);

internal static class OpenAIResponsesCodexMetadata
{
    internal static OpenAIResponsesCodexMetadataSnapshot CreateSnapshot(string installationId)
    {
        var normalizedInstallationId = NormalizeRequired(installationId, nameof(installationId));
        var context = OpenAIResponsesCodexRuntimeScope.Current;
        var activeThreadId = NormalizeOptional(context?.ThreadId)
            ?? NormalizeOptional(TracingChatClient.CurrentSessionKey)
            ?? NormalizeOptional(TracingChatClient.GetActiveSessionKey());

        if (context == null)
        {
            return new OpenAIResponsesCodexMetadataSnapshot(
                normalizedInstallationId,
                activeThreadId,
                activeThreadId,
                TurnId: null,
                WindowId: null,
                ParentThreadId: null,
                SubagentKind: null,
                TurnMetadataJson: null,
                TurnState: null);
        }

        var turnMetadata = BuildTurnMetadataJson(normalizedInstallationId, context, activeThreadId);
        return new OpenAIResponsesCodexMetadataSnapshot(
            normalizedInstallationId,
            activeThreadId,
            activeThreadId,
            NormalizeOptional(context.TurnId),
            NormalizeOptional(context.WindowId),
            NormalizeOptional(context.ParentThreadId),
            NormalizeOptional(context.SubagentKind),
            turnMetadata,
            context.TurnState);
    }

    internal static IReadOnlyDictionary<string, string> BuildClientMetadata(
        OpenAIResponsesCodexMetadataSnapshot snapshot)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [OpenAIAuthConstants.InstallationIdHeader] = snapshot.InstallationId
        };

        AddIfPresent(metadata, OpenAIAuthConstants.SessionIdCompatHeader, snapshot.SessionId);
        AddIfPresent(metadata, "thread_id", snapshot.ThreadId);
        AddIfPresent(metadata, "turn_id", snapshot.TurnId);
        AddIfPresent(metadata, OpenAIAuthConstants.WindowIdHeader, snapshot.WindowId);
        AddIfPresent(metadata, OpenAIAuthConstants.ParentThreadIdHeader, snapshot.ParentThreadId);
        AddIfPresent(metadata, OpenAIAuthConstants.SubAgentHeader, snapshot.SubagentKind);
        AddIfPresent(metadata, OpenAIAuthConstants.TurnMetadataHeader, snapshot.TurnMetadataJson);
        return metadata;
    }

    private static string BuildTurnMetadataJson(
        string installationId,
        OpenAIResponsesCodexRuntimeContext context,
        string? activeThreadId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("installation_id", installationId);
            WriteStringIfPresent(writer, "session_id", activeThreadId);
            WriteStringIfPresent(writer, "thread_id", activeThreadId);
            WriteStringIfPresent(writer, "turn_id", context.TurnId);
            WriteStringIfPresent(writer, "window_id", context.WindowId);
            WriteStringIfPresent(writer, "request_kind", context.RequestKind);
            if (context.TurnStartedAtUnixMs > 0)
                writer.WriteNumber("turn_started_at_unix_ms", context.TurnStartedAtUnixMs);
            WriteStringIfPresent(writer, "parent_thread_id", context.ParentThreadId);
            WriteStringIfPresent(writer, "forked_from_thread_id", context.ForkedFromThreadId);
            WriteStringIfPresent(writer, "subagent_kind", context.SubagentKind);
            WriteStringIfPresent(writer, "thread_source", context.ThreadSource);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void AddIfPresent(Dictionary<string, string> metadata, string key, string? value)
    {
        var normalized = NormalizeOptional(value);
        if (!string.IsNullOrEmpty(normalized))
            metadata[key] = normalized;
    }

    private static void WriteStringIfPresent(Utf8JsonWriter writer, string key, string? value)
    {
        var normalized = NormalizeOptional(value);
        if (!string.IsNullOrEmpty(normalized))
            writer.WriteString(key, normalized);
    }

    private static string NormalizeRequired(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
