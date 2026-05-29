using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Context;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

public sealed class TraceCollector(TraceStore store)
{
    private const long PromptCacheDropTokenThreshold = 2000;
    private const double PromptCacheDropRatioThreshold = 0.05;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<string, PromptCacheDiagnosticSessionState> _promptCacheDiagnosticStates = new();

    public void RecordRequest(string sessionKey, string prompt)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Request,
            SessionKey = sessionKey,
            Content = prompt
        });
    }

    public void RecordSessionMetadata(string sessionKey, string? finalSystemPrompt, IEnumerable<string>? toolNames)
    {
        var normalizedToolNames = NormalizeToolNames(toolNames);
        var existing = store.GetSession(sessionKey);
        var previousSystemPromptHash = existing?.SystemPromptHash;
        var previousToolSchemaHash = existing?.ToolSchemaHash;
        var systemPromptHash = ComputeHash(finalSystemPrompt);
        var toolSchemaHash = ComputeHash(string.Join("\n", normalizedToolNames));
        var hasBaseline =
            !string.IsNullOrWhiteSpace(previousSystemPromptHash)
            || !string.IsNullOrWhiteSpace(previousToolSchemaHash);

        string eventKind;
        var changedFields = new List<string>(capacity: 2);
        string[] changedToolNames = [];
        if (!hasBaseline)
        {
            eventKind = PromptCacheEventKinds.Baseline;
        }
        else
        {
            var promptChanged =
                !string.Equals(previousSystemPromptHash, systemPromptHash, StringComparison.Ordinal);
            var toolsChanged =
                !string.Equals(previousToolSchemaHash, toolSchemaHash, StringComparison.Ordinal);

            if (!promptChanged && !toolsChanged)
                return;

            if (promptChanged)
                changedFields.Add(PromptCacheChangedFields.Prompt);
            if (toolsChanged)
                changedFields.Add(PromptCacheChangedFields.Tools);

            changedToolNames = GetAppendedToolNames(existing?.ToolNames ?? [], normalizedToolNames);
            var toolsAppendOnly = toolsChanged
                && changedToolNames.Length > 0
                && IsAppendOnly(existing?.ToolNames ?? [], normalizedToolNames);
            eventKind = !promptChanged && toolsAppendOnly
                ? PromptCacheEventKinds.ToolExtension
                : PromptCacheEventKinds.Drift;
        }

        if (eventKind == PromptCacheEventKinds.Baseline && string.IsNullOrWhiteSpace(systemPromptHash) && string.IsNullOrWhiteSpace(toolSchemaHash))
            return;

        store.Record(new TraceEvent
        {
            Type = TraceEventType.SessionMetadata,
            SessionKey = sessionKey,
            FinalSystemPrompt = finalSystemPrompt,
            ToolNames = normalizedToolNames,
            SystemPromptHash = systemPromptHash,
            ToolSchemaHash = toolSchemaHash,
            PromptDriftDetected = eventKind == PromptCacheEventKinds.Drift,
            PromptCacheEventKind = eventKind,
            PromptCacheChangedFields = changedFields.ToArray(),
            PreviousSystemPromptHash = previousSystemPromptHash,
            PreviousToolSchemaHash = previousToolSchemaHash,
            CurrentSystemPromptHash = systemPromptHash,
            CurrentToolSchemaHash = toolSchemaHash,
            ChangedToolNames = changedToolNames
        });
    }

    public void RecordResponse(string sessionKey, string? response, DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Response,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = response ?? "(empty)"
        });
    }

    public void RecordResponse(
        string sessionKey,
        string? response,
        string? responseId,
        string? messageId,
        string? modelId,
        string? finishReason,
        object? metadata = null,
        DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Response,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = response ?? "(empty)",
            ResponseId = responseId,
            MessageId = messageId,
            ModelId = modelId,
            FinishReason = finishReason,
            MetadataJson = SerializeMetadata(metadata)
        });
    }

    public void RecordToolCallStarted(string sessionKey, FunctionCallContent fc)
    {
        string? argsJson = null;
        if (fc.Arguments != null)
        {
            try
            {
                argsJson = JsonSerializer.Serialize(fc.Arguments, JsonOptions);
            }
            catch
            {
                argsJson = fc.Arguments.ToString();
            }
        }

        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolCallStarted,
            SessionKey = sessionKey,
            ToolName = fc.Name,
            ToolIcon = ToolRegistry.GetToolIcon(fc.Name),
            ToolArguments = argsJson,
            Content = fc.CallId,
            CallId = fc.CallId
        });
    }

    public void RecordToolCallCompleted(string sessionKey, FunctionResultContent fr, string? toolName, double durationMs)
    {
        var result = Agents.ImageContentSanitizingChatClient.DescribeResult(fr.Result);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolCallCompleted,
            SessionKey = sessionKey,
            ToolName = toolName ?? "unknown",
            ToolIcon = ToolRegistry.GetToolIcon(toolName ?? ""),
            ToolResult = result,
            DurationMs = durationMs,
            Content = fr.CallId,
            CallId = fr.CallId
        });
    }

    public void RecordToolInjection(string sessionKey, IReadOnlyList<string> toolNames)
    {
        var normalizedToolNames = NormalizeToolNames(toolNames);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolInjection,
            SessionKey = sessionKey,
            ToolName = $"{normalizedToolNames.Length} tool{(normalizedToolNames.Length != 1 ? "s" : "")} injected",
            ToolIcon = "🔌",
            Content = string.Join(", ", normalizedToolNames),
            PromptCacheEventKind = PromptCacheEventKinds.ToolExtension,
            PromptCacheChangedFields = [PromptCacheChangedFields.Tools],
            ChangedToolNames = normalizedToolNames
        });
    }

    /// <summary>
    /// Records native deferred tool loading activation without marking the prompt cache as a tool extension.
    /// </summary>
    public void RecordDeferredToolLoading(
        string sessionKey,
        IReadOnlyList<DeferredToolLoadingTraceTool> tools,
        string strategy,
        string effectiveMode,
        string providerProtocol,
        string trigger,
        string query,
        int deferredToolCount,
        int requestedMaxResults,
        int maxSearchResults)
    {
        var normalizedTools = tools
            .Where(static tool => !string.IsNullOrWhiteSpace(tool.Name))
            .GroupBy(static tool => tool.Name.Trim(), StringComparer.Ordinal)
            .Select(static group =>
            {
                var tool = group.Last();
                return new DeferredToolLoadingTraceTool(
                    group.Key,
                    NormalizeOptional(tool.Source),
                    NormalizeOptional(tool.Namespace));
            })
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedTools.Length == 0)
            return;

        var normalizedToolNames = normalizedTools.Select(static tool => tool.Name).ToArray();
        store.Record(new TraceEvent
        {
            Type = TraceEventType.DeferredToolLoading,
            SessionKey = sessionKey,
            ToolName = $"{normalizedToolNames.Length} deferred tool{(normalizedToolNames.Length != 1 ? "s" : "")} activated",
            ToolIcon = "🔎",
            Content = string.Join(", ", normalizedToolNames),
            ChangedToolNames = normalizedToolNames,
            MetadataJson = SerializeMetadata(new
            {
                strategy,
                effectiveMode,
                providerProtocol,
                trigger,
                query,
                deferredToolCount,
                requestedMaxResults,
                maxSearchResults,
                tools = normalizedTools.Select(static tool => new
                {
                    name = tool.Name,
                    source = tool.Source,
                    @namespace = tool.Namespace
                }).ToArray()
            })
        });
    }

    public void RecordPromptCachePoints(
        string sessionKey,
        string model,
        IReadOnlyList<PromptCachePointTraceEntry> points,
        int? llmCallIndex = null)
    {
        if (points.Count == 0)
            return;

        store.Record(new TraceEvent
        {
            Type = TraceEventType.PromptCachePoint,
            SessionKey = sessionKey,
            Content = $"{points.Count} prompt cache point{(points.Count == 1 ? "" : "s")}",
            ModelId = model,
            LlmCallIndex = llmCallIndex,
            MetadataJson = JsonSerializer.Serialize(new
            {
                sessionKey,
                model,
                llmCallIndex,
                points
            }, JsonOptions)
        });
    }

    internal void RecordPromptCacheRequestSnapshot(
        string sessionKey,
        PromptCacheRequestDiagnosticSnapshot snapshot)
    {
        if (snapshot.LlmCallIndex <= 0)
            return;

        var state = _promptCacheDiagnosticStates.GetOrAdd(sessionKey, static _ => new PromptCacheDiagnosticSessionState());
        lock (state.Gate)
        {
            state.PendingRequests[snapshot.LlmCallIndex] = snapshot;

            if (state.PendingRequests.Count > 64)
            {
                foreach (var key in state.PendingRequests.Keys.OrderBy(static key => key).Take(state.PendingRequests.Count - 64).ToArray())
                    state.PendingRequests.Remove(key);
            }
        }
    }

    public void RecordMaintenanceForkRequest(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        string prompt,
        string? threadId,
        string? turnId,
        string? mode,
        string? modelId,
        string? providerId,
        int snapshotMessageCount,
        int extraTailMessageCount,
        IReadOnlyList<AITool>? tools,
        string? baseInstructionsFingerprint,
        string? toolFingerprint,
        long? estimatedInputTokens = null,
        string? snapshotSource = null,
        string? snapshotInvalidReason = null,
        bool? cacheShapeApplied = null,
        string? cacheShapeKind = null,
        bool? promptCacheKeyPresent = null,
        string? cacheMarkerSource = null,
        string? cacheStateKeyKind = null,
        string? cacheStateKeyHash = null)
    {
        var toolNames = NormalizeToolNames(tools?.Select(static tool => tool.Name ?? string.Empty));
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkRequest,
            SessionKey = sessionKey,
            Content = prompt,
            ToolName = FormatMaintenanceKind(taskKind),
            ModelId = modelId,
            ToolNames = toolNames,
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                threadId,
                turnId,
                mode,
                modelId,
                providerId,
                snapshotMessageCount,
                extraTailMessageCount,
                toolCount = toolNames.Length,
                toolNames,
                baseInstructionsFingerprint,
                toolFingerprint,
                estimatedInputTokens,
                snapshotSource,
                snapshotInvalidReason,
                cacheShapeApplied,
                cacheShapeKind,
                promptCacheKeyPresent,
                cacheMarkerSource,
                cacheStateKeyKind,
                cacheStateKeyHash
            })
        });
    }

    public void RecordMaintenanceForkResponse(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        ChatResponse? response,
        string? fallbackReason)
    {
        var text = response?.Text;
        var usage = response?.Usage is null
            ? (TokenUsageSnapshot?)null
            : TokenUsageExtractor.FromResponse(response);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkResponse,
            SessionKey = sessionKey,
            Content = string.IsNullOrWhiteSpace(text) ? "(empty)" : text,
            ToolName = FormatMaintenanceKind(taskKind),
            ResponseId = response?.ResponseId,
            ModelId = response?.ModelId,
            FinishReason = response?.FinishReason?.ToString(),
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                fallbackReason,
                responseMessages = DescribeMessages(response?.Messages),
                usage
            })
        });
    }

    public void RecordMaintenanceForkResponse(
        string sessionKey,
        MaintenanceForkTaskKind taskKind,
        string fallbackReason,
        string? providerError = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.MaintenanceForkResponse,
            SessionKey = sessionKey,
            Content = "(empty)",
            ToolName = FormatMaintenanceKind(taskKind),
            MetadataJson = SerializeMetadata(new
            {
                taskKind = FormatMaintenanceKind(taskKind),
                fallbackReason,
                providerError,
                responseMessages = Array.Empty<object>()
            })
        });
    }

    public void RecordTokenUsage(string sessionKey, long inputTokens, long outputTokens)
        => RecordTokenUsage(sessionKey, new TokenUsageSnapshot(inputTokens, outputTokens, 0, 0));

    public void RecordTokenUsage(
        string sessionKey,
        TokenUsageSnapshot usage,
        int? requestIndex = null,
        DateTimeOffset? timestamp = null)
    {
        var llmCallIndex = (store.GetSession(sessionKey)?.TokenUsageCount ?? 0) + 1;
        var timestampValue = timestamp ?? DateTimeOffset.UtcNow;
        RecordPromptCacheDiagnostic(sessionKey, usage, requestIndex, llmCallIndex, timestampValue);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.TokenUsage,
            SessionKey = sessionKey,
            Timestamp = timestampValue,
            RequestIndex = requestIndex,
            LlmCallIndex = llmCallIndex,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheWriteInputTokens = usage.CacheWriteInputTokens,
            FreshInputTokens = usage.FreshInputTokens,
            NonCachedInputTokens = usage.NonCachedInputTokens,
            ReasoningOutputTokens = usage.ReasoningOutputTokens,
            TotalTokens = usage.TotalTokens
        });
    }

    private void RecordPromptCacheDiagnostic(
        string sessionKey,
        TokenUsageSnapshot usage,
        int? requestIndex,
        int llmCallIndex,
        DateTimeOffset timestamp)
    {
        if (!_promptCacheDiagnosticStates.TryGetValue(sessionKey, out var state))
            return;

        TraceEvent? evt;
        lock (state.Gate)
        {
            state.PendingRequests.Remove(llmCallIndex, out var request);
            if (request == null)
            {
                state.PreviousCachedInputTokens = usage.CachedInputTokens;
                state.PreviousUsageAt = timestamp;
                return;
            }

            var previousCachedInputTokens = state.PreviousCachedInputTokens;
            var previousUsageAt = state.PreviousUsageAt;
            var previousSystemHash = state.PreviousSystemHash;
            var previousToolSchemaHash = state.PreviousToolSchemaHash;
            var previousSelectedSignature = state.PreviousSelectedSignature;
            var classification = ClassifyPromptCacheUsage(
                request,
                usage,
                previousCachedInputTokens,
                previousUsageAt,
                timestamp,
                previousSystemHash,
                previousToolSchemaHash,
                previousSelectedSignature);

            evt = CreatePromptCacheDiagnosticEvent(
                sessionKey,
                usage,
                requestIndex,
                llmCallIndex,
                timestamp,
                request,
                classification,
                previousCachedInputTokens,
                previousUsageAt,
                previousSystemHash,
                previousToolSchemaHash,
                previousSelectedSignature);

            state.PreviousCachedInputTokens = usage.CachedInputTokens;
            state.PreviousUsageAt = timestamp;
            state.PreviousSystemHash = request.SystemHash ?? state.PreviousSystemHash;
            state.PreviousToolSchemaHash = request.ToolSchemaHash ?? state.PreviousToolSchemaHash;
            state.PreviousSelectedSignature = BuildSelectedPointSignature(request);
        }

        if (evt != null)
            store.Record(evt);
    }

    private static PromptCacheDiagnosticClassification ClassifyPromptCacheUsage(
        PromptCacheRequestDiagnosticSnapshot request,
        TokenUsageSnapshot usage,
        long? previousCachedInputTokens,
        DateTimeOffset? previousUsageAt,
        DateTimeOffset timestamp,
        string? previousSystemHash,
        string? previousToolSchemaHash,
        string? previousSelectedSignature)
    {
        var ttlThreshold = ResolveTtlThreshold(request.Ttl);
        if (!previousCachedInputTokens.HasValue)
            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.ColdStart, isCacheBreak: false, ttlThreshold: ttlThreshold);

        var previousRead = previousCachedInputTokens.Value;
        var currentRead = usage.CachedInputTokens;
        var drop = Math.Max(0, previousRead - currentRead);
        var dropRatio = previousRead > 0
            ? drop / (double)previousRead
            : 0;
        var significantDrop = previousRead > 0
            && drop >= PromptCacheDropTokenThreshold
            && dropRatio > PromptCacheDropRatioThreshold;

        var promptChanged = HasChanged(previousSystemHash, request.SystemHash);
        var toolsChanged = HasChanged(previousToolSchemaHash, request.ToolSchemaHash);
        var changedFields = new List<string>(capacity: 2);
        if (promptChanged)
            changedFields.Add(PromptCacheChangedFields.Prompt);
        if (toolsChanged)
            changedFields.Add(PromptCacheChangedFields.Tools);

        var elapsed = previousUsageAt.HasValue
            ? timestamp - previousUsageAt.Value
            : (TimeSpan?)null;
        var ttlPossible = elapsed.HasValue && elapsed.Value >= ttlThreshold;
        var selectedSignature = BuildSelectedPointSignature(request);
        var selectedChanged = !string.IsNullOrWhiteSpace(previousSelectedSignature)
            && !string.Equals(previousSelectedSignature, selectedSignature, StringComparison.Ordinal);
        var hasNewPrefix = request.LatestSelectedPointIsNew
            || request.NewSelectedCount > 0
            || usage.CacheWriteInputTokens > 0;

        if (!significantDrop)
        {
            if (currentRead > 0)
                return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.CacheHitStable, isCacheBreak: false, drop, dropRatio, elapsed, ttlThreshold);

            if (hasNewPrefix)
                return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.WarmWriteOrNewPrefix, isCacheBreak: false, drop, dropRatio, elapsed, ttlThreshold);

            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.ColdStart, isCacheBreak: false, drop, dropRatio, elapsed, ttlThreshold);
        }

        if (changedFields.Count > 0)
            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.PromptOrToolsChanged, isCacheBreak: true, drop, dropRatio, elapsed, ttlThreshold, changedFields);

        if (ttlPossible)
            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.TtlPossible, isCacheBreak: true, drop, dropRatio, elapsed, ttlThreshold);

        if (hasNewPrefix && previousRead <= 0)
            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.WarmWriteOrNewPrefix, isCacheBreak: false, drop, dropRatio, elapsed, ttlThreshold);

        if (selectedChanged && currentRead > 0)
            return PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.CacheReadDrop, isCacheBreak: true, drop, dropRatio, elapsed, ttlThreshold);

        return currentRead == 0
            ? PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.LikelyServerSide, isCacheBreak: true, drop, dropRatio, elapsed, ttlThreshold)
            : PromptCacheDiagnosticClassification.Create(PromptCacheDiagnosticKinds.CacheReadDrop, isCacheBreak: true, drop, dropRatio, elapsed, ttlThreshold);
    }

    private static TraceEvent CreatePromptCacheDiagnosticEvent(
        string sessionKey,
        TokenUsageSnapshot usage,
        int? requestIndex,
        int llmCallIndex,
        DateTimeOffset timestamp,
        PromptCacheRequestDiagnosticSnapshot request,
        PromptCacheDiagnosticClassification classification,
        long? previousCachedInputTokens,
        DateTimeOffset? previousUsageAt,
        string? previousSystemHash,
        string? previousToolSchemaHash,
        string? previousSelectedSignature)
    {
        var label = FormatPromptCacheClassificationLabel(classification.Kind);
        var requestSuffix = requestIndex.HasValue
            ? $" request {requestIndex.Value}"
            : string.Empty;
        var previousReadText = previousCachedInputTokens.HasValue
            ? previousCachedInputTokens.Value.ToString("N0")
            : "-";
        var content = $"LLM #{llmCallIndex}{requestSuffix}: {label}, cached={usage.CachedInputTokens:N0}, previous read={previousReadText}";
        var currentSelectedSignature = BuildSelectedPointSignature(request);
        var selectedChanged = !string.IsNullOrWhiteSpace(previousSelectedSignature)
            && !string.Equals(previousSelectedSignature, currentSelectedSignature, StringComparison.Ordinal);
        var promptChanged = HasChanged(previousSystemHash, request.SystemHash);
        var toolsChanged = HasChanged(previousToolSchemaHash, request.ToolSchemaHash);

        return new TraceEvent
        {
            Type = TraceEventType.PromptCacheDiagnostic,
            SessionKey = sessionKey,
            Timestamp = timestamp,
            Content = content,
            ModelId = request.Model,
            RequestIndex = requestIndex,
            LlmCallIndex = llmCallIndex,
            InputTokens = usage.InputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheWriteInputTokens = usage.CacheWriteInputTokens,
            FreshInputTokens = usage.FreshInputTokens,
            NonCachedInputTokens = usage.NonCachedInputTokens,
            PromptCacheEventKind = classification.Kind,
            PromptCacheChangedFields = classification.ChangedFields.Length > 0 ? classification.ChangedFields : null,
            PromptDriftDetected = string.Equals(
                classification.Kind,
                PromptCacheDiagnosticKinds.PromptOrToolsChanged,
                StringComparison.Ordinal),
            PromptCacheBreakDetected = classification.IsCacheBreak,
            PreviousSystemPromptHash = previousSystemHash,
            PreviousToolSchemaHash = previousToolSchemaHash,
            CurrentSystemPromptHash = request.SystemHash,
            CurrentToolSchemaHash = request.ToolSchemaHash,
            MetadataJson = SerializeMetadata(new
            {
                sessionKey,
                model = request.Model,
                markerStrategy = request.MarkerStrategy,
                ttl = request.Ttl,
                llmCallIndex,
                requestIndex,
                classification = classification.Kind,
                isCacheBreak = classification.IsCacheBreak,
                inputTokens = usage.InputTokens,
                cachedInputTokens = usage.CachedInputTokens,
                cacheWriteInputTokens = usage.CacheWriteInputTokens,
                freshInputTokens = usage.FreshInputTokens,
                nonCachedInputTokens = usage.NonCachedInputTokens,
                previousCachedInputTokens,
                cacheReadDropTokens = classification.DropTokens,
                cacheReadDropRatio = classification.DropRatio,
                previousUsageAt = previousUsageAt?.ToString("O"),
                timeSincePreviousUsageMs = classification.Elapsed?.TotalMilliseconds,
                ttlThresholdMs = classification.TtlThreshold.TotalMilliseconds,
                promptChanged,
                toolsChanged,
                selectedChanged,
                previousSelectedSignature,
                currentSelectedSignature,
                systemHash = request.SystemHash,
                previousSystemHash,
                toolSchemaHash = request.ToolSchemaHash,
                previousToolSchemaHash,
                breakpointCount = request.BreakpointCount,
                candidateCount = request.CandidateCount,
                newSelectedCount = request.NewSelectedCount,
                rememberedSelectedCount = request.RememberedSelectedCount,
                latestSelectedPointIsNew = request.LatestSelectedPointIsNew,
                toolCount = request.ToolCount,
                selectedPoints = request.SelectedPoints.Select(static point => new
                {
                    role = point.Role,
                    kind = point.ContentKind,
                    messageIndex = point.MessageIndex,
                    contentIndex = point.ContentIndex,
                    sequence = point.Sequence,
                    hashPrefix = point.HashPrefix,
                    remembered = point.Remembered,
                    latest = point.Latest
                }).ToArray(),
                candidateCounts = request.CandidateCounts.Select(static count => new
                {
                    role = count.Role,
                    kind = count.ContentKind,
                    count = count.Count
                }).ToArray()
            })
        };
    }

    public void RecordError(string sessionKey, string error)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Error,
            SessionKey = sessionKey,
            Content = error
        });
    }

    public void RecordContextCompaction(string sessionKey)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ContextCompaction,
            SessionKey = sessionKey,
            Content = "Context compacted due to token limit"
        });
    }

    public void RecordThinking(string sessionKey, string content, DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Thinking,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = content
        });
    }

    public void BindThreadMainSession(string threadId, DateTimeOffset? createdAt = null)
        => store.BindThreadMainSession(threadId, createdAt);

    public void BindChildSession(
        string sessionKey,
        string rootThreadId,
        string parentSessionKey,
        DateTimeOffset? createdAt = null)
        => store.BindChildSession(sessionKey, rootThreadId, parentSessionKey, createdAt);

    public string? ResolveRootThreadId(string sessionKey)
        => store.DescribeSessionDeletion(sessionKey).RootThreadId;

    public int GetTokenUsageCount(string sessionKey)
        => store.GetSession(sessionKey)?.TokenUsageCount ?? 0;

    public ToolCallTimer StartToolTimer()
    {
        return new ToolCallTimer();
    }

    private static string? SerializeMetadata(object? metadata)
    {
        if (metadata == null)
            return null;

        try
        {
            return JsonSerializer.Serialize(metadata, JsonOptions);
        }
        catch
        {
            return metadata.ToString();
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool HasChanged(string? previous, string? current) =>
        (!string.IsNullOrWhiteSpace(previous) || !string.IsNullOrWhiteSpace(current))
        && !string.Equals(previous, current, StringComparison.Ordinal);

    private static TimeSpan ResolveTtlThreshold(string? ttl)
    {
        if (string.IsNullOrWhiteSpace(ttl))
            return TimeSpan.FromMinutes(5);

        var value = ttl.Trim();
        if (value.EndsWith('h') &&
            double.TryParse(value[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours) &&
            hours > 0)
        {
            return TimeSpan.FromHours(hours);
        }

        if (value.EndsWith('m') &&
            double.TryParse(value[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes) &&
            minutes > 0)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        return TimeSpan.FromMinutes(5);
    }

    private static string BuildSelectedPointSignature(PromptCacheRequestDiagnosticSnapshot request) =>
        string.Join(
            "|",
            request.SelectedPoints
                .OrderBy(static point => point.Sequence)
                .ThenBy(static point => point.Role, StringComparer.Ordinal)
                .ThenBy(static point => point.ContentKind, StringComparer.Ordinal)
                .Select(static point => $"{point.Role}:{point.ContentKind}:{point.HashPrefix}:{point.Remembered}:{point.Latest}"));

    private static string FormatPromptCacheClassificationLabel(string kind) => kind switch
    {
        PromptCacheDiagnosticKinds.ColdStart => "cold start",
        PromptCacheDiagnosticKinds.WarmWriteOrNewPrefix => "warm/new prefix",
        PromptCacheDiagnosticKinds.CacheHitStable => "cache hit stable",
        PromptCacheDiagnosticKinds.CacheReadDrop => "cache read drop",
        PromptCacheDiagnosticKinds.TtlPossible => "possible TTL",
        PromptCacheDiagnosticKinds.PromptOrToolsChanged => "prompt/tools changed",
        PromptCacheDiagnosticKinds.LikelyServerSide => "likely server-side",
        _ => kind
    };

    private static string FormatMaintenanceKind(MaintenanceForkTaskKind kind) => kind switch
    {
        MaintenanceForkTaskKind.ContextCompaction => "context_compaction",
        MaintenanceForkTaskKind.MemoryConsolidation => "memory_consolidation",
        _ => kind.ToString()
    };

    private static object[] DescribeMessages(IList<ChatMessage>? messages)
    {
        if (messages is not { Count: > 0 })
            return [];

        return messages
            .Select((message, index) => new
            {
                index,
                role = message.Role.ToString(),
                messageId = message.MessageId,
                authorName = message.AuthorName,
                text = message.Text,
                contents = message.Contents.Select(DescribeContent).ToArray()
            })
            .Cast<object>()
            .ToArray();
    }

    private static object DescribeContent(AIContent content) => content switch
    {
        TextContent text => new
        {
            type = "text",
            text = text.Text
        },
        FunctionCallContent call => new
        {
            type = "function_call",
            callId = call.CallId,
            name = call.Name,
            arguments = SerializeMetadata(call.Arguments)
        },
        FunctionResultContent result => new
        {
            type = "function_result",
            callId = result.CallId,
            result = Agents.ImageContentSanitizingChatClient.DescribeResult(result.Result),
            exception = result.Exception?.Message
        },
        DataContent data => new
        {
            type = "data",
            data.MediaType
        },
        UriContent uri => new
        {
            type = "uri",
            uri = uri.Uri?.ToString(),
            uri.MediaType
        },
        _ => new
        {
            type = content.GetType().Name,
            text = content.ToString()
        }
    };

    private static string[] NormalizeToolNames(IEnumerable<string>? toolNames)
    {
        if (toolNames == null)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return toolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Where(seen.Add)
            .ToArray();
    }

    private static bool IsAppendOnly(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        if (previous.Count == 0 || current.Count <= previous.Count)
            return false;

        for (var i = 0; i < previous.Count; i++)
        {
            if (!string.Equals(previous[i], current[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string[] GetAppendedToolNames(IReadOnlyList<string> previous, IReadOnlyList<string> current)
    {
        if (!IsAppendOnly(previous, current))
            return [];

        return current.Skip(previous.Count).ToArray();
    }

    private static string? ComputeHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class PromptCacheDiagnosticSessionState
    {
        public object Gate { get; } = new();

        public Dictionary<int, PromptCacheRequestDiagnosticSnapshot> PendingRequests { get; } = new();

        public long? PreviousCachedInputTokens { get; set; }

        public DateTimeOffset? PreviousUsageAt { get; set; }

        public string? PreviousSystemHash { get; set; }

        public string? PreviousToolSchemaHash { get; set; }

        public string? PreviousSelectedSignature { get; set; }
    }

    private sealed record PromptCacheDiagnosticClassification(
        string Kind,
        bool IsCacheBreak,
        long DropTokens,
        double DropRatio,
        TimeSpan? Elapsed,
        TimeSpan TtlThreshold,
        string[] ChangedFields)
    {
        public static PromptCacheDiagnosticClassification Create(
            string kind,
            bool isCacheBreak,
            long dropTokens = 0,
            double dropRatio = 0,
            TimeSpan? elapsed = null,
            TimeSpan? ttlThreshold = null,
            IReadOnlyList<string>? changedFields = null) =>
            new(
                kind,
                isCacheBreak,
                dropTokens,
                dropRatio,
                elapsed,
                ttlThreshold ?? TimeSpan.FromMinutes(5),
                changedFields?
                    .Where(static field => !string.IsNullOrWhiteSpace(field))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? []);
    }
}

public sealed class ToolCallTimer
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;

    public void Stop() => _stopwatch.Stop();
}
