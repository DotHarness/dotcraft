using System.Diagnostics;

namespace DotCraft.Satellite.Services;

/// <summary>The owner actions the tray menu and the island share.</summary>
internal sealed class SatelliteCommands(SatelliteRuntimeConnection connection)
{
    public Task PauseAsync() => connection.SetPausedAsync(paused: true);

    public Task ResumeAsync() => connection.SetPausedAsync(paused: false);

    public Task DisconnectAsync(string peerId) => connection.Runtime.DisconnectAsync(peerId);

    /// <summary>Opens a pairing's task folder, and does nothing when that folder has gone.</summary>
    public void OpenFolder(string peerId)
    {
        var folder = connection.Runtime.Peers
            .FirstOrDefault(peer => peer.PeerId == peerId)?.WorkspacePath;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;
        using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
}
