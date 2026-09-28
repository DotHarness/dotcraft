using System.Diagnostics;
using System.Text;
using DotCraft.Security.ShellCommands;

namespace DotCraft.Tools.BackgroundTerminals;

internal static class TerminalProcessStartInfo
{
    public static ProcessStartInfo Create(BackgroundTerminalStartRequest request, ShellIdentity shell)
    {
        var info = new ProcessStartInfo(shell.ExecutablePath)
        {
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        switch (shell.Family)
        {
            case ShellFamily.Cmd:
                info.Arguments = "/d /s /c \"" + request.Command + "\"";
                break;
            case ShellFamily.PowerShell:
                var script = "$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + request.Command;
                var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                info.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
                break;
        }
        return info;
    }
}
