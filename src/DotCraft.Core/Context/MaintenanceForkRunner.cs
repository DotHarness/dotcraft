using System.Security.Cryptography;
using System.Text;
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
    string Instructions)
{
    /// <summary>
    /// Optional input-token budget for the maintenance fork. When the estimated
    /// request exceeds this value, the provider request is skipped and a
    /// fallback reason is returned.
    /// </summary>
    public int? InputBudgetTokens { get; init; }

    /// <summary>Machine-readable source for <see cref="InputBudgetTokens"/> diagnostics.</summary>
    public string? InputBudgetSource { get; init; }

    /// <summary>Optional maximum output-token budget for this maintenance task.</summary>
    public int? MaxOutputTokensOverride { get; init; }
}

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

    /// <summary>Provider returned empty assistant text with non-fatal error content.</summary>
    public const string EmptyErrorResponse = "maintenance_empty_error_response";

    /// <summary>True when a compaction snapshot failure should try the trimmed legacy path.</summary>
    public static bool ShouldFallbackToTrimmedCompaction(string? reason) =>
        reason is SnapshotTooLarge or EmptyErrorResponse;
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
    string? CacheMarkerSource = null,
    string? CacheStateKeyKind = null,
    string? CacheStateKeyHash = null,
    string? CacheWriteMode = null,
    bool? TailCacheWriteSkipped = null,
    bool? ProviderImplicitCacheWrite = null)
{
    public static MaintenanceForkCacheDiagnostics None { get; } = new(false);
}

