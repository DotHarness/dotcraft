using System.CommandLine;
using DotCraft.RemoteTools;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateToolHostCommand()
    {
        var toolHost = new Command("tool-host", "Configure and run Remote Tool Host.");
        toolHost.Subcommands.Add(CreateToolHostSetupCommand());
        toolHost.Subcommands.Add(CreateToolHostWorkspaceCommand());
        toolHost.Subcommands.Add(CreateToolHostPolicyCommand());
        toolHost.Subcommands.Add(CreateToolHostAutostartCommand());
        toolHost.Subcommands.Add(CreateToolHostTokenCommand());
        toolHost.Subcommands.Add(CreateToolHostStatusCommand());
        toolHost.Subcommands.Add(CreateToolHostServeCommand());
        toolHost.Subcommands.Add(CreateToolHostRegisterCommand());
        toolHost.Subcommands.Add(CreateToolHostUnregisterCommand());
        toolHost.Subcommands.Add(CreateToolHostListCommand());
        toolHost.Subcommands.Add(CreateToolHostTestCommand());
        return toolHost;
    }

    private static Command CreateToolHostSetupCommand()
    {
        var endpoint = RequiredArgument("https-endpoint", "HTTPS endpoint advertised by this Host.");
        var output = new Option<string?>("--output", "-o")
        {
            Description = "Pairing file to create."
        };
        var command = new Command("setup", "Create the Host identity, token, and TLS certificate.")
        {
            endpoint,
            output
        };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.SetupAsync(
                parseResult.GetRequiredValue(endpoint),
                parseResult.GetValue(output),
                writer)));
        return command;
    }

    private static Command CreateToolHostWorkspaceCommand()
    {
        var workspace = new Command("workspace", "Manage directories exported by this Host.");

        var addId = RequiredArgument("workspace-id", "Stable workspace id.");
        var addPath = RequiredArgument("absolute-path", "Existing absolute workspace directory.");
        var add = new Command("add", "Add or update a workspace.") { addId, addPath };
        add.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.AddWorkspaceAsync(
                parseResult.GetRequiredValue(addId),
                parseResult.GetRequiredValue(addPath),
                writer)));

        var listJson = Flag("--json", "Write the workspace catalog as JSON.");
        var list = new Command("list", "List exported workspaces.") { listJson };
        list.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.ListWorkspacesAsync(parseResult.GetValue(listJson), writer)));

        var removeId = RequiredArgument("workspace-id", "Workspace id to remove.");
        var remove = new Command("remove", "Remove an exported workspace.") { removeId };
        remove.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.RemoveWorkspaceAsync(parseResult.GetRequiredValue(removeId), writer)));

        workspace.Subcommands.Add(add);
        workspace.Subcommands.Add(list);
        workspace.Subcommands.Add(remove);
        return workspace;
    }

    private static Command CreateToolHostPolicyCommand()
    {
        var policy = new Command("policy", "Manage local Host tool policies.");
        var json = Flag("--json", "Write policies as JSON.");
        var list = new Command("list", "List tool policies.") { json };
        list.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.ListPoliciesAsync(parseResult.GetValue(json), writer)));

        var toolName = RequiredArgument("tool-name", "Canonical tool name.");
        var value = RequiredArgument("policy", "Policy: allow, deny, or needs-approval.");
        value.AcceptOnlyFromAmong("allow", "deny", "needs-approval");
        var set = new Command("set", "Set the policy for one tool.") { toolName, value };
        set.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.SetPolicyAsync(
                parseResult.GetRequiredValue(toolName),
                parseResult.GetRequiredValue(value),
                writer)));

        policy.Subcommands.Add(list);
        policy.Subcommands.Add(set);
        return policy;
    }

    private static Command CreateToolHostAutostartCommand()
    {
        var autostart = new Command("autostart", "Manage current-user login autostart.");
        var install = new Command("install", "Start Remote Tool Host when the current user signs in.");
        install.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            RemoteToolHostCliRunner.InstallAutostartAsync));
        var remove = new Command("remove", "Remove Remote Tool Host login autostart.");
        remove.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            RemoteToolHostCliRunner.RemoveAutostartAsync));
        autostart.Subcommands.Add(install);
        autostart.Subcommands.Add(remove);
        return autostart;
    }

    private static Command CreateToolHostTokenCommand()
    {
        var token = new Command("token", "Manage the Host pairing token.");
        var output = new Option<string?>("--output", "-o") { Description = "Pairing file to create." };
        var rotate = new Command("rotate", "Replace the active token and create a pairing file.") { output };
        rotate.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.RotateTokenAsync(parseResult.GetValue(output), writer)));
        token.Subcommands.Add(rotate);
        return token;
    }

    private static Command CreateToolHostStatusCommand()
    {
        var json = Flag("--json", "Write Host status as JSON.");
        var command = new Command("status", "Show local Host configuration.") { json };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.StatusAsync(parseResult.GetValue(json), writer)));
        return command;
    }

    private static Command CreateToolHostServeCommand()
    {
        var command = new Command("serve", "Run the provider-free Remote Tool Host server.");
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            _ => RemoteToolHostCliRunner.ServeAsync(cancellationToken)));
        return command;
    }

    private static Command CreateToolHostRegisterCommand()
    {
        var file = RequiredArgument("pairing-file", "Pairing file exported by a Remote Tool Host.");
        var command = new Command("register", "Register a paired Host on this Agent machine.") { file };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.RegisterAsync(parseResult.GetRequiredValue(file), writer)));
        return command;
    }

    private static Command CreateToolHostUnregisterCommand()
    {
        var hostId = RequiredArgument("host-id", "Registered Host id.");
        var command = new Command("unregister", "Remove a registered Host and its stored credential.") { hostId };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.UnregisterAsync(parseResult.GetRequiredValue(hostId), writer)));
        return command;
    }

    private static Command CreateToolHostListCommand()
    {
        var json = Flag("--json", "Write registered Hosts and workspaces as JSON.");
        var command = new Command("list", "List registered Hosts and their online state.") { json };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.ListAsync(parseResult.GetValue(json), writer, cancellationToken)));
        return command;
    }

    private static Command CreateToolHostTestCommand()
    {
        var hostId = RequiredArgument("host-id", "Registered Host id.");
        var command = new Command("test", "Test one registered Host connection.") { hostId };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.TestAsync(parseResult.GetRequiredValue(hostId), writer, cancellationToken)));
        return command;
    }

    private static Argument<string> RequiredArgument(string name, string description) => new(name)
    {
        Description = description,
        Arity = ArgumentArity.ExactlyOne
    };

    private static async Task<int> RunRemoteAsync(
        ParseResult parseResult,
        CancellationToken cancellationToken,
        Func<TextWriter, Task<int>> action)
    {
        try
        {
            return await action(parseResult.InvocationConfiguration.Output).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await parseResult.InvocationConfiguration.Error.WriteLineAsync("Remote Tool Host operation cancelled.")
                .ConfigureAwait(false);
            return 130;
        }
        catch (Exception ex)
        {
            await parseResult.InvocationConfiguration.Error.WriteLineAsync($"Error: {ex.Message}")
                .ConfigureAwait(false);
            return 1;
        }
    }
}
