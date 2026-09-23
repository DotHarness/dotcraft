using DotCraft.Protocol;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.SessionImport;

public sealed record SessionImportServiceOptions(string WorkspacePath, string CraftDataPath, SessionImportConfig Config);

public sealed partial class SessionImportService : ISessionServiceConsumer
{
    private const string ManualTrigger = "manual";
    private const string SyncTrigger = "sync";

    private readonly SessionImportServiceOptions _options;
    private readonly SessionImportSettingsStore _settingsStore;
    private readonly IReadOnlyList<ISessionImportSource> _sources;
    private readonly SessionImportLedger _ledger;
    private readonly SessionIdentity _identity;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _ledgerGate = new(1, 1);
    private readonly Lock _passLock = new();
    private readonly Lock _settingsLock = new();
    private ISessionService? _sessions;
    private Task? _activePass;
    private bool _syncQueued;
    private CancellationTokenSource _passLifetime = new();
    private SessionImportUserSettings _userSettings;

    public SessionImportService(
        SessionImportServiceOptions options,
        SessionImportSettingsStore settingsStore,
        IEnumerable<ISessionImportSource> sources,
        ILogger<SessionImportService>? logger = null)
    {
        _options = options;
        _settingsStore = settingsStore;
        _sources = sources.ToArray();
        _ledger = new SessionImportLedger(options.CraftDataPath);
        _identity = SessionImportIdentity.Create(options.WorkspacePath);
        _logger = logger ?? NullLogger<SessionImportService>.Instance;
        _userSettings = settingsStore.ReadUserSettings();
    }

    public event Action<Contract.ImportSessionsProgressNotification>? Progress;

    public event Action<Contract.ImportSessionsCompletedNotification>? Completed;

    public event Action? SyncActivated;

    public IReadOnlyList<string> SupportedSources => _sources.Select(static source => source.SourceId).ToArray();

    public TimeSpan SyncInterval => _options.Config.SyncInterval;

    public bool IsSyncActive => _userSettings.SyncEnabled && !_settingsStore.ReadWorkspaceOptOut();

    public void SetSessionService(ISessionService service) => _sessions = service;

    public async Task<IReadOnlyList<Contract.ImportSourceDetection>> DetectAsync(
        IReadOnlyList<string>? sources,
        CancellationToken ct = default)
    {
        var selected = ResolveSources(sources ?? SupportedSources, requireAvailable: false);
        var context = await CreateContextAsync(ct).ConfigureAwait(false);
        var detections = new List<Contract.ImportSourceDetection>();
        foreach (var source in selected)
        {
            var available = source.IsAvailable;
            var detected = available
                ? await DetectSourceAsync(source, context, retainSessions: false, ct).ConfigureAwait(false)
                : [];
            detections.Add(new Contract.ImportSourceDetection
            {
                Source = source.SourceId,
                Available = available,
                Sessions = detected.Select(static entry => entry.Candidate).ToArray(),
                ImportableCount = detected.Count(static entry => IsImportable(entry.Candidate.State))
            });
        }

        await SaveDetectionUpdatesAsync(context).ConfigureAwait(false);
        return detections;
    }

    public string Run(IReadOnlyList<string> sources, IReadOnlyList<string>? sessionIds)
    {
        if (sources.Count == 0)
            throw new ArgumentException("At least one source is required.", nameof(sources));
        var request = new PassRequest(
            SessionImportIdentity.NewImportId(),
            ManualTrigger,
            ResolveSources(sources, requireAvailable: true),
            sessionIds?.ToHashSet(StringComparer.OrdinalIgnoreCase));
        lock (_passLock)
        {
            if (_activePass is not null)
                throw new SessionImportException(SessionImportErrorCodes.Busy, "An import is already running in this workspace.");
            _activePass = StartPassLoop(request);
        }

        return request.ImportId;
    }

    public void RequestSyncPass()
    {
        lock (_passLock)
        {
            if (_activePass is not null)
            {
                _syncQueued = true;
                return;
            }

            _activePass = StartPassLoop(CreateSyncRequest());
        }
    }

    public async Task StopPassesAsync()
    {
        Task? active;
        CancellationTokenSource lifetime;
        lock (_passLock)
        {
            active = _activePass;
            _syncQueued = false;
            lifetime = _passLifetime;
            _passLifetime = new CancellationTokenSource();
        }

        await lifetime.CancelAsync().ConfigureAwait(false);
        if (active is not null)
            await active.ConfigureAwait(false);
        lifetime.Dispose();
    }

    public Contract.ImportSettings GetSettings()
    {
        var settings = _userSettings;
        return new Contract.ImportSettings
        {
            SyncEnabled = settings.SyncEnabled,
            Sources = settings.Sources,
            SyncIntervalMinutes = (int)Math.Clamp(SyncInterval.TotalMinutes, 0, int.MaxValue),
            LastSyncAt = _ledger.TryRead()?.LastSyncAt is { } lastSyncAt ? Optional<DateTimeOffset>.FromValue(lastSyncAt) : default,
            WorkspaceOptOut = _settingsStore.ReadWorkspaceOptOut()
        };
    }

    public Contract.ImportSettings UpdateSettings(bool? syncEnabled, IReadOnlyList<string>? sources)
    {
        var knownSources = sources is null ? null : ValidateSources(sources);
        bool activated;
        lock (_settingsLock)
        {
            _settingsStore.WriteUserSettings(syncEnabled, knownSources);
            var previous = _userSettings;
            _userSettings = new SessionImportUserSettings(syncEnabled ?? previous.SyncEnabled, knownSources ?? previous.Sources);
            activated = !previous.SyncEnabled && _userSettings.SyncEnabled;
        }

        if (activated)
            SyncActivated?.Invoke();
        return GetSettings();
    }

    private ISessionService Sessions =>
        _sessions ?? throw new InvalidOperationException("The session service is not available yet.");

    private IReadOnlyList<ISessionImportSource> ResolveSources(IReadOnlyList<string> sourceIds, bool requireAvailable)
    {
        var resolved = new List<ISessionImportSource>();
        foreach (var sourceId in ValidateSources(sourceIds))
        {
            var source = _sources.FirstOrDefault(candidate => candidate.SourceId == sourceId)
                ?? throw new ArgumentException($"Unsupported import source: {sourceId}");
            if (requireAvailable && !source.IsAvailable)
            {
                throw new SessionImportException(
                    SessionImportErrorCodes.SourceUnavailable,
                    $"No {sourceId} session store was found on the server machine.");
            }

            resolved.Add(source);
        }

        return resolved;
    }

    private static IReadOnlyList<string> ValidateSources(IReadOnlyList<string> sources)
    {
        foreach (var source in sources)
        {
            if (!SessionImportSources.IsKnown(source))
                throw new ArgumentException($"Unknown import source: {source}");
        }

        return sources.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsImportable(string state) => state is CandidateStates.New or CandidateStates.Changed;

    private static class CandidateStates
    {
        public const string New = "new";
        public const string Changed = "changed";
        public const string Deferred = "deferred";
        public const string Current = "current";
    }
}
