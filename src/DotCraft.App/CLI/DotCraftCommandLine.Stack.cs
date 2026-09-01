using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateStackCommand()
    {
        var stack = new Command("stack", "Manage a self-hosted DotCraft Stack deployment.");
        stack.Subcommands.Add(CreateStackInitCommand());
        stack.Subcommands.Add(CreateStackAddProjectCommand());
        stack.Subcommands.Add(CreateStackDirectoryCommand("doctor", "Check deployment files and Docker connectivity.", StackCliRunner.DoctorAsync));
        stack.Subcommands.Add(CreateStackDirectoryCommand("status", "Show Docker Compose service status.", StackCliRunner.StatusAsync));
        stack.Subcommands.Add(CreateStackLogsCommand());
        stack.Subcommands.Add(CreateStackMutationCommand("restart", "Restart Stack services.", StackCliRunner.RestartAsync));
        stack.Subcommands.Add(CreateStackMutationCommand("upgrade", "Pull and recreate Stack services.", StackCliRunner.UpgradeAsync));
        stack.Subcommands.Add(CreateStackWebhookCommand());
        return stack;
    }

    private static Command CreateStackInitCommand()
    {
        var directory = StackDirectoryOption();
        var dryRun = StackDryRunOption();
        var noStart = Flag("--no-start", "Create deployment files without starting services.");
        var version = StringOption("--version", "DotCraft container version.");
        var provider = StringOption("--provider", "Default model provider.");
        var model = StringOption("--model", "Default model id.");
        var apiKey = StringOption("--api-key", "Default provider API key.");
        var command = new Command("init", "Create a new DotCraft Stack deployment.")
        {
            directory,
            dryRun,
            noStart,
            version,
            provider,
            model,
            apiKey
        };
        command.SetAction((parseResult, cancellationToken) => StackCliRunner.InitAsync(
            StackOptions(parseResult, directory, dryRun) with
            {
                NoStart = parseResult.GetValue(noStart),
                Version = parseResult.GetValue(version),
                Provider = parseResult.GetValue(provider),
                Model = parseResult.GetValue(model),
                ApiKey = parseResult.GetValue(apiKey)
            },
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }

    private static Command CreateStackAddProjectCommand()
    {
        var directory = StackDirectoryOption();
        var dryRun = StackDryRunOption();
        var provider = StringOption("--provider", "Source provider: github or gitlab.");
        provider.Required = true;
        provider.AcceptOnlyFromAmong("github", "gitlab");
        var project = StringOption("--project", "Repository or project identifier.");
        project.Required = true;
        var workspace = StringOption("--workspace", "Absolute workspace path inside the deployment.");
        workspace.Required = true;
        var command = new Command("add-project", "Bind a source repository to a Stack workspace.")
        {
            directory,
            dryRun,
            provider,
            project,
            workspace
        };
        command.SetAction(parseResult => StackCliRunner.AddProjectAsync(
            StackOptions(parseResult, directory, dryRun) with
            {
                Provider = parseResult.GetValue(provider),
                Project = parseResult.GetValue(project),
                Workspace = parseResult.GetValue(workspace)
            },
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error));
        return command;
    }

    private static Command CreateStackDirectoryCommand(
        string name,
        string description,
        Func<StackCommandOptions, TextWriter, TextWriter, CancellationToken, IStackProcessRunner?, Task<int>> action)
    {
        var directory = StackDirectoryOption();
        var command = new Command(name, description) { directory };
        command.SetAction((parseResult, cancellationToken) => action(
            StackOptions(parseResult, directory),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken,
            null));
        return command;
    }

    private static Command CreateStackLogsCommand()
    {
        var directory = StackDirectoryOption();
        var tail = StringOption("--tail", "Number of recent log lines.");
        var service = StringOption("--service", "Limit output to one service.");
        var command = new Command("logs", "Show Stack service logs.") { directory, tail, service };
        command.SetAction((parseResult, cancellationToken) => StackCliRunner.LogsAsync(
            StackOptions(parseResult, directory) with
            {
                Tail = parseResult.GetValue(tail),
                Service = parseResult.GetValue(service)
            },
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }

    private static Command CreateStackMutationCommand(
        string name,
        string description,
        Func<StackCommandOptions, TextWriter, TextWriter, CancellationToken, IStackProcessRunner?, Task<int>> action)
    {
        var directory = StackDirectoryOption();
        var dryRun = StackDryRunOption();
        var command = new Command(name, description) { directory, dryRun };
        command.SetAction((parseResult, cancellationToken) => action(
            StackOptions(parseResult, directory, dryRun),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken,
            null));
        return command;
    }

    private static Command CreateStackWebhookCommand()
    {
        var webhook = new Command("webhook", "Manage public webhook ingress.");

        var enableDirectory = StackDirectoryOption();
        var enableDryRun = StackDryRunOption();
        var publicHost = StringOption("--public-host", "Public DNS name for the webhook endpoint.");
        publicHost.Required = true;
        var enable = new Command("enable", "Enable GitHub webhook ingress.")
        {
            enableDirectory,
            enableDryRun,
            publicHost
        };
        enable.SetAction((parseResult, cancellationToken) => StackCliRunner.EnableWebhookAsync(
            StackOptions(parseResult, enableDirectory, enableDryRun) with
            {
                PublicHost = parseResult.GetValue(publicHost)
            },
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));

        var statusDirectory = StackDirectoryOption();
        var status = new Command("status", "Show webhook ingress status.") { statusDirectory };
        status.SetAction((parseResult, cancellationToken) => StackCliRunner.WebhookStatusAsync(
            StackOptions(parseResult, statusDirectory),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));

        var disableDirectory = StackDirectoryOption();
        var disableDryRun = StackDryRunOption();
        var disable = new Command("disable", "Disable webhook ingress without deleting Stack state.")
        {
            disableDirectory,
            disableDryRun
        };
        disable.SetAction((parseResult, cancellationToken) => StackCliRunner.DisableWebhookAsync(
            StackOptions(parseResult, disableDirectory, disableDryRun),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));

        webhook.Subcommands.Add(enable);
        webhook.Subcommands.Add(status);
        webhook.Subcommands.Add(disable);
        return webhook;
    }

    private static Option<string?> StackDirectoryOption() =>
        StringOption("--dir", "Stack deployment directory.");

    private static Option<bool> StackDryRunOption() =>
        Flag("--dry-run", "Describe changes without applying them.");

    private static StackCommandOptions StackOptions(
        ParseResult parseResult,
        Option<string?> directory,
        Option<bool>? dryRun = null) => new()
        {
            Directory = Path.GetFullPath(parseResult.GetValue(directory) ?? "."),
            DryRun = dryRun is not null && parseResult.GetValue(dryRun)
        };
}
