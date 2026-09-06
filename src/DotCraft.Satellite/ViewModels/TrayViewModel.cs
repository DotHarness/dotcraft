using System.Diagnostics;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Consent;
using DotCraft.Satellite.Localization;
using DotCraft.Satellite.Services;
using DotCraft.Satellite.Tray;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace DotCraft.Satellite.ViewModels;

internal sealed class TrayViewModel(
    SatelliteRuntimeConnection connection,
    ITrayIcon tray,
    ToastPresenter toasts,
    SatelliteStrings strings,
    DispatcherQueue dispatcher)
{
    private ConsentWindow? _consent;

    public void Start()
    {
        var runtime = connection.Runtime;
        runtime.StatusChanged += (_, _) => Post(Refresh);
        runtime.ActivityChanged += (_, _) => Post(Refresh);
        runtime.PeerConnected += (_, peer) => Post(() =>
        {
            toasts.Show(
                strings.Format("toast.connected.title", peer.DisplayName),
                strings.Format("toast.connected.body", DescribeFolder(peer)));
            Refresh();
        });
        runtime.PeerDisconnected += (_, peer) => Post(() =>
        {
            toasts.Show(
                strings.Format("toast.disconnected.title", peer.DisplayName),
                strings["toast.disconnected.body"]);
            Refresh();
        });
        tray.MenuRequested += (_, _) => Post(ShowMenu);

        connection.Start();
        Refresh();
    }

    public void Refresh()
    {
        var state = SatelliteStateMachine.Evaluate(connection.Runtime.Status, connection.PauseRequested);
        tray.SetState(
            SatelliteStateMachine.IconName(state),
            strings["app.name"] + " — " + strings[SatelliteStateMachine.StatusKey(state)]);
    }

    public void HandleInstanceMessage(InstanceMessage message) => PostAsync(async () =>
    {
        if (string.Equals(message.Kind, InstanceMessage.JoinKind, StringComparison.Ordinal))
        {
            await ShowConsentAsync(message.Url);
            return;
        }

        var state = SatelliteStateMachine.Evaluate(connection.Runtime.Status, connection.PauseRequested);
        toasts.Show(strings["app.name"], strings[SatelliteStateMachine.StatusKey(state)]);
    });

    public async Task ShowConsentAsync(string? link)
    {
        if (!SatelliteDeepLink.TryParse(link, out var inviteUrl))
        {
            ShowInviteFailure();
            return;
        }

        RemoteToolInvite invite;
        try
        {
            invite = RemoteToolHostRuntime.ParseInvite(inviteUrl);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            ShowInviteFailure();
            return;
        }

        invite = await RemoteToolHostRuntime.ResolveInviteAsync(invite);
        _consent?.Close();

        ConsentWindow? window = null;
        var viewModel = new ConsentViewModel(
            invite,
            new WindowFolderPicker(() =>
                window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(window)),
            (folder, cancellationToken) => AcceptAsync(invite, folder, cancellationToken),
            strings);
        window = new ConsentWindow(viewModel);
        _consent = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_consent, window))
                _consent = null;
            Refresh();
        };
        window.Activate();
    }

    private async Task AcceptAsync(
        RemoteToolInvite invite,
        string folder,
        CancellationToken cancellationToken)
    {
        await connection.Runtime.AcceptInviteAsync(
            new RemoteToolJoinDecision(invite, folder),
            cancellationToken);
        await connection.RestartAsync();
        Refresh();
    }

    private void ShowMenu()
    {
        var state = SatelliteStateMachine.Evaluate(connection.Runtime.Status, connection.PauseRequested);
        var items = TrayMenuModel.Build(
            state,
            connection.Runtime.Peers,
            connection.Runtime.CurrentActivity,
            strings);
        if (tray.ShowMenu(items) is { } chosen)
            PostAsync(() => InvokeAsync(chosen));
    }

    private async Task InvokeAsync(TrayMenuItem item)
    {
        switch (item.Command)
        {
            case TrayMenuCommand.Disconnect:
                foreach (var peer in connection.Runtime.Peers.Where(peer => peer.ConnectedSince is not null))
                    await connection.Runtime.DisconnectAsync(peer.PeerId);
                break;
            case TrayMenuCommand.PauseSharing:
                await connection.SetPausedAsync(paused: true);
                break;
            case TrayMenuCommand.ResumeSharing:
                await connection.SetPausedAsync(paused: false);
                break;
            case TrayMenuCommand.Revoke when item.PeerId is { Length: > 0 } peerId:
                await connection.Runtime.RevokeAsync(peerId);
                break;
            case TrayMenuCommand.OpenFolder:
                OpenFolder();
                break;
            case TrayMenuCommand.PasteInvite:
                await PasteInviteAsync();
                break;
            case TrayMenuCommand.Quit:
                Microsoft.UI.Xaml.Application.Current.Exit();
                return;
            default:
                break;
        }

        Refresh();
    }

    private void OpenFolder()
    {
        var folder = connection.Runtime.Peers
            .Select(peer => peer.WorkspacePath)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path) && Directory.Exists(path));
        if (folder is null)
            return;
        using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private async Task PasteInviteAsync()
    {
        string? text = null;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
                text = await content.GetTextAsync();
        }
        catch (Exception)
        {
            text = null;
        }

        if (SatelliteDeepLink.TryParse(text, out _))
        {
            await ShowConsentAsync(text);
            return;
        }

        toasts.Show(strings["toast.pasteFailed.title"], strings["toast.pasteFailed.body"]);
    }

    private void ShowInviteFailure() =>
        toasts.Show(strings["toast.inviteFailed.title"], strings["toast.inviteFailed.body"]);

    private static string DescribeFolder(RemoteToolPeer peer) =>
        string.IsNullOrEmpty(peer.WorkspacePath) ? peer.WorkspaceId : peer.WorkspacePath;

    private void Post(Action action) => dispatcher.TryEnqueue(() => action());

    private void PostAsync(Func<Task> action) => dispatcher.TryEnqueue(() => _ = action());
}
