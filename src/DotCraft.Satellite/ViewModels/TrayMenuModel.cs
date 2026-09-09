using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;

namespace DotCraft.Satellite.ViewModels;

internal enum TrayMenuCommand
{
    Header,
    Separator,
    Machine,
    Disconnect,
    PauseSharing,
    ResumeSharing,
    Revoke,
    ManageAccess,
    OpenFolder,
    PasteInvite,
    Quit
}

internal sealed record TrayMenuItem(
    TrayMenuCommand Command,
    string Text,
    bool Enabled = true,
    string? PeerId = null,
    IReadOnlyList<TrayMenuItem>? Children = null);

internal static class TrayMenuModel
{
    public static IReadOnlyList<TrayMenuItem> Build(
        SatelliteTrayState state,
        IReadOnlyList<RemoteToolPeer> peers,
        RemoteToolActivity? activity,
        SatelliteStrings strings)
    {
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.Header, strings[SatelliteStateMachine.StatusKey(state)], Enabled: false)
        };

        if (peers.Count == 0)
            items.Add(new TrayMenuItem(TrayMenuCommand.Header, strings["tray.noPeers"], Enabled: false));

        if (activity is not null)
        {
            items.Add(new TrayMenuItem(
                TrayMenuCommand.Header,
                strings.Format("tray.activity", activity.CommandPreview ?? activity.ToolName),
                Enabled: false));
        }

        // One submenu per machine: the owner picks who first, then what to do about them.
        if (peers.Count > 0)
        {
            items.Add(Separator);
            foreach (var peer in peers)
                items.Add(Machine(peer, strings));
        }

        items.Add(Separator);
        items.Add(state == SatelliteTrayState.Paused
            ? new TrayMenuItem(TrayMenuCommand.ResumeSharing, strings["tray.resume"], peers.Count > 0)
            : new TrayMenuItem(TrayMenuCommand.PauseSharing, strings["tray.pause"], peers.Count > 0));
        items.Add(new TrayMenuItem(TrayMenuCommand.PasteInvite, strings["tray.pasteInvite"]));
        items.Add(Separator);
        items.Add(new TrayMenuItem(TrayMenuCommand.Quit, strings["tray.quit"]));
        return items;
    }

    private static TrayMenuItem Separator =>
        new(TrayMenuCommand.Separator, string.Empty, Enabled: false);

    private static TrayMenuItem Machine(RemoteToolPeer peer, SatelliteStrings strings)
    {
        var connected = peer.ConnectedSince is not null;
        var label = connected
            ? strings.Format(
                "tray.peer",
                peer.DisplayName,
                peer.ConnectedSince!.Value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture))
            : peer.DisplayName;

        return new TrayMenuItem(TrayMenuCommand.Machine, label, Children:
        [
            new TrayMenuItem(
                TrayMenuCommand.Header,
                strings[peer.AuthorizationMode switch
                {
                    RemoteToolAuthorization.FullAccess => "consent.full",
                    RemoteToolAuthorization.WorkspacePreferred => "consent.preferred",
                    _ => "consent.review"
                }],
                Enabled: false),
            Separator,
            new TrayMenuItem(
                TrayMenuCommand.OpenFolder,
                strings["tray.openFolder"],
                !string.IsNullOrEmpty(peer.WorkspacePath),
                PeerId: peer.PeerId),
            new TrayMenuItem(
                TrayMenuCommand.ManageAccess,
                strings["tray.permissions"],
                PeerId: peer.PeerId),
            new TrayMenuItem(
                TrayMenuCommand.Disconnect,
                strings["tray.disconnect"],
                connected,
                PeerId: peer.PeerId),
            Separator,
            new TrayMenuItem(TrayMenuCommand.Revoke, strings["tray.revoke"], PeerId: peer.PeerId)
        ]);
    }
}
