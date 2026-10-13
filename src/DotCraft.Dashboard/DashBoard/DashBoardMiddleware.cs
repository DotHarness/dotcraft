using DotCraft.Workspaces;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Modules;
using DotCraft.Tracing;
using DotCraft.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions;
using ConfigSchemaSection = DotCraft.Configuration.ConfigSchemaSection;

namespace DotCraft.DashBoard;

public static class DashBoardMiddleware
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void MapDashBoard(
        this IEndpointRouteBuilder endpoints,
        TraceStore traceStore,
        DotCraftPaths paths,
        TokenUsageStore? tokenUsageStore = null,
        IEnumerable<IOrchestratorSnapshotProvider>? orchestratorProviders = null,
        IReadOnlyList<ConfigSchemaSection>? configSchema = null,
        SessionPersistenceService? persistence = null,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        IDashBoardSessionHandler? sessionHandler = null,
        bool refreshTraceFromDiskBeforeRead = false,
        DreamsService? dreamsService = null,
        DashBoardRuntimeOptions? runtimeOptions = null,
        ConfigurationService? configuration = null)
    {
        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("DashBoard");
        var runtime = runtimeOptions ?? DashBoardRuntimeOptions.Interactive();
        var capturedOrchestrators = orchestratorProviders?.ToList();
        var settingsConfiguration = runtime.Capabilities.Settings ? configuration : null;
        var dreamsAvailable = runtime.Capabilities.Dreams && dreamsService != null;
        var automationsAvailable = runtime.Capabilities.Automations && capturedOrchestrators is { Count: > 0 };
        var threadOperationStore = new DashBoardThreadOperationStore(paths.Data.RootPath);
        var runtimeCapabilities = new
        {
            settings = settingsConfiguration != null,
            dreams = dreamsAvailable,
            automations = automationsAvailable,
            sessionDeletion = runtime.Capabilities.SessionDeletion
        };

        var traceRefreshLock = new object();
        var lastTraceRefreshAt = DateTimeOffset.MinValue;

        void RefreshTraceFromDiskIfEnabled()
        {
            if (refreshTraceFromDiskBeforeRead)
            {
                var now = DateTimeOffset.UtcNow;
                if (now - lastTraceRefreshAt < TimeSpan.FromMilliseconds(500))
                    return;

                lock (traceRefreshLock)
                {
                    now = DateTimeOffset.UtcNow;
                    if (now - lastTraceRefreshAt < TimeSpan.FromMilliseconds(500))
                        return;

                    traceStore.RefreshFromDisk();
                    lastTraceRefreshAt = now;
                }
            }
        }

        if (automationsAvailable)
            MapOrchestratorEndpoints(endpoints, capturedOrchestrators);

        endpoints.MapGet("/dashboard/", ctx =>
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            return ctx.Response.WriteAsync(DashBoardFrontend.GetHtml());
        });

        endpoints.MapGet("/dashboard/api/runtime", () => Results.Json(new
        {
            mode = runtime.Mode,
            readOnly = runtime.ReadOnly,
            workspacePath = paths.WorkspacePath,
            capabilities = runtimeCapabilities
        }, JsonOptions));

        endpoints.MapGet("/dashboard/api/summary", () =>
        {
            RefreshTraceFromDiskIfEnabled();
            var summary = traceStore.GetSummary();
            return Results.Json(summary, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions", () =>
        {
            RefreshTraceFromDiskIfEnabled();
            var sessions = traceStore.GetSessions();
            var sessionKeys = sessions.Select(s => s.SessionKey).ToArray();
            var descriptors = persistence?.DescribeSessionDeletions(sessions.Select(s => s.SessionKey))
                              ?? new Dictionary<string, TraceSessionDeletionDescriptor>(StringComparer.Ordinal);
            var relationships = traceStore.DescribeSessionRelationships(sessionKeys);
            var prefixDiagnostics = traceStore.GetLatestEvents(
                sessionKeys,
                TraceEventType.SubAgentPrefixDiagnostic);
            var result = sessions.Select(s =>
            {
                descriptors.TryGetValue(s.SessionKey, out var descriptor);
                relationships.TryGetValue(s.SessionKey, out var relationship);
                prefixDiagnostics.TryGetValue(s.SessionKey, out var prefixDiagnostic);
                var rootThreadId = descriptor?.RootThreadId ?? s.SessionKey;
                return new
                {
                    s.SessionKey,
                    startedAt = s.StartedAt.ToString("o"),
                    lastActivityAt = s.LastActivityAt.ToString("o"),
                    s.TotalInputTokens,
                    s.TotalOutputTokens,
                    s.TotalCachedInputTokens,
                    s.TotalCacheWriteInputTokens,
                    s.TotalFreshInputTokens,
                    s.TotalNonCachedInputTokens,
                    s.TotalReasoningOutputTokens,
                    s.CacheHitRate,
                    totalTokens = s.TotalInputTokens + s.TotalOutputTokens,
                    s.RequestCount,
                    s.MaintenanceForkRequestCount,
                    s.ResponseCount,
                    s.MaintenanceForkResponseCount,
                    s.ToolCallCount,
                    s.ErrorCount,
                    s.ContextCompactionCount,
                    llmCallCount = s.TokenUsageCount,
                    totalToolDurationMs = s.TotalToolDurationMs,
                    avgToolDurationMs = s.AvgToolDurationMs,
                    maxToolDurationMs = s.MaxToolDurationMs,
                    firstUserRequest = s.FirstUserRequest,
                    finalSystemPrompt = s.FinalSystemPrompt,
                    systemPromptHash = s.SystemPromptHash,
                    toolSchemaHash = s.ToolSchemaHash,
                    promptDriftCount = s.PromptDriftCount,
                    toolNames = s.ToolNames,
                    lastPromptCacheChangeAt = s.LastPromptCacheChangeAt?.ToString("o"),
                    lastPromptCacheChangeKind = s.LastPromptCacheChangeKind,
                    lastPromptCacheChangedFields = s.LastPromptCacheChangedFields,
                    lastFinishReason = s.LastFinishReason,
                    rootThreadId = descriptor?.RootThreadId,
                    parentSessionKey = relationship?.ParentSessionKey,
                    parentPrefix = BuildParentPrefixSummary(prefixDiagnostic),
                    bindingKind = relationship?.BindingKind ?? descriptor?.BindingKind ?? "unbound",
                    deletionScope = descriptor?.DeletionScope ?? SessionPersistenceDeletionScopes.TraceOnly,
                    rollbackCount = threadOperationStore.CountThreadRollbacks(rootThreadId)
                };
            });
            return Results.Json(result, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/operations", (string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            var threadId = sessionKey;
            if (persistence != null)
            {
                try
                {
                    var descriptor = persistence.DescribeSessionDeletion(sessionKey);
                    if (!string.IsNullOrWhiteSpace(descriptor.RootThreadId))
                        threadId = descriptor.RootThreadId;
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Dashboard could not resolve root thread for session {SessionKey}", sessionKey);
                }
            }

            return Results.Json(threadOperationStore.GetThreadOperations(threadId), JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/events", (string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            var events = traceStore.GetEvents(sessionKey);
            return Results.Json(events, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/events/page", (HttpContext http) =>
        {
            RefreshTraceFromDiskIfEnabled();
            var page = traceStore.GetEventPage(
                sessionKey: null,
                limit: ParseIntQuery(http, "limit", 1000),
                beforeCursor: ReadQuery(http, "beforeCursor") ?? ReadQuery(http, "before"),
                filter: ReadQuery(http, "filter"));
            return Results.Json(page, JsonOptions);
        });

        endpoints.MapGet("/dashboard/api/sessions/{sessionKey}/events/page", (HttpContext http, string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            var page = traceStore.GetEventPage(
                sessionKey,
                ParseIntQuery(http, "limit", 1000),
                ReadQuery(http, "beforeCursor") ?? ReadQuery(http, "before"),
                ReadQuery(http, "filter"));
            return Results.Json(page, JsonOptions);
        });

        var capturedHandler = sessionHandler;
        if (runtime.Capabilities.SessionDeletion)
        {
            endpoints.MapDelete("/dashboard/api/sessions/{sessionKey}", async (HttpContext http, string sessionKey) =>
        {
            RefreshTraceFromDiskIfEnabled();
            if (persistence != null)
            {
                var cascadeDeleted = await persistence.DeleteTraceSessionAsync(sessionKey, deleteThreadAsync, http.RequestAborted);
                return cascadeDeleted
                    ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                    : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
            }

            var deleted = traceStore.ClearSession(sessionKey);

            if (capturedHandler != null)
            {
                try
                {
                    await capturedHandler.DeleteThreadAsync(sessionKey);
                }
                catch (KeyNotFoundException)
                {
                    // Thread may not exist (e.g. tracing-only session without a persisted thread) — ignore.
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "DeleteThreadAsync failed for session {SessionKey}", sessionKey);
                }
            }

            return deleted
                ? Results.Json(new { deleted = true, sessionKey }, JsonOptions)
                : Results.Json(new { deleted = false, sessionKey }, JsonOptions, statusCode: 404);
        });

            endpoints.MapDelete("/dashboard/api/sessions", async (HttpContext http) =>
        {
            // Align with on-disk state when the dashboard process is not the trace producer (e.g. CLI + AppServer subprocess).
            RefreshTraceFromDiskIfEnabled();
            // Capture keys before clearing so we can delete the underlying threads.
            var sessionKeys = traceStore.GetSessions().Select(s => s.SessionKey).ToList();
            if (persistence != null)
            {
                await persistence.DeleteTraceSessionsAsync(sessionKeys, deleteThreadAsync, http.RequestAborted);
                persistence.CompactStateIfWorthwhile();
                return Results.Json(new { cleared = true }, JsonOptions);
            }

            traceStore.ClearAll();

            if (capturedHandler != null)
            {
                try
                {
                    await capturedHandler.DeleteAllThreadsAsync(sessionKeys);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "DeleteAllThreadsAsync failed after clearing traces");
                }
            }

            return Results.Json(new { cleared = true }, JsonOptions);
        });
        }

        if (settingsConfiguration != null)
            DashBoardSettingsEndpoints.Map(endpoints, settingsConfiguration, configSchema ?? [], paths, logger);


        if (tokenUsageStore != null)
        {
            endpoints.MapGet("/dashboard/api/usage/sources", () =>
            {
                var summaries = tokenUsageStore.GetSourceSummaries().Select(summary => new
                {
                    sourceId = summary.SourceId,
                    sourceMode = summary.SourceMode,
                    subjectKind = summary.SubjectKind,
                    contextKind = summary.ContextKind,
                    subjectCount = summary.SubjectCount,
                    contextCount = summary.ContextCount,
                    requestCount = summary.RequestCount,
                    totalInputTokens = summary.TotalInputTokens,
                    totalOutputTokens = summary.TotalOutputTokens,
                    totalCachedInputTokens = summary.TotalCachedInputTokens,
                    totalCacheWriteInputTokens = summary.TotalCacheWriteInputTokens,
                    totalFreshInputTokens = summary.TotalFreshInputTokens,
                    totalNonCachedInputTokens = summary.TotalNonCachedInputTokens,
                    totalReasoningOutputTokens = summary.TotalReasoningOutputTokens,
                    cacheHitRate = summary.CacheHitRate,
                    totalTokens = summary.TotalTokens,
                    llmCallCount = summary.LlmCallCount,
                    lastActiveAt = summary.LastActiveAt.ToString("o")
                });
                return Results.Json(summaries, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/usage/sources/{sourceId}/subjects", (string sourceId) =>
            {
                var entries = tokenUsageStore.GetSubjectBreakdown(sourceId).Select(entry => new
                {
                    kind = entry.Kind,
                    id = entry.Id,
                    label = entry.Label,
                    requestCount = entry.RequestCount,
                    relatedSubjectCount = entry.RelatedSubjectCount,
                    totalInputTokens = entry.TotalInputTokens,
                    totalOutputTokens = entry.TotalOutputTokens,
                    totalCachedInputTokens = entry.TotalCachedInputTokens,
                    totalCacheWriteInputTokens = entry.TotalCacheWriteInputTokens,
                    totalFreshInputTokens = entry.TotalFreshInputTokens,
                    totalNonCachedInputTokens = entry.TotalNonCachedInputTokens,
                    totalReasoningOutputTokens = entry.TotalReasoningOutputTokens,
                    cacheHitRate = entry.CacheHitRate,
                    totalTokens = entry.TotalTokens,
                    llmCallCount = entry.LlmCallCount,
                    lastActiveAt = entry.LastActiveAt.ToString("o")
                });
                return Results.Json(entries, JsonOptions);
            });

            endpoints.MapGet("/dashboard/api/usage/sources/{sourceId}/contexts", (string sourceId) =>
            {
                var entries = tokenUsageStore.GetContextBreakdown(sourceId).Select(entry => new
                {
                    kind = entry.Kind,
                    id = entry.Id,
                    label = entry.Label,
                    requestCount = entry.RequestCount,
                    relatedSubjectCount = entry.RelatedSubjectCount,
                    totalInputTokens = entry.TotalInputTokens,
                    totalOutputTokens = entry.TotalOutputTokens,
                    totalCachedInputTokens = entry.TotalCachedInputTokens,
                    totalCacheWriteInputTokens = entry.TotalCacheWriteInputTokens,
                    totalFreshInputTokens = entry.TotalFreshInputTokens,
                    totalNonCachedInputTokens = entry.TotalNonCachedInputTokens,
                    totalReasoningOutputTokens = entry.TotalReasoningOutputTokens,
                    cacheHitRate = entry.CacheHitRate,
                    totalTokens = entry.TotalTokens,
                    llmCallCount = entry.LlmCallCount,
                    lastActiveAt = entry.LastActiveAt.ToString("o")
                });
                return Results.Json(entries, JsonOptions);
            });
        }

        if (dreamsAvailable)
            DashBoardDreamsEndpoints.Map(
                endpoints,
                paths,
                dreamsService!,
                traceStore,
                persistence,
                deleteThreadAsync,
                sessionHandler,
                logger);

        endpoints.MapGet("/dashboard/api/events/stream", async ctx =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var lifetime = ctx.RequestServices.GetRequiredService<IHostApplicationLifetime>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                ctx.RequestAborted, lifetime.ApplicationStopping);
            var cancellationToken = cts.Token;
            var reader = traceStore.SseReader;

            logger?.LogDebug("Dashboard SSE client connected");
            try
            {
                await foreach (var evt in reader.ReadAllAsync(cancellationToken))
                {
                    var json = JsonSerializer.Serialize(evt, JsonOptions);
                    await ctx.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    await ctx.Response.Body.FlushAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or server shutting down
            }
            finally
            {
                logger?.LogDebug("Dashboard SSE client disconnected");
            }
        });
    }

    private static string? ReadQuery(HttpContext http, string name)
    {
        var value = http.Request.Query[name].ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int ParseIntQuery(HttpContext http, string name, int defaultValue)
    {
        var value = http.Request.Query[name].ToString();
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static object? BuildParentPrefixSummary(TraceEvent? diagnostic)
    {
        if (diagnostic?.MetadataJson is not { Length: > 0 } metadataJson)
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            return new
            {
                status = ReadString(root, "status"),
                matchedInputItemCount = ReadInt32(root, "matchedInputItemCount"),
                parentInputItemCount = ReadInt32(root, "parentInputItemCount"),
                childInputItemCount = ReadInt32(root, "childInputItemCount"),
                divergenceIndex = ReadInt32(root, "divergenceIndex"),
                exactParentInputPrefix = ReadBoolean(root, "exactParentInputPrefix"),
                expectedSharedPrefix = ReadBoolean(root, "expectedSharedPrefix"),
                cacheIdentityShared = ReadBoolean(root, "cacheIdentityShared"),
                staticPrefixCompatible = ReadBoolean(root, "staticPrefixCompatible"),
                changedFields = root.TryGetProperty("changedFields", out var fields)
                    && fields.ValueKind == JsonValueKind.Array
                    ? fields.EnumerateArray()
                        .Where(static field => field.ValueKind == JsonValueKind.String)
                        .Select(static field => field.GetString()!)
                        .ToArray()
                    : []
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private static void MapOrchestratorEndpoints(
        IEndpointRouteBuilder endpoints,
        IEnumerable<IOrchestratorSnapshotProvider>? providers)
    {
        if (providers == null) return;

        foreach (var provider in providers)
        {
            var captured = provider;

            endpoints.MapGet($"/dashboard/api/orchestrators/{captured.Name}/state", () =>
            {
                var snapshot = captured.GetSnapshot();
                return Results.Json(snapshot, JsonOptions);
            });

            endpoints.MapPost($"/dashboard/api/orchestrators/{captured.Name}/refresh", () =>
            {
                captured.TriggerRefresh();
                return Results.Json(new { triggered = true, name = captured.Name }, JsonOptions);
            });
        }
    }
}
