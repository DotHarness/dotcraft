using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateExecCommand()
    {
        var prompt = new Argument<string[]>("prompt")
        {
            Description = "Prompt to send to the agent.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var stdin = Flag("--stdin", "Read the prompt from standard input.");
        var remote = StringOption("--remote", "Connect to an existing AppServer WebSocket endpoint.");
        var token = StringOption("--token", "Authenticate to the remote AppServer with this token.");
        var command = new Command("exec", "Run one agent task non-interactively.")
        {
            prompt,
            stdin,
            remote,
            token
        };
        command.Validators.Add(result =>
        {
            var values = result.GetValue(prompt) ?? [];
            if (values.Length == 0 && !result.GetValue(stdin))
                result.AddError("Provide a prompt or use --stdin.");
            if (values.Length > 0 && result.GetValue(stdin))
                result.AddError("A prompt cannot be combined with --stdin.");
        });
        command.SetAction((parseResult, cancellationToken) =>
        {
            var values = parseResult.GetValue(prompt) ?? [];
            var readStdin = parseResult.GetValue(stdin) || values is ["-"];
            return RunApplicationAsync(new CommandLineArgs
            {
                Mode = CommandLineArgs.RunMode.Exec,
                ExecPrompt = readStdin ? null : string.Join(' ', values).Trim(),
                ExecReadStdin = readStdin,
                RemoteUrl = parseResult.GetValue(remote),
                Token = parseResult.GetValue(token),
                ReservesStdout = true
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateAppServerCommand()
    {
        var listen = StringOption("--listen", "Transport URL: stdio://, ws://host:port, or ws+stdio://host:port.");
        listen.Validators.Add(result =>
        {
            try
            {
                CommandLineArgs.ParseListenUrl(result.GetValueOrDefault<string?>());
            }
            catch (ArgumentException ex)
            {
                result.AddError(ex.Message);
            }
        });
        var token = StringOption("--token", "Require this token for WebSocket connections.");
        var command = new Command("app-server", "Run the DotCraft AppServer protocol host.")
        {
            listen,
            token
        };
        command.SetAction((parseResult, cancellationToken) =>
        {
            var listenUrl = parseResult.GetValue(listen);
            var mode = CommandLineArgs.ParseListenUrl(listenUrl).Mode;
            return RunApplicationAsync(new CommandLineArgs
            {
                Mode = CommandLineArgs.RunMode.AppServer,
                ListenUrl = listenUrl,
                Token = parseResult.GetValue(token),
                ReservesStdout = mode != AppServer.AppServerMode.WebSocket
            }, cancellationToken);
        });
        return command;
    }

    private static Command CreateAcpCommand()
    {
        var remote = StringOption("--remote", "Connect the ACP bridge to an existing AppServer WebSocket endpoint.");
        var token = StringOption("--token", "Authenticate to the remote AppServer with this token.");
        var command = new Command("acp", "Run the ACP bridge over standard input and output.")
        {
            remote,
            token
        };
        command.SetAction((parseResult, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Acp,
            RemoteUrl = parseResult.GetValue(remote),
            Token = parseResult.GetValue(token),
            ReservesStdout = true
        }, cancellationToken));
        return command;
    }

    private static Command CreateDashboardCommand()
    {
        var workspace = StringOption("--workspace", "Workspace directory to open.");
        var host = StringOption("--host", "HTTP host to bind.");
        var port = new Option<int?>("--port") { Description = "HTTP port to bind." };
        port.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is <= 0 or > 65535)
                result.AddError("--port must be between 1 and 65535.");
        });
        var command = new Command("dashboard", "Run the read-only workspace dashboard.")
        {
            workspace,
            host,
            port
        };
        command.SetAction((parseResult, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Dashboard,
            DashboardWorkspacePath = parseResult.GetValue(workspace),
            DashboardHost = parseResult.GetValue(host),
            DashboardPort = parseResult.GetValue(port)
        }, cancellationToken));
        return command;
    }

    private static Command CreateSetupCommand()
    {
        var model = StringOption("--model", "Model id to select for this workspace.");
        var preference = StringOption("--preference-json", "Model preference JSON.");
        var endpoint = StringOption("--endpoint", "Provider API endpoint.");
        var apiKey = StringOption("--api-key", "Provider API key.");
        var providerMode = StringOption("--provider-mode", "Provider mode: existing, create, or skip.");
        providerMode.AcceptOnlyFromAmong("existing", "create", "skip");
        var providerId = StringOption("--provider-id", "Provider id to use or create.");
        var providerDisplayName = StringOption("--provider-display-name", "Display name for a new provider.");
        var providerProtocol = StringOption("--provider-protocol", "Protocol for a new provider.");
        var providerTimeout = StringOption("--provider-timeout-seconds", "Positive provider network timeout in seconds.");
        var authMethod = StringOption("--auth-method", "Authentication method for a new provider.");
        var saveUserConfig = Flag("--save-user-config", "Save the provider selection as the user default.");
        var preferUserConfig = Flag("--prefer-existing-user-config", "Prefer an existing user-level provider selection.");
        var setUserDefault = Flag("--set-user-default", "Set the selected provider as the user default.");
        var skipProvider = Flag("--skip-provider", "Create the workspace without configuring a provider.");
        var command = new Command("setup", "Initialize or update the current DotCraft workspace.")
        {
            model,
            preference,
            endpoint,
            apiKey,
            providerMode,
            providerId,
            providerDisplayName,
            providerProtocol,
            providerTimeout,
            authMethod,
            saveUserConfig,
            preferUserConfig,
            setUserDefault,
            skipProvider
        };
        command.SetAction((parseResult, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Setup,
            SetupModel = parseResult.GetValue(model),
            SetupPreferenceJson = parseResult.GetValue(preference),
            SetupEndPoint = parseResult.GetValue(endpoint),
            SetupApiKey = parseResult.GetValue(apiKey),
            SetupProviderMode = parseResult.GetValue(providerMode),
            SetupProviderId = parseResult.GetValue(providerId),
            SetupProviderDisplayName = parseResult.GetValue(providerDisplayName),
            SetupProviderProtocol = parseResult.GetValue(providerProtocol),
            SetupProviderTimeoutSeconds = parseResult.GetValue(providerTimeout),
            SetupAuthMethod = parseResult.GetValue(authMethod),
            SaveUserConfig = parseResult.GetValue(saveUserConfig),
            PreferExistingUserConfig = parseResult.GetValue(preferUserConfig),
            SetupSetUserDefault = parseResult.GetValue(setUserDefault),
            SetupSkipProvider = parseResult.GetValue(skipProvider)
        }, cancellationToken));
        return command;
    }
}
