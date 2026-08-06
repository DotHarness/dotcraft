using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;
using DotCraft.Auth.OpenAI;

namespace DotCraft.Agents;

internal sealed class OpenAIResponsesCodexRuntimeContext(ProviderConversationIdentity conversationIdentity)
{
    private readonly object _gate = new();
    private ProviderConversationIdentity _conversationIdentity = conversationIdentity
        ?? throw new ArgumentNullException(nameof(conversationIdentity));
    private string? _turnState;

    public ProviderConversationIdentity ConversationIdentity
    {
        get
        {
            lock (_gate)
                return _conversationIdentity;
        }
    }

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

    public void AdvanceContextWindow(string contextWindowId)
    {
        if (string.IsNullOrWhiteSpace(contextWindowId))
            throw new ArgumentException("Value must be non-empty.", nameof(contextWindowId));

        lock (_gate)
        {
            _conversationIdentity = _conversationIdentity with
            {
                ContextWindowId = contextWindowId.Trim()
            };
        }
    }

    public IDisposable OverrideRequestKind(ProviderRequestKind requestKind)
    {
        ProviderRequestKind previous;
        lock (_gate)
        {
            previous = _conversationIdentity.RequestKind;
            _conversationIdentity = _conversationIdentity with { RequestKind = requestKind };
        }

        return new RequestKindScope(this, previous);
    }

    private void RestoreRequestKind(ProviderRequestKind requestKind)
    {
        lock (_gate)
            _conversationIdentity = _conversationIdentity with { RequestKind = requestKind };
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

    private sealed class RequestKindScope(
        OpenAIResponsesCodexRuntimeContext owner,
        ProviderRequestKind previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RestoreRequestKind(previous);
        }
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

internal static class OpenAIResponsesRoutingIdentityScope
{
    private static readonly AsyncLocal<OpenAIResponsesRoutingIdentity?> CurrentIdentity = new();

    public static OpenAIResponsesRoutingIdentity? Current => CurrentIdentity.Value;

    public static IDisposable Set(OpenAIResponsesRoutingIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var previous = CurrentIdentity.Value;
        CurrentIdentity.Value = identity;
        return new Scope(previous);
    }

    private sealed class Scope(OpenAIResponsesRoutingIdentity? previous) : IDisposable
    {
        public void Dispose() => CurrentIdentity.Value = previous;
    }
}

internal sealed record OpenAIResponsesCodexMetadataSnapshot(
    string InstallationId,
    string? SessionId,
    string? ThreadId,
    string? ClientRequestId,
    string? DefaultPromptCacheKey,
    string? TurnId,
    string? WindowId,
    string? ParentThreadId,
    string? SubagentHeader,
    string? SubagentKind,
    string? TurnMetadataJson,
    string? TurnState);

internal sealed record OpenAIResponsesRoutingIdentity(
    string? SessionId,
    string? ThreadId,
    string? DefaultPromptCacheKey,
    string? ClientRequestId);

internal static class OpenAIResponsesCodexMetadata
{
    internal static OpenAIResponsesCodexMetadataSnapshot CreateSnapshot(string installationId)
    {
        var normalizedInstallationId = NormalizeRequired(installationId, nameof(installationId));
        var context = OpenAIResponsesCodexRuntimeScope.Current;
        var requestContext = ProviderRequestContextScope.Current;
        var conversationIdentity = requestContext?.CurrentIdentity ?? context?.ConversationIdentity;
        var routingIdentity = ResolveRoutingIdentity();

        if (conversationIdentity == null)
        {
            return new OpenAIResponsesCodexMetadataSnapshot(
                normalizedInstallationId,
                routingIdentity.SessionId,
                routingIdentity.ThreadId,
                routingIdentity.ClientRequestId,
                routingIdentity.DefaultPromptCacheKey,
                TurnId: null,
                WindowId: null,
                ParentThreadId: null,
                SubagentHeader: null,
                SubagentKind: null,
                TurnMetadataJson: null,
                TurnState: null);
        }

        var turnMetadata = BuildTurnMetadataJson(
            normalizedInstallationId,
            conversationIdentity!,
            routingIdentity);
        return new OpenAIResponsesCodexMetadataSnapshot(
            normalizedInstallationId,
            routingIdentity.SessionId,
            routingIdentity.ThreadId,
            routingIdentity.ClientRequestId,
            routingIdentity.DefaultPromptCacheKey,
            NormalizeOptional(conversationIdentity!.TurnId),
            NormalizeOptional(conversationIdentity.ContextWindowId),
            NormalizeOptional(conversationIdentity.ParentThreadId),
            ResolveSubagentHeader(conversationIdentity.SubagentKind),
            NormalizeOptional(conversationIdentity.SubagentKind),
            turnMetadata,
            requestContext?.ConversationState?.ContinuationState ?? context?.TurnState);
    }

    internal static OpenAIResponsesCodexMetadataSnapshot GetOrCreateSnapshot(
        PipelineMessage message,
        string installationId)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.TryGetProperty(
                typeof(OpenAIResponsesCodexMetadataSnapshot),
                out var value)
            && value is OpenAIResponsesCodexMetadataSnapshot snapshot)
        {
            return snapshot;
        }

        snapshot = CreateSnapshot(installationId);
        message.SetProperty(typeof(OpenAIResponsesCodexMetadataSnapshot), snapshot);
        return snapshot;
    }

