using AnthropicCacheControlEphemeral = Anthropic.Models.Messages.CacheControlEphemeral;
using AnthropicTextBlockParam = Anthropic.Models.Messages.TextBlockParam;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Tracing;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Maintenance task kinds that may run by forking a stable prompt request prefix.
/// </summary>
public enum MaintenanceForkTaskKind
{
    /// <summary>Summarize conversation context for history compaction.</summary>
    ContextCompaction,

    /// <summary>Extract durable user/project memory from recent conversation context.</summary>
    MemoryConsolidation
}

/// <summary>
/// A maintenance task appended to a prompt request snapshot.
/// </summary>
/// <param name="Kind">The task kind.</param>
/// <param name="Instructions">Task-specific instructions appended at the tail.</param>
public sealed record MaintenanceForkTask(
    MaintenanceForkTaskKind Kind,
    string Instructions);

/// <summary>
/// Result returned from a maintenance fork attempt.
/// </summary>
public sealed record MaintenanceForkResult(
    MaintenanceForkTaskKind TaskKind,
    string? Text,
    string? FallbackReason,
    TokenUsageSnapshot? TokenUsage);

/// <summary>
/// Optional execution settings for maintenance forks that intentionally allow
/// local tool calls while preserving the model-visible tool schema.
/// </summary>
public sealed record MaintenanceForkToolExecutionOptions(
    Func<FunctionInvocationContext, ModeToolPolicyDecision> ToolPolicy)
{
    /// <summary>Whether multiple tool calls from one model response may run concurrently.</summary>
    public bool AllowConcurrentInvocation { get; init; }

    /// <summary>Whether recoverable tool exceptions should include detailed messages.</summary>
    public bool IncludeDetailedErrors { get; init; }

    /// <summary>
    /// Maximum model continuations after tool-loop termination. The default is
    /// inherited from <see cref="StreamingFunctionInvokingChatClient"/>.
    /// </summary>
    public int? MaximumGuidanceContinuationsPerRequest { get; init; }
}

/// <summary>
/// Machine-readable fallback reasons returned by maintenance forks.
/// </summary>
public static class MaintenanceForkFallbackReasons
{
    /// <summary>Provider rejected the snapshot fork because the input exceeded the context window.</summary>
    public const string SnapshotTooLarge = "maintenance_snapshot_too_large";
}

/// <summary>
/// Provider-specific prompt-cache shaping settings for maintenance forks.
/// </summary>
public sealed record MaintenanceForkCacheOptions(
    string? ProviderProtocol,
    AppConfig.PromptCachingConfig? PromptCaching,
    string? Model);

/// <summary>
/// Diagnostics emitted for provider-specific maintenance fork cache shaping.
/// </summary>
public sealed record MaintenanceForkCacheDiagnostics(
    bool CacheShapeApplied,
    string? CacheShapeKind = null,
    bool? PromptCacheKeyPresent = null,
    string? CacheMarkerSource = null)
{
    public static MaintenanceForkCacheDiagnostics None { get; } = new(false);
}

