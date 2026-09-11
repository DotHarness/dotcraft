using CommunityToolkit.Mvvm.ComponentModel;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using Microsoft.UI.Dispatching;

namespace DotCraft.Satellite.ViewModels;

/// <summary>
/// Turns the Remote Tool Host runtime and the owner request queue into the island's state, and
/// carries the capsule's actions back to the runtime.
/// </summary>
internal sealed partial class IslandViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan HoverGrace = TimeSpan.FromMilliseconds(150);

    private static readonly TimeSpan SettleGrace = TimeSpan.FromSeconds(3);

    private readonly SatelliteStrings _strings;
    private readonly DispatcherQueue _dispatcher;
    private SatelliteCommands? _commands;
    private readonly DispatcherQueueTimer _ticker;
    private readonly DispatcherQueueTimer _hoverExit;
    private readonly DispatcherQueueTimer _settle;
    private SatelliteRuntimeConnection? _connection;
    private IReadOnlyList<RemoteToolPeer> _peers = [];
    private IReadOnlyList<RemoteToolActivity> _activities = [];
    private SatelliteTrayState _state = SatelliteTrayState.Offline;
    private bool _pinned;
    private bool _pointerOver;
    private bool _frozen;
    private bool _settled;

    public IslandViewModel(
        SatelliteStrings strings,
        DispatcherQueue dispatcher,
        IslandApprovalQueue approvals)
    {
        _strings = strings;
        _dispatcher = dispatcher;
        Approvals = approvals;
        State = new IslandStateModel();
        Approvals.Changed += (_, _) => Update();
        _ticker = dispatcher.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) => Tick();
        _hoverExit = dispatcher.CreateTimer();
        _hoverExit.Interval = HoverGrace;
        _hoverExit.IsRepeating = false;
        _hoverExit.Tick += (_, _) =>
        {
            _pointerOver = false;
            Update();
        };
        _settle = dispatcher.CreateTimer();
        _settle.Interval = SettleGrace;
        _settle.IsRepeating = false;
        _settle.Tick += (_, _) =>
        {
            _settled = true;
            Update();
        };
    }

    [ObservableProperty]
    public partial IslandStateModel State { get; set; }

    public IslandApprovalQueue Approvals { get; }

    public void Dispose()
    {
        _ticker.Stop();
        _hoverExit.Stop();
        _settle.Stop();
        if (_connection is not { } connection)
            return;
        connection.Runtime.StatusChanged -= OnStatusChanged;
        connection.Runtime.ActivityChanged -= OnActivityChanged;
        connection.Runtime.PeerConnected -= OnPeerConnected;
        connection.Runtime.PeerDisconnected -= OnPeerDisconnected;
        connection.Runtime.ScreenViewStarted -= OnScreenViewChanged;
        connection.Runtime.ScreenViewStopped -= OnScreenViewChanged;
    }

    public void Attach(SatelliteRuntimeConnection connection, SatelliteCommands commands)
    {
        _connection = connection;
        _commands = commands;
        connection.Runtime.StatusChanged += OnStatusChanged;
        connection.Runtime.ActivityChanged += OnActivityChanged;
        connection.Runtime.PeerConnected += OnPeerConnected;
        connection.Runtime.PeerDisconnected += OnPeerDisconnected;
        connection.Runtime.ScreenViewStarted += OnScreenViewChanged;
        connection.Runtime.ScreenViewStopped += OnScreenViewChanged;
        _settle.Start();
        Refresh();
    }

    /// <summary>Shows fixture data with the clock stopped, for the <c>--preview-island</c> switch.</summary>
    public void Freeze(
        IReadOnlyList<RemoteToolPeer> peers,
        IReadOnlyList<RemoteToolActivity> activities,
        IReadOnlyList<IslandApprovalEntry> approvals,
        bool pinned,
        SatelliteTrayState state = SatelliteTrayState.Connected)
    {
        _frozen = true;
        _settled = true;
        _state = state;
        _peers = peers;
        _activities = activities;
        _pinned = pinned;
        foreach (var approval in approvals)
            Approvals.Add(approval);
        Update();
    }

    public void Refresh()
    {
        if (_connection is not { } connection || _frozen)
            return;
        var next = SatelliteStateMachine.Evaluate(connection.Runtime.Status, connection.PauseRequested);
        // A pause, a revoke or a lost connection makes every waiting request meaningless.
        if (next != SatelliteTrayState.Connected)
            Approvals.Invalidate();
        if (next != SatelliteTrayState.Offline)
            _settled = true;
        _state = next;
        _peers = connection.Runtime.Peers;
        _activities = connection.Runtime.CurrentActivity is { } activity ? [activity] : [];
        if (next != SatelliteTrayState.Connected)
            _pinned = false;
        Update();
    }

    public void PointerEntered()
    {
        _hoverExit.Stop();
        _pointerOver = true;
        Update();
    }

    /// <summary>Leaves a moment of grace so the pointer may cross a gap without collapsing the list.</summary>
    public void PointerExited() => _hoverExit.Start();

    public void Toggle()
    {
        if (State.IsQuiet)
            return;
        _pinned = !_pinned;
        Update();
    }

    public void Collapse()
    {
        if (!_pinned)
            return;
        _pinned = false;
        Update();
    }

    public void Answer(bool allowed) => Approvals.Answer(allowed);

    public void Disconnect(string peerId)
    {
        Approvals.Invalidate(peerId);
        Run(commands => commands.DisconnectAsync(peerId));
    }

    public void OpenFolder(string peerId) => _commands?.OpenFolder(peerId);

    public void Pause()
    {
        _pinned = false;
        Approvals.Invalidate();
        Run(commands => commands.PauseAsync());
    }

    public void Resume() => Run(commands => commands.ResumeAsync());

    private void Run(Func<SatelliteCommands, Task> action)
    {
        if (_commands is not { } commands)
            return;
        _ = Complete(action(commands));

        async Task Complete(Task running)
        {
            await running.ConfigureAwait(true);
            Refresh();
        }
    }

    private void Tick()
    {
        Approvals.Expire(DateTimeOffset.Now);
        Update();
    }

    private void Update()
    {
        // Until the first dial is answered the capsule waits rather than claim the machine is offline.
        State = _settled
            ? IslandStateModel.Derive(
                new IslandInputs(
                    _state,
                    _peers,
                    _activities,
                    Approvals.Pending,
                    _pinned,
                    _pointerOver,
                    DateTimeOffset.Now),
                _strings)
            : new IslandStateModel();

        // Only the running elapsed time and the countdown move on their own.
        var counting = !_frozen && State.Visible && (State.ShowRunning || State.Mode == IslandMode.Approval);
        if (counting && !_ticker.IsRunning)
            _ticker.Start();
        else if (!counting && _ticker.IsRunning)
            _ticker.Stop();
    }

    private void OnStatusChanged(object? sender, RemoteToolHostStatus status) => Post(Refresh);

    private void OnActivityChanged(object? sender, RemoteToolActivity? activity) => Post(Refresh);

    private void OnPeerConnected(object? sender, RemoteToolPeer peer) => Post(Refresh);

    private void OnScreenViewChanged(object? sender, RemoteToolPeer peer) => Post(Refresh);

    private void OnPeerDisconnected(object? sender, RemoteToolPeer peer) => Post(() =>
    {
        Approvals.Invalidate(peer.PeerId);
        Refresh();
    });

    /// <summary>Runtime events arrive on the host's threads; every state change happens on the interface thread.</summary>
    private void Post(Action action) => _dispatcher.TryEnqueue(() => action());
}