    internal static OpenAIResponsesRoutingIdentity ResolveRoutingIdentity()
    {
        if (OpenAIResponsesRoutingIdentityScope.Current is { } requestIdentity)
            return requestIdentity;

        var conversationIdentity = ProviderRequestContextScope.Current?.CurrentIdentity
                                   ?? OpenAIResponsesCodexRuntimeScope.Current?.ConversationIdentity;
        var fallbackThreadId = NormalizeOptional(
            ProviderRequestContextScope.Current?.ConversationIdentity.CurrentThreadId);
        var currentThreadId = NormalizeOptional(conversationIdentity?.CurrentThreadId)
                              ?? fallbackThreadId;
        var cacheSessionId = NormalizeOptional(conversationIdentity?.RootThreadId)
                             ?? currentThreadId;
        return new OpenAIResponsesRoutingIdentity(
            SessionId: cacheSessionId,
            ThreadId: currentThreadId,
            DefaultPromptCacheKey: cacheSessionId,
            ClientRequestId: currentThreadId);
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
        AddIfPresent(metadata, OpenAIAuthConstants.SubAgentHeader, snapshot.SubagentHeader);
        AddIfPresent(metadata, OpenAIAuthConstants.TurnMetadataHeader, snapshot.TurnMetadataJson);
        return metadata;
    }

    private static string? ResolveSubagentHeader(string? subagentKind) =>
        string.Equals(subagentKind, "thread_spawn", StringComparison.Ordinal)
            ? "collab_spawn"
            : NormalizeOptional(subagentKind);

    private static string BuildTurnMetadataJson(
        string installationId,
        ProviderConversationIdentity identity,
        OpenAIResponsesRoutingIdentity routingIdentity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("installation_id", installationId);
            WriteStringIfPresent(writer, "session_id", routingIdentity.SessionId);
            WriteStringIfPresent(writer, "thread_id", routingIdentity.ThreadId);
            WriteStringIfPresent(writer, "turn_id", identity.TurnId);
            WriteStringIfPresent(writer, "window_id", identity.ContextWindowId);
            writer.WriteString("request_kind", ToProviderRequestKind(identity.RequestKind));
            if (identity.TurnStartedAtUnixMs > 0)
                writer.WriteNumber("turn_started_at_unix_ms", identity.TurnStartedAtUnixMs);
            WriteStringIfPresent(writer, "parent_thread_id", identity.ParentThreadId);
            WriteStringIfPresent(writer, "forked_from_thread_id", identity.ForkedFromThreadId);
            WriteStringIfPresent(writer, "subagent_kind", identity.SubagentKind);
            WriteStringIfPresent(writer, "thread_source", identity.ThreadSource);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ToProviderRequestKind(ProviderRequestKind requestKind) =>
        requestKind switch
        {
            ProviderRequestKind.Turn => "turn",
            ProviderRequestKind.Compaction => "compaction",
            ProviderRequestKind.Memory => "memory",
            _ => throw new ArgumentOutOfRangeException(nameof(requestKind), requestKind, null)
        };

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