/// <summary>
/// Runs provider-agnostic maintenance requests by reusing a captured prompt
/// request prefix and appending only a tail task message.
/// </summary>
public sealed class MaintenanceForkRunner(
    IChatClient chatClient,
    TraceCollector? traceCollector = null,
    MaintenanceForkCacheOptions? cacheOptions = null)
{
    /// <summary>
    /// Runs a maintenance fork and returns the assistant text, or a fallback reason.
    /// </summary>
    public async Task<MaintenanceForkResult> RunAsync(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            snapshot,
            task,
            messagesBeforeTask: null,
            cancellationToken);
    }

    /// <summary>
    /// Runs a maintenance fork with extra messages appended after the cached
    /// snapshot prefix and before the maintenance task.
    /// </summary>
    public async Task<MaintenanceForkResult> RunAsync(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        IReadOnlyList<ChatMessage>? messagesBeforeTask,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(
            snapshot,
            task,
            messagesBeforeTask,
            toolExecution: null,
            cancellationToken);
    }

    /// <summary>
    /// Runs a maintenance fork with optional local tool execution guarded by a
    /// runtime policy. Tool schemas are copied from the snapshot unchanged.
    /// </summary>
    public async Task<MaintenanceForkResult> RunAsync(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        IReadOnlyList<ChatMessage>? messagesBeforeTask,
        MaintenanceForkToolExecutionOptions? toolExecution,
        CancellationToken cancellationToken = default)
    {
        var messages = BuildMessages(snapshot, task, messagesBeforeTask).ToList();
        var options = BuildOptions(snapshot);
        var cacheDiagnostics = MaintenanceForkCacheShaper.Apply(
            snapshot,
            messages,
            options,
            cacheOptions);
        var sessionKey = ResolveTraceSessionKey(snapshot);
        var taskPrompt = FormatTask(task);
        var estimatedInputTokens = EstimateInputTokens(snapshot, messages, options, messagesBeforeTask, task);
        traceCollector?.RecordMaintenanceForkRequest(
            sessionKey,
            task.Kind,
            taskPrompt,
            snapshot.ThreadId,
            snapshot.TurnId,
            snapshot.Mode,
            snapshot.ModelId,
            snapshot.ProviderId,
            snapshot.Messages.Count,
            messagesBeforeTask?.Count ?? 0,
            snapshot.Tools,
            snapshot.BaseInstructionsFingerprint,
            snapshot.ToolFingerprint,
            estimatedInputTokens: estimatedInputTokens,
            snapshotSource: snapshot.SnapshotSource,
            snapshotInvalidReason: snapshot.SnapshotInvalidReason,
            cacheShapeApplied: cacheDiagnostics.CacheShapeApplied,
            cacheShapeKind: cacheDiagnostics.CacheShapeKind,
            promptCacheKeyPresent: cacheDiagnostics.PromptCacheKeyPresent,
            cacheMarkerSource: cacheDiagnostics.CacheMarkerSource);

        try
        {
            using var traceScope = traceCollector == null
                ? null
                : BeginToolExecutionTraceScope(snapshot, task, sessionKey, toolExecution);
            var responseClient = CreateResponseClient(toolExecution);
            var response = await GetResponseAsync(
                responseClient,
                messages,
                options,
                toolExecution,
                cancellationToken);
            TokenUsageSnapshot? usage = response.Usage is null
                ? null
                : TokenUsageExtractor.FromResponse(response);
            var fallbackReason = ClassifyFallbackReason(response);
            traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                task.Kind,
                response,
                fallbackReason);
            return new MaintenanceForkResult(
                task.Kind,
                response.Text,
                fallbackReason,
                usage);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                task.Kind,
                "provider_timeout");
            return new MaintenanceForkResult(task.Kind, null, "provider_timeout", null);
        }
        catch (OperationCanceledException)
        {
            traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                task.Kind,
                "cancelled");
            throw;
        }
        catch (Exception ex)
        {
            var fallbackReason = CompactionErrors.IsPromptTooLong(ex)
                ? MaintenanceForkFallbackReasons.SnapshotTooLarge
                : ex.Message;
            traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                task.Kind,
                fallbackReason,
                ex.Message);
            return new MaintenanceForkResult(task.Kind, null, fallbackReason, null);
        }
    }

    private IChatClient CreateResponseClient(MaintenanceForkToolExecutionOptions? toolExecution)
    {
        if (toolExecution == null)
            return chatClient;

        var invokingClient = new StreamingFunctionInvokingChatClient(chatClient)
        {
            AllowConcurrentInvocation = toolExecution.AllowConcurrentInvocation,
            IncludeDetailedErrors = toolExecution.IncludeDetailedErrors,
            ModeToolPolicy = toolExecution.ToolPolicy
        };
        if (toolExecution.MaximumGuidanceContinuationsPerRequest is { } continuations)
            invokingClient.MaximumGuidanceContinuationsPerRequest = continuations;

        return traceCollector == null
            ? invokingClient
            : new TracingChatClient(invokingClient, traceCollector);
    }

    private async Task<ChatResponse> GetResponseAsync(
        IChatClient responseClient,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        MaintenanceForkToolExecutionOptions? toolExecution,
        CancellationToken cancellationToken)
    {
        if (toolExecution != null && traceCollector != null)
        {
            return await responseClient
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .ToChatResponseAsync(cancellationToken);
        }

        return await responseClient.GetResponseAsync(
            messages,
            options,
            cancellationToken);
    }

    private static IDisposable? BeginToolExecutionTraceScope(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        string sessionKey,
        MaintenanceForkToolExecutionOptions? toolExecution)
    {
        if (toolExecution == null)
            return null;

        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = sessionKey;
        var callStateKey = BuildMaintenanceCallStateKey(snapshot, task, sessionKey);
        var callStateScope = TracingChatClient.UseCallStateKey(callStateKey);
        return new MaintenanceTraceScope(previousSessionKey, callStateKey, callStateScope);
    }

    private static string BuildMaintenanceCallStateKey(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        string sessionKey)
    {
        var turnOrRequestId = string.IsNullOrWhiteSpace(snapshot.TurnId)
            ? Guid.NewGuid().ToString("N")[..12]
            : snapshot.TurnId!.Trim();
        return $"{sessionKey}:maintenance:{FormatKind(task.Kind)}:{turnOrRequestId}";
    }

    private sealed class MaintenanceTraceScope(
        string? previousSessionKey,
        string callStateKey,
        IDisposable callStateScope) : IDisposable
    {
        public void Dispose()
        {
            TracingChatClient.ResetCallState(callStateKey);
            callStateScope.Dispose();
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    internal static IReadOnlyList<ChatMessage> BuildMessages(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        IReadOnlyList<ChatMessage>? messagesBeforeTask = null)
    {
        var messages = MessageGrouper
            .NormalizeFunctionCallArguments(snapshot.Messages)
            .Select(message => message.Clone())
            .ToList();
        if (messagesBeforeTask is { Count: > 0 })
        {
            messages.AddRange(MessageGrouper
                .NormalizeFunctionCallArguments(messagesBeforeTask)
                .Select(message => message.Clone()));
        }
        messages.Add(BuildTaskMessage(task));
        return messages;
    }

    internal static ChatMessage BuildTaskMessage(MaintenanceForkTask task) =>
        new(ChatRole.User, FormatTask(task));

    internal static ChatOptions BuildOptions(PromptRequestSnapshot snapshot)
    {
        return new ChatOptions
        {
            Instructions = snapshot.BaseInstructions,
            ModelId = snapshot.ModelId,
            Tools = snapshot.Tools.ToList(),
            Reasoning = snapshot.Reasoning,
            ResponseFormat = snapshot.ResponseFormat,
            MaxOutputTokens = snapshot.MaxOutputTokens,
            AllowMultipleToolCalls = snapshot.AllowMultipleToolCalls,
            ToolMode = snapshot.ToolMode
        };
    }

    private static string FormatTask(MaintenanceForkTask task)
    {
        return $"""
<system-reminder>
## Maintenance Task
Task: {FormatKind(task.Kind)}

{task.Instructions}
</system-reminder>
""";
    }

    private static string FormatKind(MaintenanceForkTaskKind kind) => kind switch
    {
        MaintenanceForkTaskKind.ContextCompaction => "context_compaction",
        MaintenanceForkTaskKind.MemoryConsolidation => "memory_consolidation",
        _ => kind.ToString()
    };

    private static string ResolveTraceSessionKey(PromptRequestSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.ThreadId))
            return snapshot.ThreadId!;

        var active = TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();
        if (!string.IsNullOrWhiteSpace(active))
            return active!;

        return "maintenance:" + Guid.NewGuid().ToString("N")[..12];
    }

    private static string? ClassifyFallbackReason(ChatResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
            return null;

        return ResponseContainsToolCall(response)
            ? "tool_call_without_text"
            : "empty_response";
    }

    private static bool ResponseContainsToolCall(ChatResponse response)
    {
        foreach (var message in response.Messages)
        {
            if (message.Contents.OfType<FunctionCallContent>().Any())
                return true;
        }

        return false;
    }

    private static long EstimateInputTokens(
        PromptRequestSnapshot snapshot,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options,
        IReadOnlyList<ChatMessage>? messagesBeforeTask,
        MaintenanceForkTask task)
    {
        var roughFullEstimate = EstimateRoughFullRequest(snapshot, messages, options);
        var estimatedInputTokens = roughFullEstimate;
        if (snapshot.EstimatedInputTokens is > 0)
        {
            var appended = new List<ChatMessage>((messagesBeforeTask?.Count ?? 0) + 1);
            if (messagesBeforeTask is { Count: > 0 })
                appended.AddRange(messagesBeforeTask);
            appended.Add(BuildTaskMessage(task));
            var hintedEstimate = (long)snapshot.EstimatedInputTokens.Value
                + MessageTokenEstimator.EstimateDelta(appended);
            estimatedInputTokens = Math.Max(roughFullEstimate, hintedEstimate);
        }

        return estimatedInputTokens;
    }

    private static long EstimateRoughFullRequest(
        PromptRequestSnapshot snapshot,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions? options)
    {
        var messageTokens = MessageTokenEstimator.Estimate(messages);
        var baseInstructionTokens = string.IsNullOrWhiteSpace(options?.Instructions)
            ? 0
            : MessageTokenEstimator.RoughTokenCount(snapshot.BaseInstructions);
        return (long)messageTokens + baseInstructionTokens;
    }

}

