using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateCodexRemoteControlCommand()
    {
        var group = new Command("codex-remote-control", "Manage Codex access to this computer through DotCraft Hub.");
        foreach (var (name, description) in new[]
        {
            ("status", "Show Codex access status."),
            ("enable", "Enable Codex access to this computer's projects."),
            ("disable", "Disable Codex access without stopping project tasks."),
            ("pair", "Create a pairing link and manual code for Codex.")
        })
        {
            var command = new Command(name, description);
            command.SetAction((parse, cancellationToken) => CodexRemoteControlCliRunner.RunAsync(
                name, parse.InvocationConfiguration.Output, parse.InvocationConfiguration.Error, cancellationToken));
            group.Subcommands.Add(command);
        }
        return group;
    }
}
