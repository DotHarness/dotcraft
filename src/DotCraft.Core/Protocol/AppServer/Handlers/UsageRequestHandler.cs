using DotCraft.Skills;
using DotCraft.Tracing;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Handles the <c>usage/*</c> and <c>profile/insights</c> wire methods (spec Section 27A): usage
/// summary, daily timeseries, and aggregated profile insights.
/// </summary>
internal sealed class UsageRequestHandler(
    TraceStore? traceStore,
    SkillsLoader? skillsLoader,
    ISessionService sessionService,
    string? hostWorkspacePath) : IAppServerDomainHandler
{
    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.UsageSummary, HandleUsageSummaryAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.UsageTimeseries, HandleUsageTimeseriesAsync);
        table.Map(global::DotCraft.Protocol.Contracts.AppServer.AppServerRpc.ProfileInsights, HandleProfileInsightsAsync);
    }

    private Task<object?> HandleUsageSummaryAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.UsageSummary);
        var s = traceStore.GetSummary();
        return Task.FromResult<object?>(new UsageSummaryResult
        {
            SessionCount = s.SessionCount,
            TotalRequests = s.TotalRequests,
            TotalResponses = s.TotalResponses,
            TotalToolCalls = s.TotalToolCalls,
            TotalErrors = s.TotalErrors,
            TotalContextCompactions = s.TotalContextCompactions,
            TotalInputTokens = s.TotalInputTokens,
            TotalOutputTokens = s.TotalOutputTokens,
            TotalCachedInputTokens = s.TotalCachedInputTokens,
            TotalCacheWriteInputTokens = s.TotalCacheWriteInputTokens,
            TotalFreshInputTokens = s.TotalFreshInputTokens,
            TotalNonCachedInputTokens = s.TotalNonCachedInputTokens,
            TotalReasoningOutputTokens = s.TotalReasoningOutputTokens,
            TotalToolDurationMs = s.TotalToolDurationMs,
            AvgToolDurationMs = s.AvgToolDurationMs,
            MaxToolDurationMs = s.MaxToolDurationMs,
            CacheHitRate = s.CacheHitRate,
            TotalTokens = s.TotalTokens
        });
    }

    private Task<object?> HandleUsageTimeseriesAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.UsageTimeseries);
        var p = AppServerParams.Get<UsageTimeseriesParams>(msg);

        var from = ParseUsageDate(p.From, "from");
        var to = ParseUsageDate(p.To, "to");
        var tz = Math.Clamp(p.TzOffsetMinutes ?? 0, -840, 840);

        var buckets = traceStore.GetDailyUsage(from, to, tz);
        return Task.FromResult<object?>(new UsageTimeseriesResult
        {
            TzOffsetMinutes = tz,
            LongestTaskMs = traceStore.GetLongestTurnDurationMs(),
            Days = buckets
                .Select(b => new UsageTimeseriesDay
                {
                    Date = b.Date.ToString("yyyy-MM-dd"),
                    InputTokens = b.InputTokens,
                    OutputTokens = b.OutputTokens,
                    TotalTokens = b.TotalTokens,
                    SessionCount = b.SessionCount
                })
                .ToList()
        });
    }

    private async Task<object?> HandleProfileInsightsAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights);
        var p = AppServerParams.Get<ProfileInsightsParams>(msg);
        var topSkills = Math.Clamp(p.TopSkills ?? 5, 1, 20);

        var insights = traceStore.GetProfileInsights(topSkills);

        // Workspace-scoped count (not identity-scoped): the Profile page reflects all threads in
        // this workspace, regardless of which channel/user (e.g. channelContext) created them.
        var identity = new SessionIdentity();
        var workspacePath = string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath)
            ? hostWorkspacePath
            : identity.WorkspacePath;
        var totalThreads = await sessionService.CountWorkspaceThreadsAsync(workspacePath, ct);

        return new ProfileInsightsResult
        {
            TopModel = ToRankedMetric(insights.TopModel),
            TopReasoning = ToRankedMetric(insights.TopReasoning),
            SkillsExplored = insights.DistinctSkillCount,
            TotalSkillsUsed = insights.TotalSkillCount,
            TotalThreads = totalThreads,
            Skills = insights.TopSkills.Select(MapSkillUsage).ToList()
        };
    }

    /// <summary>Maps an aggregated skill bucket to wire form, attaching live plugin attribution.</summary>
    private SkillUsageWire MapSkillUsage(SkillUsageBucket bucket)
    {
        var wire = new SkillUsageWire { Name = bucket.Name, Count = bucket.Count };
        var info = skillsLoader?.ResolveSkillInfo(bucket.Name);
        if (info != null && !string.IsNullOrWhiteSpace(info.PluginId))
        {
            wire.PluginId = info.PluginId;
            wire.PluginDisplayName = info.PluginDisplayName;
        }
        return wire;
    }

    private static RankedMetric? ToRankedMetric(RankedUsage? usage) =>
        usage == null
            ? null
            : new RankedMetric { Key = usage.Key, Count = usage.Count, Total = usage.Total };

    private static DateOnly? ParseUsageDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var date))
            throw AppServerErrors.InvalidParams($"'{field}' must be a 'YYYY-MM-DD' date.");
        return date;
    }
}
