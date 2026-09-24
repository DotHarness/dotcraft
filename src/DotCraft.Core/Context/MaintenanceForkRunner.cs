using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
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
    ContextCompaction
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
    string CacheShapeKind,
    string CacheMarkerSource);

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
        var parentContext = ProviderRequestContextScope.Current;
        var identity = parentContext?.CurrentIdentity ?? new ProviderConversationIdentity(
            snapshot.ThreadId ?? "maintenance", snapshot.ThreadId ?? "maintenance", null, null,
            snapshot.TurnId, Guid.CreateVersion7().ToString(), ProviderRequestKind.Compaction, 0, "maintenance", null);
        using var auxiliaryScope = new AuxiliaryProviderRequestScope(identity with
        {
            RequestKind = ProviderRequestKind.Compaction,
            TurnId = snapshot.TurnId ?? identity.TurnId
        }, parentContext?.Diagnostics ?? traceCollector);
        using var retryScope = ModelStreamRetryRuntimeScope.Suppress();
        var messages = BuildMessages(snapshot, task, messagesBeforeTask).ToList();
        var options = BuildOptions(snapshot, task);
        var sessionKey = ResolveTraceSessionKey(snapshot);
        var maintenancePathKey = BuildMaintenancePathKey(snapshot, task, sessionKey);
        var promptCacheState = CreatePromptCacheState(snapshot, maintenancePathKey);
        var cacheDiagnostics = MaintenanceForkCacheShaper.Apply(
            snapshot,
            options,
            cacheOptions,
            promptCacheState);
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
            using var runtimeScope = BeginMaintenanceRuntimeScope(snapshot, sessionKey, promptCacheState);
            var response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
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

    private static IDisposable BeginMaintenanceRuntimeScope(
        PromptRequestSnapshot snapshot,
        string sessionKey,
        MaintenanceForkPromptCacheState? promptCacheState)
    {
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = sessionKey;
        var promptCacheScope = promptCacheState == null
            ? null
            : PromptCacheStateScope.Use(
                promptCacheState.StateKey,
                sessionKey,
                new PromptCacheMaintenanceScope(snapshot.Messages.Count));
        return new MaintenanceRuntimeScope(previousSessionKey, promptCacheScope);
    }

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
        IDisposable? promptCacheScope) : IDisposable
    {
        public void Dispose()
        {
            promptCacheScope?.Dispose();
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
        string maintenancePathKey)
    {
        if (cacheOptions?.PromptCaching == null)
            return null;

        var model = cacheOptions.Model ?? snapshot.ModelId ?? string.Empty;
        if (!cacheOptions.PromptCaching.ShouldApply(model))
            return null;

        var protocol = MaintenanceForkCacheShaper.NormalizeProtocol(cacheOptions.ProviderProtocol);
        if (protocol != ModelProviderProtocols.Anthropic)
            return null;

        return new MaintenanceForkPromptCacheState(
            maintenancePathKey,
            ComputeCacheStateKeyHash(maintenancePathKey),
            "anthropic-cache-control",
            "system+snapshot_prefix");
    }

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
        ChatOptions options,
        MaintenanceForkCacheOptions? cacheOptions,
        MaintenanceForkPromptCacheState? promptCacheState = null)
    {
        if (cacheOptions == null)
            return MaintenanceForkCacheDiagnostics.None;

        var protocol = NormalizeProtocol(cacheOptions.ProviderProtocol);
        return protocol switch
        {
            ModelProviderProtocols.Anthropic => ApplyAnthropic(promptCacheState),
            ModelProviderProtocols.OpenAIResponses => ApplyOpenAIResponses(snapshot, options),
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
            CacheWriteMode: "readOnlyPrefix",
            TailCacheWriteSkipped: true,
            ProviderImplicitCacheWrite: false);
    }

    private static MaintenanceForkCacheDiagnostics ApplyOpenAIResponses(
        PromptRequestSnapshot snapshot,
        ChatOptions options)
    {
        var promptCacheKey = ProviderPromptCacheMetadata.ResolveKey(
            options,
            snapshot.ThreadId);
        if (string.IsNullOrWhiteSpace(promptCacheKey))
            return MaintenanceForkCacheDiagnostics.None;

        ProviderPromptCacheMetadata.ApplyKey(options, promptCacheKey);

        return new MaintenanceForkCacheDiagnostics(
            true,
            "openai-responses-prompt-cache-key",
            PromptCacheKeyPresent: true,
            CacheMarkerSource: "thread",
            CacheWriteMode: "providerImplicit",
            TailCacheWriteSkipped: null,
            ProviderImplicitCacheWrite: true);
    }
}
