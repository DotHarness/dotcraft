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
        toolHost.Subcommands.Add(CreateToolHostStatusCommand());
        toolHost.Subcommands.Add(CreateToolHostServeCommand());
        toolHost.Subcommands.Add(CreateToolHostInviteCommand());
        toolHost.Subcommands.Add(CreateToolHostJoinCommand());
        toolHost.Subcommands.Add(CreateToolHostRevokeCommand());
        toolHost.Subcommands.Add(CreateToolHostListCommand());
        toolHost.Subcommands.Add(CreateToolHostTestCommand());
        return toolHost;
    }

    private static Command CreateToolHostSetupCommand()
    {
        var name = new Option<string?>("--name") { Description = "Name shown to paired machines." };
        var command = new Command("setup", "Create this machine's Remote Tool Host identity.") { name };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.SetupAsync(parseResult.GetValue(name), writer)));
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

    private static Command CreateToolHostStatusCommand()
    {
        var json = Flag("--json", "Write Host status as JSON.");
        var command = new Command("status", "Show local Host configuration and pairings.") { json };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.StatusAsync(parseResult.GetValue(json), writer)));
        return command;
    }

    private static Command CreateToolHostServeCommand()
    {
        var command = new Command("serve", "Connect this machine to its paired Hubs and stay available.");
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.ServeAsync(writer, cancellationToken)));
        return command;
    }

    private static Command CreateToolHostInviteCommand()
    {
        var name = new Option<string?>("--name") { Description = "Label shown to the invited machine." };
        var host = new Option<string?>("--host") { Description = "Address the invited machine should dial." };
        var expires = new Option<int?>("--expires") { Description = "Invitation validity in hours." };
        var json = Flag("--json", "Write the invitation as JSON.");
        var command = new Command("invite", "Create an invitation for another machine.")
        {
            name,
            host,
            expires,
            json
        };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.InviteAsync(
                parseResult.GetValue(name),
                parseResult.GetValue(host),
                parseResult.GetValue(expires),
                parseResult.GetValue(json),
                writer,
                cancellationToken)));
        return command;
    }

    private static Command CreateToolHostJoinCommand()
    {
        var url = RequiredArgument("invite-url", "Invitation link received from the other machine.");
        var workspace = new Option<string?>("--workspace")
        {
            Description = "Absolute folder to share when the invitation proposes none."
        };
        var command = new Command("join", "Accept an invitation and pair this machine.") { url, workspace };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.JoinAsync(
                parseResult.GetRequiredValue(url),
                parseResult.GetValue(workspace),
                writer,
                cancellationToken)));
        return command;
    }

    private static Command CreateToolHostRevokeCommand()
    {
        var id = RequiredArgument("id", "Paired machine id.");
        var command = new Command("revoke", "End a pairing on this machine or on its Hub.") { id };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.RevokeAsync(
                parseResult.GetRequiredValue(id),
                writer,
                cancellationToken)));
        return command;
    }

    private static Command CreateToolHostListCommand()
    {
        var json = Flag("--json", "Write paired machines and workspaces as JSON.");
        var command = new Command("list", "List machines paired with this Hub.") { json };
        command.SetAction((parseResult, cancellationToken) => RunRemoteAsync(
            parseResult,
            cancellationToken,
            writer => RemoteToolHostCliRunner.ListAsync(parseResult.GetValue(json), writer, cancellationToken)));
        return command;
    }

    private static Command CreateToolHostTestCommand()
    {
        var hostId = RequiredArgument("id", "Paired machine id.");
        var command = new Command("test", "Check whether one paired machine is online.") { hostId };
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
