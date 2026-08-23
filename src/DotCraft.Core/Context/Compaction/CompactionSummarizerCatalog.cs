using DotCraft.Contributions;

namespace DotCraft.Context.Compaction;

/// <summary>Registers the kernel's own summarizer as the named default of the compaction-summary contribution point, and reads the point per compaction.</summary>
public static class CompactionSummarizerCatalog
{
    /// <summary>The Tier-B target name of the built-in summarizer, named for what it does: summarize locally, unlike the host-owned provider-native backend.</summary>
    public const string BuiltInTargetName = "local-summary";

    /// <summary>The order the built-in summarizer is registered at.</summary>
    public const int BuiltInOrder = 100;

    /// <summary>Gets the built-in summarizer, so a compaction can run without a registry.</summary>
    internal static ICompactionSummarizer BuiltIn { get; } = new LocalCompactionSummarizer();

    /// <summary>Registers the built-in summarizer into a registry.</summary>
    /// <param name="registrar">Optional origin-scoped owner for the handle; when omitted the summarizer is attributed to <see cref="ContributionOrigin.Builtin"/> and lives for the registry's lifetime.</param>
    /// <returns>The registration handle.</returns>
    internal static IReadOnlyList<IContributionHandle> RegisterBuiltIns(
        IContributionRegistry registry,
        IContributionRegistrar? registrar = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var options = new ContributionOptions(Order: BuiltInOrder)
        {
            TargetName = BuiltInTargetName,
            OwnsContribution = false
        };
        return [registrar is null
            ? registry.Add(BuiltIn, options)
            : registrar.Add(BuiltIn, options)];
    }

    /// <summary>Returns the effective summarizer: the contribution point's authority, falling back to the built-in when it is empty.</summary>
    /// <remarks>Read per compaction, so a replacement registered mid-session governs the next one without rebuilding any agent.</remarks>
    public static ICompactionSummarizer Resolve(IContributionView? contributions, string? threadId = null) =>
        ContributionRead.Authority(contributions?.Resolve<ICompactionSummarizer>(threadId), BuiltIn);

    /// <summary>Stateless: every per-pipeline input travels on the request, so one instance serves every thread in the process.</summary>
    private sealed class LocalCompactionSummarizer : ICompactionSummarizer
    {
        public async Task<CompactionSummaryAttempt> SummarizeAsync(
            CompactionSummaryRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            var host = request.RequireHost();

            if (request.Scope == CompactionSummaryScope.Full)
            {
                var full = await host.Full.CompactAsync(
                    request.History,
                    request.Snapshot,
                    request.ThreadId,
                    request.FallbackTools,
                    cancellationToken);
                return new CompactionSummaryAttempt(
                    full.Result is { } fullResult
                        ? new CompactionSummary(fullResult.FormattedSummary, fullResult.PreservedTail)
                        : null,
                    full.Reason);
            }

            var partial = await host.Partial.CompactAsync(
                request.History,
                request.Snapshot,
                request.ThreadId,
                request.FallbackTools,
                cancellationToken);
            return new CompactionSummaryAttempt(
                partial.Result is { } partialResult
                    ? new CompactionSummary(partialResult.FormattedSummary, partialResult.PreservedTail)
                    : null,
                partial.Reason);
        }
    }
}
