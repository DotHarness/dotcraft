using DotCraft.Skills;
using DotCraft.Tracing;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

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
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.UsageSummary, HandleUsageSummaryAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.UsageTimeseries, HandleUsageTimeseriesAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.ProfileInsights, HandleProfileInsightsAsync);
    }

    private Task<AppServerTypedResult<Contract.UsageSummaryResult>> HandleUsageSummaryAsync(
        AppServerTypedRequest<global::DotCraft.Protocol.RpcEmpty> request,
        CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageSummary);
        _ = request;
        _ = ct;
        var s = traceStore.GetSummary();
        return Task.FromResult(AppServerTypedResult<Contract.UsageSummaryResult>.FromResult(new Contract.UsageSummaryResult
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
        }));
    }

    private Task<AppServerTypedResult<Contract.UsageTimeseriesResult>> HandleUsageTimeseriesAsync(
        AppServerTypedRequest<Contract.UsageTimeseriesParams> request,
        CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries);
        _ = ct;
        var p = request.Params;

        var from = ParseUsageDate(p.From.IsSet ? p.From.Value : null, "from");
        var to = ParseUsageDate(p.To.IsSet ? p.To.Value : null, "to");
        var tz = Math.Clamp(p.TzOffsetMinutes.IsSet ? p.TzOffsetMinutes.Value ?? 0 : 0, -840, 840);

        var buckets = traceStore.GetDailyUsage(from, to, tz);
        return Task.FromResult(AppServerTypedResult<Contract.UsageTimeseriesResult>.FromResult(new Contract.UsageTimeseriesResult
        {
            TzOffsetMinutes = tz,
            LongestTaskMs = traceStore.GetLongestTurnDurationMs(),
            Days = buckets
                .Select(b => new Contract.UsageTimeseriesDay
                {
                    Date = b.Date.ToString("yyyy-MM-dd"),
                    InputTokens = b.InputTokens,
                    OutputTokens = b.OutputTokens,
                    TotalTokens = b.TotalTokens,
                    SessionCount = b.SessionCount
                })
                .ToList()
        }));
    }

    private async Task<AppServerTypedResult<Contract.ProfileInsightsResult>> HandleProfileInsightsAsync(
        AppServerTypedRequest<Contract.ProfileInsightsParams> request,
        CancellationToken ct)
    {
        if (traceStore == null) throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.ProfileInsights);
        var p = request.Params;
        var topSkills = Math.Clamp(p.TopSkills.IsSet ? p.TopSkills.Value ?? 5 : 5, 1, 20);

        var insights = traceStore.GetProfileInsights(topSkills);

        // Workspace-scoped count (not identity-scoped): the Profile page reflects all threads in
        // this workspace, regardless of which channel/user (e.g. channelContext) created them.
        var identity = new SessionIdentity();
        var workspacePath = string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(hostWorkspacePath)
            ? hostWorkspacePath
            : identity.WorkspacePath;
        var totalThreads = await sessionService.CountWorkspaceThreadsAsync(workspacePath, ct);

        return AppServerTypedResult<Contract.ProfileInsightsResult>.FromResult(new Contract.ProfileInsightsResult
        {
            TopModel = ToRankedMetric(insights.TopModel),
            TopReasoning = ToRankedMetric(insights.TopReasoning),
            SkillsExplored = insights.DistinctSkillCount,
            TotalSkillsUsed = insights.TotalSkillCount,
            TotalThreads = totalThreads,
            Skills = insights.TopSkills.Select(MapSkillUsage).ToList()
        });
    }

    /// <summary>Maps an aggregated skill bucket to wire form, attaching live plugin attribution.</summary>
    private Contract.SkillUsage MapSkillUsage(SkillUsageBucket bucket)
    {
        var info = skillsLoader?.ResolveSkillInfo(bucket.Name);
        return new Contract.SkillUsage
        {
            Name = bucket.Name,
            Count = bucket.Count,
            PluginId = info != null && !string.IsNullOrWhiteSpace(info.PluginId)
                ? DotCraft.Protocol.Optional<string?>.FromValue(info.PluginId)
                : default,
            PluginDisplayName = info != null && !string.IsNullOrWhiteSpace(info.PluginId)
                ? DotCraft.Protocol.Optional<string?>.FromValue(info.PluginDisplayName)
                : default
        };
    }

    private static DotCraft.Protocol.Optional<Contract.RankedMetric?> ToRankedMetric(RankedUsage? usage) =>
        usage == null
            ? default
            : DotCraft.Protocol.Optional<Contract.RankedMetric?>.FromValue(
                new Contract.RankedMetric { Key = usage.Key, Count = usage.Count, Total = usage.Total });

    private static DateOnly? ParseUsageDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var date))
            throw AppServerErrors.InvalidParams($"'{field}' must be a 'YYYY-MM-DD' date.");
        return date;
    }
}