internal sealed record MaintenanceForkPromptCacheState(
    string StateKey,
    string StateKeyHash,
    AppConfig.PromptCachingConfig PromptCaching,
    string Model,
    PromptCacheMarkerStrategy MarkerStrategy,
    string CacheShapeKind,
    string CacheMarkerSource,
    PromptCacheMaintenanceWriteMode CacheWriteMode);

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
        var options = BuildOptions(snapshot, task);
        var sessionKey = ResolveTraceSessionKey(snapshot);
        var maintenancePathKey = BuildMaintenancePathKey(snapshot, task, sessionKey);
        var cacheWriteMode = ResolveCacheWriteMode(toolExecution);
        var promptCacheState = CreatePromptCacheState(snapshot, maintenancePathKey, cacheWriteMode);
        var cacheDiagnostics = MaintenanceForkCacheShaper.Apply(
            snapshot,
            messages,
            options,
            cacheOptions,
            promptCacheState,
            cacheWriteMode);
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
            effectiveBudgetTokens: task.InputBudgetTokens,
            inputBudgetSource: task.InputBudgetSource,
            preflightRejected: task.InputBudgetTokens is > 0
                ? IsOverInputBudget(estimatedInputTokens, task)
                : null,
            cacheShapeApplied: cacheDiagnostics.CacheShapeApplied,
            cacheShapeKind: cacheDiagnostics.CacheShapeKind,
            promptCacheKeyPresent: cacheDiagnostics.PromptCacheKeyPresent,
            cacheMarkerSource: cacheDiagnostics.CacheMarkerSource,
            cacheStateKeyKind: cacheDiagnostics.CacheStateKeyKind,
            cacheStateKeyHash: cacheDiagnostics.CacheStateKeyHash,
            cacheWriteMode: cacheDiagnostics.CacheWriteMode,
            tailCacheWriteSkipped: cacheDiagnostics.TailCacheWriteSkipped,
            providerImplicitCacheWrite: cacheDiagnostics.ProviderImplicitCacheWrite);

        if (IsOverInputBudget(estimatedInputTokens, task))
        {
            traceCollector?.RecordMaintenanceForkResponse(
                sessionKey,
                task.Kind,
                MaintenanceForkFallbackReasons.SnapshotTooLarge,
                providerError: "maintenance fork preflight rejected because the estimated input exceeded the effective maintenance input budget");
            return new MaintenanceForkResult(
                task.Kind,
                null,
                MaintenanceForkFallbackReasons.SnapshotTooLarge,
                null);
        }

        try
        {
            using var runtimeScope = BeginMaintenanceRuntimeScope(
                snapshot,
                sessionKey,
                maintenancePathKey,
                promptCacheState,
                toolExecution);
            var responseClient = CreateResponseClient(toolExecution, promptCacheState);
            var response = await GetResponseAsync(
                responseClient,
                messages,
                options,
                toolExecution,
                cancellationToken);
            TokenUsageSnapshot? usage = response.Usage is null
                ? null
                : TokenUsageExtractor.FromResponse(response);
            var fallbackReason = CompactionTrace.ClassifyFallbackReason(response);
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

    private IChatClient CreateResponseClient(
        MaintenanceForkToolExecutionOptions? toolExecution,
        MaintenanceForkPromptCacheState? promptCacheState)
    {
        var baseClient = CreatePromptCachingClient(promptCacheState);
        if (toolExecution == null)
            return baseClient;

        var invokingClient = new StreamingFunctionInvokingChatClient(baseClient)
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

    private IChatClient CreatePromptCachingClient(MaintenanceForkPromptCacheState? promptCacheState)
    {
        if (promptCacheState == null)
            return chatClient;

        return promptCacheState.MarkerStrategy == PromptCacheMarkerStrategy.AnthropicNative
            ? new PromptCachingChatClient(
                chatClient,
                promptCacheState.PromptCaching,
                promptCacheState.Model,
                PromptCacheMarkerStrategy.AnthropicNative,
                traceCollector)
            : new PromptCachingChatClient(
                chatClient,
                promptCacheState.PromptCaching,
                promptCacheState.Model,
                traceCollector);
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

    private static IDisposable BeginMaintenanceRuntimeScope(
        PromptRequestSnapshot snapshot,
        string sessionKey,
        string maintenancePathKey,
        MaintenanceForkPromptCacheState? promptCacheState,
        MaintenanceForkToolExecutionOptions? toolExecution)
    {
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = sessionKey;
        var callStateKey = toolExecution == null ? null : maintenancePathKey;
        var callStateScope = callStateKey == null ? null : TracingChatClient.UseCallStateKey(callStateKey);
        var promptCacheScope = promptCacheState == null
            ? null
            : PromptCachingChatClient.UseCacheStateKey(
                promptCacheState.StateKey,
                sessionKey,
                new PromptCacheMaintenanceScope(snapshot.Messages.Count, promptCacheState.CacheWriteMode));
        return new MaintenanceRuntimeScope(previousSessionKey, callStateKey, callStateScope, promptCacheScope);
    }

    private static PromptCacheMaintenanceWriteMode ResolveCacheWriteMode(
        MaintenanceForkToolExecutionOptions? toolExecution) =>
        toolExecution == null
            ? PromptCacheMaintenanceWriteMode.ReadOnlyPrefix
            : PromptCacheMaintenanceWriteMode.WriteThrough;

    private static string BuildMaintenancePathKey(
        PromptRequestSnapshot snapshot,
        MaintenanceForkTask task,
        string sessionKey)
    {
        var turnOrRequestId = string.IsNullOrWhiteSpace(snapshot.TurnId)
            ? Guid.NewGuid().ToString("N")[..12]
            : snapshot.TurnId!.Trim();
        return $"{sessionKey}:maintenance:{FormatKind(task.Kind)}:{turnOrRequestId}";
    }

    private sealed class MaintenanceRuntimeScope(
        string? previousSessionKey,
        string? callStateKey,
        IDisposable? callStateScope,
        IDisposable? promptCacheScope) : IDisposable
    {
        public void Dispose()
        {
            promptCacheScope?.Dispose();
            if (callStateKey != null)
                TracingChatClient.ResetCallState(callStateKey);
            callStateScope?.Dispose();
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

    internal static ChatOptions BuildOptions(PromptRequestSnapshot snapshot, MaintenanceForkTask? task = null)
    {
        return new ChatOptions
        {
            Instructions = snapshot.BaseInstructions,
            ModelId = snapshot.ModelId,
            Tools = snapshot.Tools.ToList(),
            Reasoning = snapshot.Reasoning,
            ResponseFormat = snapshot.ResponseFormat,
            MaxOutputTokens = task?.MaxOutputTokensOverride ?? snapshot.MaxOutputTokens,
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

    private MaintenanceForkPromptCacheState? CreatePromptCacheState(
        PromptRequestSnapshot snapshot,
        string maintenancePathKey,
        PromptCacheMaintenanceWriteMode cacheWriteMode)
    {
        if (cacheOptions?.PromptCaching == null)
            return null;

        var model = cacheOptions.Model ?? snapshot.ModelId ?? string.Empty;
        if (!cacheOptions.PromptCaching.ShouldApply(model))
            return null;

        var protocol = MaintenanceForkCacheShaper.NormalizeProtocol(cacheOptions.ProviderProtocol);
        var markerStrategy = protocol switch
        {
            ModelProviderProtocols.Anthropic => PromptCacheMarkerStrategy.AnthropicNative,
            ModelProviderProtocols.OpenAIChatCompletions => PromptCacheMarkerStrategy.OpenAICompatible,
            _ => (PromptCacheMarkerStrategy?)null
        };
        if (!markerStrategy.HasValue)
            return null;

        return new MaintenanceForkPromptCacheState(
            maintenancePathKey,
            ComputeCacheStateKeyHash(maintenancePathKey),
            cacheOptions.PromptCaching,
            model,
            markerStrategy.Value,
            markerStrategy.Value == PromptCacheMarkerStrategy.AnthropicNative
                ? "anthropic-cache-control"
                : "openai-compatible-cache-control",
            markerStrategy.Value == PromptCacheMarkerStrategy.AnthropicNative
                ? CacheMarkerSourceForAnthropic(cacheWriteMode)
                : CacheMarkerSourceForOpenAICompatible(cacheWriteMode),
            cacheWriteMode);
    }

    private static string CacheMarkerSourceForAnthropic(PromptCacheMaintenanceWriteMode cacheWriteMode) =>
        cacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix
            ? "system+snapshot_prefix"
            : "system+snapshot_prefix+fork_tail";

    private static string CacheMarkerSourceForOpenAICompatible(PromptCacheMaintenanceWriteMode cacheWriteMode) =>
        cacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix
            ? "system+snapshot_prefix"
            : "system+snapshot_prefix+fork_tail";

    private static string ComputeCacheStateKeyHash(string cacheStateKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(cacheStateKey));
        return Convert.ToHexString(bytes)[..12];
    }

    private static bool IsOverInputBudget(long estimatedInputTokens, MaintenanceForkTask task) =>
        task.InputBudgetTokens is > 0 && estimatedInputTokens > task.InputBudgetTokens.Value;

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
        MaintenanceForkCacheOptions? cacheOptions,
        MaintenanceForkPromptCacheState? promptCacheState = null,
        PromptCacheMaintenanceWriteMode cacheWriteMode = PromptCacheMaintenanceWriteMode.WriteThrough)
    {
        if (cacheOptions == null)
            return MaintenanceForkCacheDiagnostics.None;

        var protocol = NormalizeProtocol(cacheOptions.ProviderProtocol);
        return protocol switch
        {
            ModelProviderProtocols.Anthropic => ApplyAnthropic(promptCacheState),
            ModelProviderProtocols.OpenAIResponses => ApplyOpenAIResponses(snapshot, options, cacheWriteMode),
            ModelProviderProtocols.OpenAIChatCompletions => ApplyOpenAICompatible(promptCacheState),
            _ => MaintenanceForkCacheDiagnostics.None
        };
    }

    internal static string NormalizeProtocol(string? protocol)
    {
        try
        {
            return ModelProviderProtocols.Normalize(protocol);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static MaintenanceForkCacheDiagnostics ApplyAnthropic(
        MaintenanceForkPromptCacheState? promptCacheState)
    {
        if (promptCacheState == null)
            return MaintenanceForkCacheDiagnostics.None;

        return new MaintenanceForkCacheDiagnostics(
            true,
            promptCacheState.CacheShapeKind,
            PromptCacheKeyPresent: false,
            CacheMarkerSource: promptCacheState.CacheMarkerSource,
            CacheStateKeyKind: "maintenanceFork",
            CacheStateKeyHash: promptCacheState.StateKeyHash,
            CacheWriteMode: FormatCacheWriteMode(promptCacheState.CacheWriteMode),
            TailCacheWriteSkipped: promptCacheState.CacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix,
            ProviderImplicitCacheWrite: false);
    }

    private static MaintenanceForkCacheDiagnostics ApplyOpenAICompatible(
        MaintenanceForkPromptCacheState? promptCacheState)
    {
        if (promptCacheState == null)
            return MaintenanceForkCacheDiagnostics.None;

        return new MaintenanceForkCacheDiagnostics(
            true,
            promptCacheState.CacheShapeKind,
            PromptCacheKeyPresent: null,
            CacheMarkerSource: promptCacheState.CacheMarkerSource,
            CacheStateKeyKind: "maintenanceFork",
            CacheStateKeyHash: promptCacheState.StateKeyHash,
            CacheWriteMode: FormatCacheWriteMode(promptCacheState.CacheWriteMode),
            TailCacheWriteSkipped: promptCacheState.CacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix,
            ProviderImplicitCacheWrite: false);
    }

    private static MaintenanceForkCacheDiagnostics ApplyOpenAIResponses(
        PromptRequestSnapshot snapshot,
        ChatOptions options,
        PromptCacheMaintenanceWriteMode cacheWriteMode)
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
            CacheMarkerSource: "thread",
            CacheWriteMode: "providerImplicit",
            TailCacheWriteSkipped: null,
            ProviderImplicitCacheWrite: true);
    }

    private static string FormatCacheWriteMode(PromptCacheMaintenanceWriteMode cacheWriteMode) =>
        cacheWriteMode == PromptCacheMaintenanceWriteMode.ReadOnlyPrefix
            ? "readOnlyPrefix"
            : "writeThrough";

}