internal static class MaintenanceForkCacheShaper
{
    public static MaintenanceForkCacheDiagnostics Apply(
        PromptRequestSnapshot snapshot,
        List<ChatMessage> messages,
        ChatOptions options,
        MaintenanceForkCacheOptions? cacheOptions)
    {
        if (cacheOptions == null)
            return MaintenanceForkCacheDiagnostics.None;

        var protocol = NormalizeProtocol(cacheOptions.ProviderProtocol);
        return protocol switch
        {
            ModelProviderProtocols.Anthropic => ApplyAnthropic(snapshot, messages, options, cacheOptions),
            ModelProviderProtocols.OpenAIResponses => ApplyOpenAIResponses(snapshot, options),
            _ => MaintenanceForkCacheDiagnostics.None
        };
    }

    private static MaintenanceForkCacheDiagnostics ApplyAnthropic(
        PromptRequestSnapshot snapshot,
        List<ChatMessage> messages,
        ChatOptions options,
        MaintenanceForkCacheOptions cacheOptions)
    {
        var promptCaching = cacheOptions.PromptCaching;
        if (promptCaching == null || !promptCaching.ShouldApply(cacheOptions.Model ?? snapshot.ModelId))
            return MaintenanceForkCacheDiagnostics.None;

        var cacheControl = CreateAnthropicCacheControl(promptCaching.Ttl);
        var markedSources = new List<string>(capacity: 2);
        if (!string.IsNullOrWhiteSpace(options.Instructions))
        {
            messages.Insert(0, new ChatMessage(
                ChatRole.System,
                (IList<AIContent>)[CreateAnthropicCachedTextContent(new TextContent(options.Instructions), cacheControl)]));
            options.Instructions = null;
            markedSources.Add("system");
        }

        var stableSnapshotOffset = markedSources.Count > 0 ? 1 : 0;
        if (MarkSnapshotTextBlocks(messages, stableSnapshotOffset, snapshot.Messages.Count, cacheControl) > 0)
            markedSources.Add("snapshot_prefix");

        return markedSources.Count == 0
            ? MaintenanceForkCacheDiagnostics.None
            : new MaintenanceForkCacheDiagnostics(
                true,
                "anthropic-cache-control",
                PromptCacheKeyPresent: false,
                CacheMarkerSource: string.Join("+", markedSources));
    }

