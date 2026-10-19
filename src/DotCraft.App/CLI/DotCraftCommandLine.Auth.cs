using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateAuthCommand()
    {
        var auth = new Command("auth", "Sign providers in and out.");
        auth.Subcommands.Add(CreateAuthLoginCommand());
        auth.Subcommands.Add(CreateAuthProviderCommand("status", "Show a provider's sign-in status and usage.", providerRequired: true));
        auth.Subcommands.Add(CreateAuthProviderCommand("logout", "Sign a provider out.", providerRequired: true));
        auth.Subcommands.Add(CreateAuthProviderCommand("verify", "Refresh credentials and check the preferred model is listed.", providerRequired: false));
        return auth;
    }

    private static Command CreateAuthLoginCommand()
    {
        var providerId = new Argument<string?>("provider-id")
        {
            Description = "Configured provider identifier.",
            Arity = ArgumentArity.ZeroOrOne
        };
        var stdin = Flag("--stdin", "Read an unsaved provider draft from standard input.");
        var method = StringOption("--method", "Authentication method to sign in with.");
        var workspace = StringOption("--workspace", "Workspace whose configuration and plugins define the provider.");
        var noBrowser = Flag("--no-browser", "Print the authorization URL without opening a browser.");
        var command = new Command("login", "Sign a provider in.") { providerId, stdin, method, workspace, noBrowser };
        command.Validators.Add(result =>
        {
            var hasProvider = !string.IsNullOrWhiteSpace(result.GetValue(providerId));
            var readsStdin = result.GetValue(stdin);
            if (hasProvider == readsStdin)
                result.AddError("Specify either a provider id or --stdin.");
            else if (readsStdin && result.GetValue(workspace) is not null)
                result.AddError("--workspace cannot be combined with --stdin.");
        });
        command.SetAction((parsed, ct) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Auth,
            AuthAction = "login",
            SetupProviderId = parsed.GetValue(providerId),
            AuthReadStdin = parsed.GetValue(stdin),
            AuthMethod = parsed.GetValue(method),
            AuthWorkspacePath = parsed.GetValue(workspace),
            AuthNoBrowser = parsed.GetValue(noBrowser)
        }, ct));
        return command;
    }

    private static Command CreateAuthProviderCommand(string action, string description, bool providerRequired)
    {
        var providerId = new Argument<string?>("provider-id")
        {
            Description = providerRequired ? "Configured provider identifier." : "Configured provider identifier; defaults to the selected provider.",
            Arity = providerRequired ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne
        };
        var workspace = StringOption("--workspace", "Workspace whose configuration and plugins define the provider.");
        var command = new Command(action, description) { providerId, workspace };
        command.SetAction((parsed, ct) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Auth,
            AuthAction = action,
            SetupProviderId = parsed.GetValue(providerId),
            AuthWorkspacePath = parsed.GetValue(workspace)
        }, ct));
        return command;
    }
}
