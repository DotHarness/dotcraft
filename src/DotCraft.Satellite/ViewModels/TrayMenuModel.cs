using System.Globalization;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;

namespace DotCraft.Satellite.ViewModels;

internal enum TrayMenuCommand
{
    Header,
    Separator,
    Disconnect,
    PauseSharing,
    ResumeSharing,
    Revoke,
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
        var connected = peers.Where(peer => peer.ConnectedSince is not null).ToArray();
        var items = new List<TrayMenuItem>
        {
            new(TrayMenuCommand.Header, strings[SatelliteStateMachine.StatusKey(state)], Enabled: false)
        };

        if (peers.Count == 0)
        {
            items.Add(new TrayMenuItem(TrayMenuCommand.Header, strings["tray.noPeers"], Enabled: false));
        }
        else
        {
            foreach (var peer in connected)
            {
                items.Add(new TrayMenuItem(
                    TrayMenuCommand.Header,
                    strings.Format(
                        "tray.peer",
                        peer.DisplayName,
                        peer.ConnectedSince!.Value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)),
                    Enabled: false));
            }
        }

        if (activity is not null)
        {
            items.Add(new TrayMenuItem(
                TrayMenuCommand.Header,
                strings.Format("tray.activity", activity.CommandPreview ?? activity.ToolName),
                Enabled: false));
        }

        items.Add(new TrayMenuItem(TrayMenuCommand.Separator, string.Empty, Enabled: false));
        items.Add(new TrayMenuItem(
            TrayMenuCommand.Disconnect,
            strings["tray.disconnect"],
            connected.Length > 0));
        items.Add(state == SatelliteTrayState.Paused
            ? new TrayMenuItem(TrayMenuCommand.ResumeSharing, strings["tray.resume"], peers.Count > 0)
            : new TrayMenuItem(TrayMenuCommand.PauseSharing, strings["tray.pause"], peers.Count > 0));
        items.Add(new TrayMenuItem(
            TrayMenuCommand.Revoke,
            strings["tray.revoke"],
            peers.Count > 0,
            Children:
            [
                .. peers.Select(peer => new TrayMenuItem(
                    TrayMenuCommand.Revoke,
                    peer.DisplayName,
                    PeerId: peer.PeerId))
            ]));
        items.Add(new TrayMenuItem(
            TrayMenuCommand.OpenFolder,
            strings["tray.openFolder"],
            peers.Any(peer => !string.IsNullOrEmpty(peer.WorkspacePath))));
        items.Add(new TrayMenuItem(TrayMenuCommand.PasteInvite, strings["tray.pasteInvite"]));
        items.Add(new TrayMenuItem(TrayMenuCommand.Separator, string.Empty, Enabled: false));
        items.Add(new TrayMenuItem(TrayMenuCommand.Quit, strings["tray.quit"]));
        return items;
    }
}
