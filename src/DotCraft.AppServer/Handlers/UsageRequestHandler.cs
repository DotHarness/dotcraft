using DotCraft.Skills;
using DotCraft.Tracing;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>Handles the <c>usage/*</c> and <c>profile/insights</c> wire methods (spec Section 27A).</summary>
internal sealed class UsageRequestHandler(
    UsageAnalyticsService? usageAnalytics,
    TraceStore? traceStore,
    SkillsLoader? skillsLoader,
    ISessionService sessionService,
    string? hostWorkspacePath) : IAppServerDomainHandler
{
    private const int DefaultThreadLimit = 5;
    private const int MaxThreadLimit = 50;

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.UsageSummary, HandleUsageSummaryAsync);
        table.Map(Protocol.AppServer.AppServerRpc.UsageHistory, HandleUsageHistoryAsync);
        table.Map(Protocol.AppServer.AppServerRpc.UsageThreads, HandleUsageThreadsAsync);
        table.Map(Protocol.AppServer.AppServerRpc.UsageThread, HandleUsageThreadAsync);
        table.Map(Protocol.AppServer.AppServerRpc.ProfileInsights, HandleProfileInsightsAsync);
    }

    private Task<AppServerTypedResult<Contract.UsageSummaryResult>> HandleUsageSummaryAsync(
        AppServerTypedRequest<Contract.UsageSummaryParams> request,
        CancellationToken ct)
    {
        var analytics = RequireAnalytics(Protocol.AppServer.AppServerMethodNames.UsageSummary);
        _ = ct;
        var p = request.Params;
        var range = ParseRange(p.From, p.To, p.TzOffsetMinutes);
        var s = analytics.GetSummary(range);

        return Task.FromResult(AppServerTypedResult<Contract.UsageSummaryResult>.FromResult(new Contract.UsageSummaryResult
        {
            SessionCount = s.ThreadCount,
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

    private Task<AppServerTypedResult<Contract.UsageHistoryResult>> HandleUsageHistoryAsync(
        AppServerTypedRequest<Contract.UsageHistoryParams> request,
        CancellationToken ct)
    {
        var analytics = RequireAnalytics(Protocol.AppServer.AppServerMethodNames.UsageHistory);
        _ = ct;
        var p = request.Params;

        var metricName = p.Metric.IsSet ? p.Metric.Value : null;
        if (!UsageAnalyticsNames.TryParseMetric(metricName, out var metric))
            throw AppServerErrors.InvalidParams("'metric' must be one of tokens, turns, requests, toolCalls, skillUses, errors.");

        var groupByName = p.GroupBy.IsSet ? p.GroupBy.Value : null;
        if (!UsageAnalyticsNames.TryParseDimension(groupByName, out var groupBy))
            throw AppServerErrors.InvalidParams("'groupBy' must be one of none, tokenType, surface, model, toolSource, tool, skill.");
        if (!UsageAnalyticsNames.IsSupported(metric, groupBy))
            throw AppServerErrors.InvalidParams($"'{metricName}' cannot be grouped by '{groupByName}'.");

        var topLimit = Math.Clamp(
            p.TopLimit.IsSet ? p.TopLimit.Value ?? UsageHistoryQuery.DefaultTopLimit : UsageHistoryQuery.DefaultTopLimit,
            1,
            UsageHistoryQuery.MaxTopLimit);
        var range = ParseRange(p.From, p.To, p.TzOffsetMinutes);
        var history = analytics.GetHistory(new UsageHistoryQuery(metric, groupBy, range, topLimit));

        return Task.FromResult(AppServerTypedResult<Contract.UsageHistoryResult>.FromResult(new Contract.UsageHistoryResult
        {
            Unit = history.Unit,
            GroupBy = UsageAnalyticsNames.ToWire(history.GroupBy),
            Days = history.Days
                .Select(day => new Contract.UsageHistoryDay
                {
                    Date = day.Date.ToString("yyyy-MM-dd"),
                    Total = day.Total,
                    Values = day.Values
                        .Select(value => new Contract.UsageHistoryValue { Key = value.Key, Value = value.Value })
                        .ToList()
                })
                .ToList(),
            Series = history.Series
                .Select(series => new Contract.UsageHistorySeries { Key = series.Key, Total = series.Total })
                .ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.UsageThreadsResult>> HandleUsageThreadsAsync(
        AppServerTypedRequest<Contract.UsageThreadsParams> request,
        CancellationToken ct)
    {
        var analytics = RequireAnalytics(Protocol.AppServer.AppServerMethodNames.UsageThreads);
        _ = ct;
        var p = request.Params;
        var range = ParseRange(p.From, p.To, p.TzOffsetMinutes);
        var limit = Math.Clamp(p.Limit.IsSet ? p.Limit.Value ?? DefaultThreadLimit : DefaultThreadLimit, 1, MaxThreadLimit);

        var threads = analytics.GetTopThreads(range, limit);
        return Task.FromResult(AppServerTypedResult<Contract.UsageThreadsResult>.FromResult(new Contract.UsageThreadsResult
        {
            Threads = threads
                .Select(thread => new Contract.UsageThreadRow
                {
                    ThreadId = thread.ThreadId,
                    Title = thread.Title == null ? default : Protocol.Optional<string?>.FromValue(thread.Title),
                    OriginChannel = thread.OriginChannel,
                    LastActiveAt = UsageDateRange.FormatTimestamp(thread.LastActiveAt),
                    Turns = thread.Turns,
                    TotalTokens = thread.TotalTokens,
                    InputTokens = thread.InputTokens,
                    OutputTokens = thread.OutputTokens,
                    CachedInputTokens = thread.CachedInputTokens,
                    CacheHitRate = thread.CacheHitRate,
                    Archived = thread.Archived
                })
                .ToList()
        }));
    }

    private Task<AppServerTypedResult<Contract.UsageThreadResult>> HandleUsageThreadAsync(
        AppServerTypedRequest<Contract.UsageThreadParams> request,
        CancellationToken ct)
    {
        var analytics = RequireAnalytics(Protocol.AppServer.AppServerMethodNames.UsageThread);
        _ = ct;
        var threadId = request.Params.ThreadId.IsSet ? request.Params.ThreadId.Value?.Trim() : null;
        if (string.IsNullOrEmpty(threadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");

        var usage = analytics.GetThreadUsage(threadId);
        return Task.FromResult(AppServerTypedResult<Contract.UsageThreadResult>.FromResult(new Contract.UsageThreadResult
        {
            ThreadId = usage.ThreadId,
            Turns = usage.Turns,
            TotalTokens = usage.TotalTokens,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CachedInputTokens = usage.CachedInputTokens,
            CacheHitRate = usage.CacheHitRate,
            Groups = usage.Groups
                .Select(group => new Contract.UsageThreadGroup
                {
                    Model = OptionalText(group.Model),
                    ReasoningEffort = OptionalText(group.ReasoningEffort),
                    Speed = OptionalText(group.Speed),
                    Turns = group.Turns,
                    TotalTokens = group.TotalTokens,
                    InputTokens = group.InputTokens,
                    OutputTokens = group.OutputTokens,
                    CachedInputTokens = group.CachedInputTokens
                })
                .ToList()
        }));
    }

    private static Protocol.Optional<string?> OptionalText(string? value) =>
        string.IsNullOrEmpty(value) ? default : Protocol.Optional<string?>.FromValue(value);

    private async Task<AppServerTypedResult<Contract.ProfileInsightsResult>> HandleProfileInsightsAsync(
        AppServerTypedRequest<Contract.ProfileInsightsParams> request,
        CancellationToken ct)
    {
        if (traceStore == null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.ProfileInsights);
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
            LongestTaskMs = traceStore.GetLongestTurnDurationMs(),
            Skills = insights.TopSkills.Select(MapSkillUsage).ToList()
        });
    }

    private UsageAnalyticsService RequireAnalytics(string method) =>
        usageAnalytics ?? throw AppServerErrors.MethodNotFound(method);

    /// <summary>Maps an aggregated skill bucket to wire form using the plugin id recorded at reference time.</summary>
    private Contract.SkillUsage MapSkillUsage(SkillUsageBucket bucket)
    {
        var pluginId = ToolUsageSource.PluginIdOf(bucket.ToolSource);
        if (pluginId == null)
            return new Contract.SkillUsage { Name = bucket.Name, Count = bucket.Count };

        var info = skillsLoader?.ResolveSkillInfo(bucket.Name);
        var displayName = info != null && string.Equals(info.PluginId, pluginId, StringComparison.Ordinal)
            ? info.PluginDisplayName
            : null;
        return new Contract.SkillUsage
        {
            Name = bucket.Name,
            Count = bucket.Count,
            PluginId = Protocol.Optional<string?>.FromValue(pluginId),
            PluginDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? default
                : Protocol.Optional<string?>.FromValue(displayName)
        };
    }

    private static Protocol.Optional<Contract.RankedMetric?> ToRankedMetric(RankedUsage? usage) =>
        usage == null
            ? default
            : Protocol.Optional<Contract.RankedMetric?>.FromValue(
                new Contract.RankedMetric { Key = usage.Key, Count = usage.Count, Total = usage.Total });

    private static UsageDateRange ParseRange(
        Protocol.Optional<string?> from,
        Protocol.Optional<string?> to,
        Protocol.Optional<int?> tzOffsetMinutes) =>
        UsageDateRange.Create(
            ParseUsageDate(from.IsSet ? from.Value : null, "from"),
            ParseUsageDate(to.IsSet ? to.Value : null, "to"),
            tzOffsetMinutes.IsSet ? tzOffsetMinutes.Value : null);

    private static DateOnly? ParseUsageDate(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", out var date))
            throw AppServerErrors.InvalidParams($"'{field}' must be a 'YYYY-MM-DD' date.");
        return date;
    }
}
