using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DotCraft.TraceViewer.Services;
using DotCraft.TraceViewer.Analysis;

namespace DotCraft.TraceViewer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int EventPageSize = 250;
    private readonly TraceViewerSettingsStore _settingsStore;
    private readonly ITraceAnalystService _analyst;
    private readonly TraceReviewStore _reviewStore;
    private readonly List<Tracing.TraceEvent> _loadedEvents = [];
    private readonly HashSet<string> _collapsedTurns = new(StringComparer.Ordinal);
    private readonly HashSet<string> _collapsedModelCalls = new(StringComparer.Ordinal);
    private string? _recentWorkspacePath;
    private WorkspaceTraceSource? _source;
    private string? _oldestCursor;
    private bool _hasOlderEvents;
    private int _trajectoryLoadVersion;
    private bool _disposed;

    internal MainViewModel(
        TraceViewerSettingsStore settingsStore,
        ITraceAnalystService analyst,
        TraceReviewStore reviewStore)
    {
        _settingsStore = settingsStore;
        _analyst = analyst;
        _reviewStore = reviewStore;
        _recentWorkspacePath = settingsStore.Load().RecentWorkspacePath;
        ReviewMessages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasReviewMessages));
        OnPropertyChanged(nameof(HasRecentWorkspace));
    }

    public ObservableCollection<SessionListItem> Sessions { get; } = [];

    public ObservableCollection<SessionListItem> VisibleSessions { get; } = [];

    public ObservableCollection<TurnGroupItem> VisibleTurns { get; } = [];

    public ObservableCollection<TrajectoryListItem> VisibleTrajectory { get; } = [];

    public IReadOnlyList<TimelineMarkerItem> TimelineMarkers { get; private set; } = [];

    public IReadOnlyList<string> EventFilters { get; } = ["Activity", "All events", "Diagnostics"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    [NotifyPropertyChangedFor(nameof(CanLoadOlderEvents))]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [NotifyPropertyChangedFor(nameof(CanAskReview))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadOlderButtonText))]
    public partial bool IsLoadingOlderEvents { get; set; }

    [ObservableProperty]
    public partial string WorkspacePath { get; set; } = "No workspace open";

    [ObservableProperty]
    public partial string WorkspaceName { get; set; } = "Open a DotCraft workspace";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceClosed))]
    public partial bool IsWorkspaceOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkbenchPage))]
    public partial bool IsSessionsPage { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSessionPaneOpen { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TitleBarSubtitle))]
    public partial bool IsCompactLayout { get; set; }

    [ObservableProperty]
    public partial string SessionSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSessionTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedSessionKey))]
    [NotifyPropertyChangedFor(nameof(SelectedActivity))]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    public partial SessionListItem? SelectedSession { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedEvent))]
    public partial EventRowItem? SelectedEvent { get; set; }

    [ObservableProperty]
    public partial DetailSectionItem? SelectedDetailSection { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedEventFilter { get; set; } = "Activity";

    [ObservableProperty]
    public partial TimelineScaleMode TimelineScaleMode { get; set; } = TimelineScaleMode.Sequence;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimelineZoomLabel))]
    public partial double TimelineZoomFactor { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimelineMode))]
    [NotifyPropertyChangedFor(nameof(IsReviewMode))]
    public partial string SessionMode { get; set; } = "Timeline";

    [ObservableProperty]
    public partial int SessionCount { get; set; }

    [ObservableProperty]
    public partial int TotalRequests { get; set; }

    [ObservableProperty]
    public partial int TotalToolCalls { get; set; }

    [ObservableProperty]
    public partial int TotalErrors { get; set; }

    public bool HasRecentWorkspace => !string.IsNullOrWhiteSpace(_recentWorkspacePath);

    public bool CanRefresh => _source is not null && !IsBusy;

    public bool IsWorkbenchPage => !IsSessionsPage;

    public bool IsWorkspaceClosed => !IsWorkspaceOpen;

    public bool HasSessionResults => VisibleSessions.Count > 0;

    public string TitleBarSubtitle => !IsCompactLayout && IsWorkspaceOpen ? WorkspaceName : string.Empty;

    public string WorkspaceSummary => $"{SessionCount:N0} sessions · {TotalRequests:N0} requests · {TotalToolCalls:N0} tools · {TotalErrors:N0} errors";

    public string SessionResultsHeading => VisibleSessions.Count == Sessions.Count
        ? SessionHeading
        : $"{VisibleSessions.Count:N0} of {Sessions.Count:N0} sessions";

    public bool ShowLoadOlderEvents => _source is not null && _hasOlderEvents;

    public bool CanLoadOlderEvents => ShowLoadOlderEvents && !IsBusy;

    public string LoadOlderButtonText => IsLoadingOlderEvents
        ? "Loading earlier history…"
        : "Load earlier history";

    public bool HasSelectedEvent => SelectedEvent is not null;

    public string EventWindowLabel => _loadedEvents.Count == 0
        ? "No events"
        : _hasOlderEvents ? $"{_loadedEvents.Count:N0} loaded · older available" : $"{_loadedEvents.Count:N0} events";

    public string SessionHeading => SessionCount == 1 ? "1 session" : $"{SessionCount:N0} sessions";

    public string TimelineZoomLabel => $"{TimelineZoomFactor:P0}";

    public bool IsTimelineMode => SessionMode == "Timeline";

    public bool IsReviewMode => SessionMode == "Review";

    public string SelectedSessionTitle => SelectedSession?.DisplayTitle ?? "Select a session to inspect its summary";

    public string SelectedSessionKey => SelectedSession?.SessionKey ?? string.Empty;

    public string SelectedActivity => SelectedSession?.DetailedActivity ?? "—";

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        CancelReviewOperation(invalidate: true);
        _collapsedTurns.Clear();
        _collapsedModelCalls.Clear();
        if (value is not null && _source is not null)
        {
            _ = LoadTrajectoryAsync(value.SessionKey);
            LoadReview(value.SessionKey);
        }
        else
        {
            ClearTrajectory();
            ClearReview();
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyEventFilter();

    partial void OnSelectedEventFilterChanged(string value) => ApplyEventFilter();

    partial void OnSelectedEventChanged(EventRowItem? value)
    {
        SelectedDetailSection = value?.Detail.Sections.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedEvent));
    }

    partial void OnSessionSearchTextChanged(string value) => ApplySessionFilter();

    public void OpenSession(SessionListItem session)
    {
        ArgumentNullException.ThrowIfNull(session);
        SelectedSession = session;
        IsSessionsPage = false;
    }

    public void ShowSessions()
    {
        IsSessionsPage = true;
        IsSessionPaneOpen = true;
    }

    public void ToggleSessionPane() => IsSessionPaneOpen = !IsSessionPaneOpen;

    public void SelectEvent(string rowId)
    {
        var row = VisibleTurns.SelectMany(static turn => turn).FirstOrDefault(item => item.Id == rowId);
        if (row is not null)
            SelectedEvent = row;
    }

    public void ToggleTrajectoryGroup(TrajectoryListItem item)
    {
        var target = item switch
        {
            TurnHeaderItem turn => _collapsedTurns,
            ModelCallHeaderItem => _collapsedModelCalls,
            _ => null,
        };
        if (target is null)
            return;

        if (!target.Add(item.Id))
            target.Remove(item.Id);
        RebuildVisibleTrajectory();
    }

    public Task OpenRecentWorkspaceAsync()
    {
        if (_recentWorkspacePath is null)
            return Task.CompletedTask;

        return OpenWorkspaceAsync(_recentWorkspacePath);
    }

    public async Task OpenWorkspaceAsync(string workspacePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy)
            return;

        IsBusy = true;
        ClearError();
        try
        {
            await StopReviewOperationAsync();
            var opened = await Task.Run(() =>
            {
                var source = WorkspaceTraceSource.Open(workspacePath);
                try
                {
                    return (Source: source, Snapshot: source.ReadSnapshot());
                }
                catch
                {
                    source.Dispose();
                    throw;
                }
            });

            var previous = _source;
            _source = opened.Source;
            previous?.Dispose();

            WorkspacePath = opened.Source.WorkspacePath;
            WorkspaceName = GetWorkspaceName(opened.Source.WorkspacePath);
            SelectedSession = null;
            ApplySnapshot(opened.Snapshot);
            IsWorkspaceOpen = true;
            IsSessionsPage = true;

            _recentWorkspacePath = WorkspacePath;
            _settingsStore.SaveRecentWorkspace(WorkspacePath);
            OnPropertyChanged(nameof(HasRecentWorkspace));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(TitleBarSubtitle));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DirectoryNotFoundException
                                          or FileNotFoundException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or Microsoft.Data.Sqlite.SqliteException)
        {
            SetError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is null || IsBusy)
            return;

        IsBusy = true;
        ClearError();
        try
        {
            ApplySnapshot(await Task.Run(_source.ReadSnapshot));
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or Microsoft.Data.Sqlite.SqliteException)
        {
            SetError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadOlderEventsAsync()
    {
        if (_source is null || SelectedSession is null || !_hasOlderEvents || IsBusy)
            return;

        IsLoadingOlderEvents = true;
        IsBusy = true;
        ClearError();
        try
        {
            var page = await Task.Run(() => _source.ReadEventPage(
                SelectedSession.SessionKey,
                EventPageSize,
                _oldestCursor));
            var knownIds = _loadedEvents.Select(static item => item.Id).ToHashSet(StringComparer.Ordinal);
            _loadedEvents.InsertRange(0, page.Events.Where(item => knownIds.Add(item.Id)));
            _oldestCursor = page.OldestCursor;
            _hasOlderEvents = page.HasMore;
            ApplyEventFilter();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or Microsoft.Data.Sqlite.SqliteException)
        {
            SetError(exception.Message);
        }
        finally
        {
            IsBusy = false;
            IsLoadingOlderEvents = false;
            NotifyTrajectoryState();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelReviewOperation(invalidate: true);
        _source?.Dispose();
        _source = null;
        _analyst.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private void ApplySnapshot(WorkspaceTraceSnapshot snapshot)
    {
        var selectedKey = SelectedSession?.SessionKey;
        Sessions.Clear();
        foreach (var session in snapshot.Sessions)
            Sessions.Add(session);

        SessionCount = snapshot.Summary.SessionCount;
        TotalRequests = snapshot.Summary.TotalRequests;
        TotalToolCalls = snapshot.Summary.TotalToolCalls;
        TotalErrors = snapshot.Summary.TotalErrors;
        SelectedSession = Sessions.FirstOrDefault(session => session.SessionKey == selectedKey)
            ?? SelectedSession;
        ApplySessionFilter();
        OnPropertyChanged(nameof(SessionHeading));
        OnPropertyChanged(nameof(WorkspaceSummary));
    }

    private void ApplySessionFilter()
    {
        var query = SessionSearchText.Trim();
        VisibleSessions.Clear();
        foreach (var session in Sessions.Where(session =>
                     query.Length == 0
                     || session.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            VisibleSessions.Add(session);
        }

        OnPropertyChanged(nameof(HasSessionResults));
        OnPropertyChanged(nameof(SessionResultsHeading));
    }

    private static string GetWorkspaceName(string workspacePath)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(workspacePath));
        return string.IsNullOrWhiteSpace(name) ? workspacePath : name;
    }

    private async Task LoadTrajectoryAsync(string sessionKey)
    {
        if (_source is null)
            return;

        var version = Interlocked.Increment(ref _trajectoryLoadVersion);
        IsBusy = true;
        ClearError();
        try
        {
            var page = await Task.Run(() => _source.ReadEventPage(sessionKey, EventPageSize));
            if (version != _trajectoryLoadVersion || SelectedSession?.SessionKey != sessionKey)
                return;

            _loadedEvents.Clear();
            _loadedEvents.AddRange(page.Events);
            _oldestCursor = page.OldestCursor;
            _hasOlderEvents = page.HasMore;
            ApplyEventFilter();
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or Microsoft.Data.Sqlite.SqliteException)
        {
            SetError(exception.Message);
        }
        finally
        {
            if (version == _trajectoryLoadVersion)
            {
                IsBusy = false;
                NotifyTrajectoryState();
            }
        }
    }

    private void ApplyEventFilter()
    {
        var selectedId = SelectedEvent?.Id;
        var query = SearchText.Trim();
        var projected = TrajectoryProjection.Project(_loadedEvents, _hasOlderEvents);
        var visibleIds = new HashSet<string>(StringComparer.Ordinal);
        VisibleTurns.Clear();
        foreach (var sourceTurn in projected.Turns)
        {
            var visibleRows = sourceTurn.Where(item => IsVisible(item, query)).ToArray();
            if (visibleRows.Length == 0)
                continue;

            var turn = new TurnGroupItem
            {
                Key = sourceTurn.Key,
                Title = sourceTurn.Title,
                Summary = sourceTurn.Summary,
            };
            foreach (var row in visibleRows)
            {
                turn.Add(row);
                visibleIds.Add(row.Id);
            }

            VisibleTurns.Add(turn);
        }

        RebuildVisibleTrajectory();

        TimelineMarkers = projected.Timeline.Where(marker => visibleIds.Contains(marker.RowId)).ToArray();
        OnPropertyChanged(nameof(TimelineMarkers));
        var rows = VisibleTurns.SelectMany(static turn => turn).ToArray();
        SelectedEvent = rows.FirstOrDefault(item => item.Id == selectedId) ?? rows.FirstOrDefault();
        NotifyTrajectoryState();
    }

    private bool IsVisible(EventRowItem item, string query)
    {
        var modeMatch = SelectedEventFilter switch
        {
            "Activity" => !item.IsDiagnostic,
            "Diagnostics" => item.IsDiagnostic,
            _ => true,
        };
        return modeMatch && (query.Length == 0
            || item.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private void ClearTrajectory()
    {
        Interlocked.Increment(ref _trajectoryLoadVersion);
        _loadedEvents.Clear();
        VisibleTurns.Clear();
        VisibleTrajectory.Clear();
        TimelineMarkers = [];
        OnPropertyChanged(nameof(TimelineMarkers));
        SelectedEvent = null;
        _oldestCursor = null;
        _hasOlderEvents = false;
        NotifyTrajectoryState();
    }

    private void RebuildVisibleTrajectory()
    {
        VisibleTrajectory.Clear();
        foreach (var turn in VisibleTurns)
        {
            var turnCollapsed = _collapsedTurns.Contains(turn.Key);
            VisibleTrajectory.Add(new TurnHeaderItem
            {
                Id = turn.Key,
                Title = turn.Title,
                Summary = turn.Summary,
                IsCollapsed = turnCollapsed,
            });
            if (turnCollapsed)
                continue;

            int? activeCall = null;
            var callCollapsed = false;
            foreach (var row in turn)
            {
                if (row.ModelCallIndex != activeCall)
                {
                    activeCall = row.ModelCallIndex;
                    callCollapsed = false;
                    if (activeCall is { } callIndex)
                    {
                        var key = $"{turn.Key}:call:{callIndex}";
                        callCollapsed = _collapsedModelCalls.Contains(key);
                        var count = turn.Count(item => item.ModelCallIndex == callIndex);
                        VisibleTrajectory.Add(new ModelCallHeaderItem
                        {
                            Id = key,
                            Title = $"Model call {callIndex}",
                            Summary = count == 1 ? "1 event" : $"{count:N0} events",
                            IsCollapsed = callCollapsed,
                        });
                    }
                }

                if (!callCollapsed)
                    VisibleTrajectory.Add(row);
            }
        }
    }

    private void NotifyTrajectoryState()
    {
        OnPropertyChanged(nameof(ShowLoadOlderEvents));
        OnPropertyChanged(nameof(CanLoadOlderEvents));
        OnPropertyChanged(nameof(EventWindowLabel));
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        HasError = false;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
