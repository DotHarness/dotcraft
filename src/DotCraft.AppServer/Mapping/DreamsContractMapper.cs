using DotCraft.Dreams;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Projects Dreams state onto the AppServer wire contract, shared with Dashboard.</summary>
public static class DreamsContractMapper
{
    public static Contract.DreamsStatusResult ToStatus(DreamsService service, DreamsConfig config)
    {
        var state = service.LoadLatestState();
        return new Contract.DreamsStatusResult
        {
            Enabled = config.Enabled,
            Interval = FormatInterval(config.Interval),
            AutoApply = config.AutoApply,
            MinSessionsSinceLastRun = config.MinSessionsSinceLastRun,
            NextRunAt = service.NextRunAt,
            Running = service.IsRunning,
            LastRun = state == null ? null : ToContract(state)
        };
    }

    public static Contract.DreamsRunState ToContract(DreamsRunState state) => new()
    {
        Id = state.Id,
        Scope = state.Scope == null ? default : new Protocol.Optional<string?>(state.Scope),
        Status = state.Status,
        Trigger = state.Trigger,
        StartedAt = state.StartedAt,
        EndedAt = state.EndedAt,
        CandidateThreadCount = state.CandidateThreadCount,
        EvidenceThreadIds = new Protocol.Optional<IReadOnlyList<string>>(state.EvidenceThreadIds),
        EvidenceSearchCount = state.EvidenceSearchCount,
        EvidenceReadCount = state.EvidenceReadCount,
        Changes = state.Changes is not { } changes
            ? null
            : new Contract.DreamsRunChanges
            {
                Added = new Protocol.Optional<IReadOnlyList<string>>(changes.Added),
                Updated = new Protocol.Optional<IReadOnlyList<string>>(changes.Updated),
                Deleted = new Protocol.Optional<IReadOnlyList<string>>(changes.Deleted),
                IndexChanged = changes.IndexChanged
            },
        ReviewStatus = state.ReviewStatus,
        AutoApplied = state.AutoApplied,
        Summary = state.Summary,
        ErrorType = state.ErrorType,
        Message = state.Message,
        ThreadId = state.ThreadId,
        TurnId = state.TurnId,
        Usage = state.Usage is null ? null : AppServerContractMapper.ToContract(state.Usage),
        InputManifestPath = state.InputManifestPath
    };

    public static Contract.DreamsGetResult ToContract(DreamsRunReview review) => new()
    {
        Run = ToContract(review.Run),
        Files = new Protocol.Optional<IReadOnlyList<Contract.DreamsFileChange>>(review.Files
            .Select(static file => new Contract.DreamsFileChange
            {
                Name = file.Name,
                Change = file.Change,
                Baseline = file.Baseline,
                Candidate = file.Candidate
            })
            .ToArray())
    };

    private static string FormatInterval(TimeSpan value)
    {
        var normalized = value <= TimeSpan.Zero ? TimeSpan.FromHours(24) : value;
        var totalSeconds = (long)Math.Round(normalized.TotalSeconds);
        return $"{totalSeconds / 3600:00}:{totalSeconds % 3600 / 60:00}:{totalSeconds % 60:00}";
    }
}
