using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.SessionImport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed partial class HubLocalImport
{
    private readonly HubPaths _paths;
    private readonly ProjectRegistry _projects;
    private readonly HubEventBus _events;
    private readonly ILogger _logger;
    private readonly CancellationToken _stopping;
    private readonly HubLocalImportHistory _history;
    private readonly SessionImportSettingsStore _settings;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<LocalImportSource>>> _detect;
    private readonly Func<string, ImportRunParams, bool, Action<int>, CancellationToken, Task<ImportCompletedNotification>> _runTarget;
    private readonly Func<IReadOnlySet<string>> _runningWorkspaces;
    private readonly Dictionary<string, DateTimeOffset> _lastSyncByDestination = new(ManagedAppServerRegistry.WorkspaceComparer);
    private readonly Lock _gate = new();
    private ImportProgressNotification? _running;
    private DateTimeOffset _runningSince;
    private Task? _job;

    public HubLocalImport(HubPaths paths, ProjectRegistry projects, HubEventBus events,
        Func<string, ImportRunParams, bool, Action<int>, CancellationToken, Task<ImportCompletedNotification>> runTarget,
        Func<IReadOnlySet<string>> runningWorkspaces, ILogger logger, CancellationToken stopping,
        Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<LocalImportSource>>>? detect = null)
    {
        _paths = paths;
        _projects = projects;
        _events = events;
        _runTarget = runTarget;
        _runningWorkspaces = runningWorkspaces;
        _logger = logger;
        _stopping = stopping;
        _history = new(paths, projects, logger);
        _settings = SettingsFor(paths.DefaultChatWorkspacePath);
        _detect = detect ?? new LocalImportDiscovery(paths.CraftHomePath, paths.DefaultChatWorkspacePath).DetectAsync;
    }

    public async Task<IReadOnlyList<LocalImportSource>> DetectAsync(CancellationToken ct) =>
        ReadConfig().Enabled ? await _detect(_projects.List().Select(project => project.Path).ToArray(), ct) : [];

    public HubLocalImportState GetState()
    {
        ImportProgressNotification? running;
        DateTimeOffset? incompleteSince;
        lock (_gate)
        {
            running = _running;
            incompleteSince = running is null ? null : _runningSince;
        }
        var (imports, attention, lastSyncAt) = _history.Read(incompleteSince);
        var config = ReadConfig();
        return new(config.Enabled, ReadSettings(config, imports.Count > 0, lastSyncAt), running, imports, attention);
    }

    public ImportSettings SetSettings(ImportSettingsSetParams request)
    {
        var config = ReadConfig();
        RequireEnabled(config);
        var sources = request.Sources.IsSet ? request.Sources.Value : null;
        var selection = request.Selection.IsSet ? request.Selection.Value : null;
        if (request.Sources.IsSet && (sources is null || sources.Any(source => !SessionImportSources.IsKnown(source))))
            throw Invalid("Choose a supported import source.");
        if (request.Selection.IsSet)
            ValidateSelection(selection);
        _settings.WriteUserSettings(request.SyncEnabled.IsSet ? request.SyncEnabled.Value : null,
            sources?.Distinct(StringComparer.Ordinal).ToArray(), selection);
        if (request.SyncEnabled.IsSet && request.SyncEnabled.Value)
            TryScheduleSync(force: true);
        var (imports, _, lastSyncAt) = _history.Read();
        return ReadSettings(config, imports.Count > 0, lastSyncAt);
    }

    public string Run(HubLocalImportRequest request)
    {
        RequireEnabled(ReadConfig());
        if (!SessionImportSources.IsKnown(request.Source))
            throw Invalid("Choose a supported import source.");
        if (request.Items is not { Count: > 0 and <= 10000 } || request.Items.Any(item =>
                item is null || string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Fingerprint))
            || request.Items.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != request.Items.Count)
            throw Invalid("Choose items from the current import preview.");
        ValidateSelection(request.Selection);
        ValidateSelection(request.Offered);
        lock (_gate)
        {
            if (_running is not null)
                throw new HubProtocolException("import_busy", "An import is already running.", StatusCodes.Status409Conflict);
            return Start([request.Source], request.Selection, request);
        }
    }

    public async Task ScheduleAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                TryScheduleSync(force: false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Unable to schedule local imports");
            }
        } while (await timer.WaitForNextTickAsync(_stopping));
    }

    public Task StopAsync()
    {
        lock (_gate)
            return _job ?? Task.CompletedTask;
    }

    private void TryScheduleSync(bool force)
    {
        lock (_gate)
        {
            if (_running is not null || _stopping.IsCancellationRequested)
                return;
            var config = ReadConfig();
            var settings = _settings.ReadUserSettings();
            if (!config.Enabled || !settings.SyncEnabled || settings.Sources.Count == 0)
                return;
            var interval = TimeSpan.FromMinutes(SyncIntervalMinutes(config));
            var now = DateTimeOffset.UtcNow;
            var ready = _runningWorkspaces();
            var targets = _projects.List().Select(project => project.Path).Append(_paths.DefaultChatWorkspacePath)
                .Where(path => ready.Contains(path) && (force || !_lastSyncByDestination.TryGetValue(path, out var last)
                    || now - last >= interval)).ToHashSet(ManagedAppServerRegistry.WorkspaceComparer);
            if (targets.Count == 0)
                return;
            foreach (var path in targets)
                _lastSyncByDestination[path] = now;
            Start(settings.Sources, settings.Selection, null, targets);
        }
    }

    private string Start(IReadOnlyList<string> sources, ImportSelection selection,
        HubLocalImportRequest? manual, IReadOnlySet<string>? syncTargets = null)
    {
        _stopping.ThrowIfCancellationRequested();
        var id = "desktop_import_" + Guid.NewGuid().ToString("N");
        var started = _runningSince = DateTimeOffset.UtcNow;
        _running = Progress(id, sources[0], 0, manual?.Items.Count ?? 0);
        _job = Task.Run(() => ExecuteAsync(id, started, sources, selection, manual, syncTargets));
        return id;
    }

    private SessionImportSettingsStore SettingsFor(string path) => new(_paths.GlobalConfigPath, ConfigPath(path));

    private static string ConfigPath(string root) => Path.Combine(root, ".craft", "config.json");

    private SessionImportConfig ReadConfig() => AppConfig.Load(_paths.GlobalConfigPath).GetSection<SessionImportConfig>("AgentImport");

    private ImportSettings ReadSettings(SessionImportConfig config, bool hasImported, DateTimeOffset? lastSyncAt)
    {
        var settings = _settings.ReadUserSettings();
        return new()
        {
            HasImported = hasImported, Selection = settings.Selection, Sources = settings.Sources,
            SyncEnabled = settings.SyncEnabled, SyncIntervalMinutes = SyncIntervalMinutes(config), WorkspaceOptOut = false,
            LastSyncAt = lastSyncAt is { } at ? Optional<DateTimeOffset>.FromValue(at) : default
        };
    }

    private static int SyncIntervalMinutes(SessionImportConfig config) =>
        (int)Math.Clamp(config.SyncInterval.TotalMinutes, 1, TimeSpan.FromDays(30).TotalMinutes);

    private void Report(string id, string source, int completed, int total)
    {
        lock (_gate)
            _running = Progress(id, source, completed, total);
    }

    private static ImportProgressNotification Progress(string id, string source, int completed, int total) =>
        new() { ImportId = id, Source = source, Completed = completed, Total = total };

    private static void RequireEnabled(SessionImportConfig config)
    {
        if (!config.Enabled)
            throw new HubProtocolException("import_disabled", "Import is disabled.", StatusCodes.Status403Forbidden);
    }

    private static void ValidateSelection(ImportSelection? selection)
    {
        try
        {
            if (selection?.User is null || selection.Workspace is null)
                throw new ArgumentException("Choose supported import categories.");
            ImportCategories.Validate(selection);
        }
        catch (ArgumentException ex)
        {
            throw Invalid(ex.Message);
        }
    }

    private static HubProtocolException Invalid(string message) =>
        new("invalidRequest", message, StatusCodes.Status400BadRequest);
}