    private static MaintenanceForkCacheDiagnostics ApplyOpenAIResponses(
        PromptRequestSnapshot snapshot,
        ChatOptions options)
    {
        var promptCacheKey = ResponsesToolSearchMapper.ResolvePromptCacheKey(
            options,
            snapshot.ThreadId);
        if (string.IsNullOrWhiteSpace(promptCacheKey))
            return MaintenanceForkCacheDiagnostics.None;

        ResponsesToolSearchMapper.ApplyPromptCacheKey(options, promptCacheKey);

        return new MaintenanceForkCacheDiagnostics(
            true,
            "openai-responses-prompt-cache-key",
            PromptCacheKeyPresent: true,
            CacheMarkerSource: "thread");
    }

    private static int MarkSnapshotTextBlocks(
        List<ChatMessage> messages,
        int offset,
        int snapshotMessageCount,
        AnthropicCacheControlEphemeral cacheControl)
    {
        var marked = 0;
        var endExclusive = Math.Min(messages.Count, offset + Math.Max(0, snapshotMessageCount));
        var targets = new HashSet<(int MessageIndex, int ContentIndex)>();
        if (TryFindFirstSnapshotTextBlock(messages, offset, endExclusive, out var first))
            targets.Add(first);
        if (TryFindLatestSnapshotTextBlock(messages, offset, endExclusive, out var latest))
            targets.Add(latest);

        foreach (var group in targets
                     .GroupBy(static target => target.MessageIndex)
                     .OrderBy(static group => group.Key))
        {
            var message = messages[group.Key];
            var targetIndexes = group.Select(static target => target.ContentIndex).ToHashSet();
            var contents = message.Contents.ToList();
            var messageMarked = false;
            for (var i = 0; i < contents.Count; i++)
            {
                if (!targetIndexes.Contains(i) ||
                    contents[i] is not TextContent text ||
                    string.IsNullOrEmpty(text.Text))
                {
                    continue;
                }

                contents[i] = CreateAnthropicCachedTextContent(text, cacheControl);
                messageMarked = true;
                marked++;
            }

            if (!messageMarked)
                continue;

            messages[group.Key] = new ChatMessage(message.Role, contents)
            {
                AdditionalProperties = message.AdditionalProperties,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId,
                RawRepresentation = message.RawRepresentation
            };
        }

        return marked;
    }

