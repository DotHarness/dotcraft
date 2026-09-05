using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DotCraft.RemoteTools;
using DotCraft.Satellite.Localization;
using Microsoft.Windows.AppNotifications;

namespace DotCraft.Satellite.Services;

[SupportedOSPlatform("windows10.0.19041.0")]
internal static class SatelliteUninstall
{
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const int IDYES = 6;

    public static void Run(bool prompt = true)
    {
        var executable = Environment.ProcessPath ?? string.Empty;
        if (executable.Length > 0)
            new ShellIntegration(new WindowsRegistryStore(), executable).RemoveAll();

        try { AppNotificationManager.Default.Unregister(); }
        catch (Exception) { }

        var runtime = RemoteToolHostRuntime.Create();
        var peers = runtime.Peers;
        if (peers.Count == 0)
            return;

        var strings = SatelliteStrings.Current;
        if (prompt && !Confirm(strings.Format("uninstall.revokePrompt", peers.Count), strings["uninstall.title"]))
            return;
        foreach (var peer in peers)
            runtime.RevokeAsync(peer.PeerId).GetAwaiter().GetResult();
    }

    private static bool Confirm(string text, string caption) =>
        MessageBoxW(0, text, caption, MB_YESNO | MB_ICONQUESTION) == IDYES;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
