using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateProviderAuthCommand()
    {
        var group = new Command("provider", "Manage authentication for a configured backend provider.");
        foreach (var action in new[] { "login", "status", "logout" })
        {
            var providerId = RequiredArgument("provider-id", "Configured provider identifier.");
            var workspace = StringOption("--workspace", "Workspace containing the backend plugin and provider configuration.");
            var method = StringOption("--method", "Backend authentication method.");
            var command = new Command(action, $"Provider authentication: {action}.") { providerId, workspace, method };
            command.SetAction((parsed, ct) => RunApplicationAsync(new CommandLineArgs
            {
                Mode = CommandLineArgs.RunMode.ProviderAuth,
                SetupProviderId = parsed.GetRequiredValue(providerId),
                ProviderAuthAction = action,
                ProviderAuthMethod = parsed.GetValue(method),
                ProviderAuthWorkspacePath = parsed.GetValue(workspace)
            }, ct));
            group.Subcommands.Add(command);
        }
        return group;
    }
}