    private static bool TryFindFirstSnapshotTextBlock(
        IReadOnlyList<ChatMessage> messages,
        int offset,
        int endExclusive,
        out (int MessageIndex, int ContentIndex) target)
    {
        for (var messageIndex = offset; messageIndex < endExclusive; messageIndex++)
        {
            if (TryFindTextBlock(messages[messageIndex], reverse: false, out var contentIndex))
            {
                target = (messageIndex, contentIndex);
                return true;
            }
        }

        target = default;
        return false;
    }

    private static bool TryFindLatestSnapshotTextBlock(
        IReadOnlyList<ChatMessage> messages,
        int offset,
        int endExclusive,
        out (int MessageIndex, int ContentIndex) target)
    {
        for (var messageIndex = endExclusive - 1; messageIndex >= offset; messageIndex--)
        {
            if (TryFindTextBlock(messages[messageIndex], reverse: true, out var contentIndex))
            {
                target = (messageIndex, contentIndex);
                return true;
            }
        }

        target = default;
        return false;
    }

    private static bool TryFindTextBlock(
        ChatMessage message,
        bool reverse,
        out int contentIndex)
    {
        contentIndex = -1;
        if (message.Role != ChatRole.User &&
            message.Role != ChatRole.Assistant &&
            message.Role != ChatRole.Tool &&
            message.Role != ChatRole.System)
        {
            return false;
        }

        if (reverse)
        {
            for (var i = message.Contents.Count - 1; i >= 0; i--)
            {
                if (message.Contents[i] is TextContent { Text.Length: > 0 })
                {
                    contentIndex = i;
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < message.Contents.Count; i++)
        {
            if (message.Contents[i] is TextContent { Text.Length: > 0 })
            {
                contentIndex = i;
                return true;
            }
        }

        return false;
    }

    private static TextContent CreateAnthropicCachedTextContent(
        TextContent text,
        AnthropicCacheControlEphemeral cacheControl)
    {
        var cached = new TextContent(text.Text)
        {
            AdditionalProperties = text.AdditionalProperties == null
                ? null
                : new AdditionalPropertiesDictionary(text.AdditionalProperties),
            RawRepresentation = text.RawRepresentation is AnthropicTextBlockParam block
                ? block with { CacheControl = cacheControl }
                : null
        };
        cached.WithCacheControl(cacheControl);
        return cached;
    }

    private static AnthropicCacheControlEphemeral CreateAnthropicCacheControl(string? configuredTtl)
    {
        var ttl = string.IsNullOrWhiteSpace(configuredTtl)
            ? null
            : configuredTtl.Trim();
        return string.IsNullOrWhiteSpace(ttl)
            ? new AnthropicCacheControlEphemeral()
            : new AnthropicCacheControlEphemeral { Ttl = ttl };
    }

    private static string? NormalizeProtocol(string? protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
