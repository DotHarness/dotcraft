using System.CommandLine;
using DotCraft.Hub;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateHubCommand()
    {
        var hub = new Command("hub", "Run the workspace-independent local Hub.");
        hub.SetAction((_, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.Hub
        }, cancellationToken));

        var nodeBin = StringOption("--node-bin", "Node.js executable path.");
        var modulesDir = StringOption("--modules-dir", "Node.js modules directory.");
        var ripgrepPath = StringOption("--ripgrep-path", "ripgrep executable path.");
        var registryUrl = StringOption("--default-plugin-registry-url", "Default plugin registry URL.");
        var runAsNode = Flag("--node-run-as-node", "Set ELECTRON_RUN_AS_NODE for the configured runtime.");
        var setRuntime = new Command("set-runtime", "Configure runtime tools used by the Hub.")
        {
            nodeBin,
            modulesDir,
            ripgrepPath,
            registryUrl,
            runAsNode
        };
        setRuntime.Hidden = true;
        setRuntime.SetAction(async parseResult =>
        {
            var paths = HubPaths.ForCurrentUser();
            var store = new HubRuntimeToolsStore(paths.RuntimeToolsPath);
            var merged = store.MergeAndSave(new HubRuntimeToolsRequest
            {
                NodeBin = parseResult.GetValue(nodeBin),
                ModulesDir = parseResult.GetValue(modulesDir),
                RipgrepPath = parseResult.GetValue(ripgrepPath),
                DefaultPluginRegistryUrl = parseResult.GetValue(registryUrl),
                NodeRunAsNode = parseResult.GetValue(runAsNode)
            });
            var output = parseResult.InvocationConfiguration.Output;
            await output.WriteLineAsync($"Saved Hub runtime tools to {paths.RuntimeToolsPath}");
            if (!string.IsNullOrWhiteSpace(merged.NodeBin))
                await output.WriteLineAsync($"Node: {merged.NodeBin}");
            if (!string.IsNullOrWhiteSpace(merged.ModulesDir))
                await output.WriteLineAsync($"Modules: {merged.ModulesDir}");
            return 0;
        });
        hub.Subcommands.Add(setRuntime);
        return hub;
    }

    private static Command CreateAuthCommand()
    {
        var auth = new Command("auth", "Manage provider authentication.");
        var openAi = new Command("openai", "Manage OpenAI authentication.");
        openAi.Subcommands.Add(CreateOpenAiLoginCommand());
        openAi.Subcommands.Add(CreateOpenAiLogoutCommand());
        openAi.Subcommands.Add(CreateOpenAiStatusCommand());
        auth.Subcommands.Add(openAi);
        return auth;
    }

    private static Command CreateOpenAiLoginCommand()
    {
        var providerId = StringOption("--provider-id", "Provider id to bind after login.");
        var noBrowser = Flag("--no-browser", "Print the authorization URL without opening a browser.");
        var noBind = Flag("--no-bind", "Do not bind the provider configuration after login.");
        var command = new Command("login", "Sign in with OpenAI.") { providerId, noBrowser, noBind };
        command.SetAction((parseResult, cancellationToken) => AuthCliRunner.LoginAsync(
            parseResult.GetValue(providerId),
            parseResult.GetValue(noBrowser),
            parseResult.GetValue(noBind),
            cancellationToken));
        return command;
    }

    private static Command CreateOpenAiLogoutCommand()
    {
        var providerId = StringOption("--provider-id", "Provider id to unbind after logout.");
        var command = new Command("logout", "Remove stored OpenAI credentials.") { providerId };
        command.SetAction((parseResult, cancellationToken) => AuthCliRunner.LogoutAsync(
            parseResult.GetValue(providerId),
            cancellationToken));
        return command;
    }

    private static Command CreateOpenAiStatusCommand()
    {
        var noUsage = Flag("--no-usage", "Skip the usage and rate-limit lookup.");
        var command = new Command("status", "Show OpenAI authentication status.") { noUsage };
        command.SetAction((parseResult, cancellationToken) => AuthCliRunner.StatusAsync(
            parseResult.GetValue(noUsage),
            cancellationToken));
        return command;
    }

    private static Command CreateSkillCommand()
    {
        var skill = new Command("skill", "Verify and install DotCraft skills.");
        skill.Subcommands.Add(CreateSkillLeaf("verify", "Verify a skill candidate without installing it."));
        skill.Subcommands.Add(CreateSkillLeaf("install", "Install a verified skill candidate."));
        return skill;
    }

    private static Command CreateSkillLeaf(string name, string description)
    {
        var candidate = StringOption("--candidate", "Candidate skill directory.");
        candidate.Required = true;
        var skillName = StringOption("--name", "Override the installed skill name.");
        var source = StringOption("--source", "Record the source of the installed skill.");
        var overwrite = Flag("--overwrite", "Replace an existing skill with the same name.");
        var json = Flag("--json", "Write the result as JSON.");
        var command = new Command(name, description) { candidate, skillName, json };
        if (name == "install")
        {
            command.Options.Add(source);
            command.Options.Add(overwrite);
        }
        command.SetAction((parseResult, cancellationToken) =>
        {
            var craftPath = Path.GetFullPath(".craft");
            var writer = parseResult.InvocationConfiguration.Output;
            var error = parseResult.InvocationConfiguration.Error;
            var candidatePath = parseResult.GetRequiredValue(candidate)!;
            return name == "install"
                ? SkillCliRunner.InstallAsync(
                    craftPath,
                    candidatePath,
                    parseResult.GetValue(skillName),
                    parseResult.GetValue(source),
                    parseResult.GetValue(overwrite),
                    parseResult.GetValue(json),
                    writer,
                    error,
                    cancellationToken)
                : SkillCliRunner.VerifyAsync(
                    craftPath,
                    candidatePath,
                    parseResult.GetValue(skillName),
                    parseResult.GetValue(json),
                    writer,
                    error,
                    cancellationToken);
        });
        return command;
    }

    private static Command CreateContextCommand()
    {
        var context = new Command("context", "Search and export saved DotCraft context.");
        context.Subcommands.Add(CreateContextExportCommand());
        context.Subcommands.Add(CreateContextSearchCommand());
        return context;
    }

    private static Command CreateContextExportCommand()
    {
        var thread = StringOption("--thread", "Thread id to export.");
        thread.Required = true;
        var workspace = StringOption("--workspace", "Workspace or .craft directory to search.");
        var output = StringOption("--output", "Write Markdown to this file instead of stdout.");
        var profile = StringOption("--profile", "Export profile: handoff or transcript.");
        profile.AcceptOnlyFromAmong("handoff", "transcript");
        var toolResults = StringOption("--tool-results", "Tool result detail: none, summary, or full.");
        toolResults.AcceptOnlyFromAmong("none", "summary", "full");
        var history = StringOption("--history", "Memory history detail: none, tail, or full.");
        history.AcceptOnlyFromAmong("none", "tail", "full");
        var command = new Command("export", "Export one thread as Markdown.")
        {
            thread,
            workspace,
            output,
            profile,
            toolResults,
            history
        };
        command.SetAction((parseResult, cancellationToken) => ContextCliRunner.ExportAsync(
            new ContextExportCommandOptions(
                parseResult.GetRequiredValue(thread)!,
                parseResult.GetValue(workspace),
                parseResult.GetValue(output),
                parseResult.GetValue(profile),
                parseResult.GetValue(toolResults),
                parseResult.GetValue(history)),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }

    private static Command CreateContextSearchCommand()
    {
        var query = StringOption("--query", "Text to search for.");
        query.Required = true;
        var workspace = StringOption("--workspace", "Workspace or .craft directory to search.");
        var limit = new Option<int?>("--limit") { Description = "Maximum number of results." };
        limit.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is <= 0)
                result.AddError("--limit must be positive.");
        });
        var status = StringOption("--status", "Thread status: active, archived, or all.");
        status.AcceptOnlyFromAmong("active", "archived", "all");
        var json = Flag("--json", "Write the result as JSON.");
        var command = new Command("search", "Search saved thread context.")
        {
            query,
            workspace,
            limit,
            status,
            json
        };
        command.SetAction((parseResult, cancellationToken) => ContextCliRunner.SearchAsync(
            new ContextSearchCommandOptions(
                parseResult.GetRequiredValue(query)!,
                parseResult.GetValue(workspace),
                parseResult.GetValue(limit),
                parseResult.GetValue(status),
                parseResult.GetValue(json)),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }

    private static Command CreateModelCatalogCommand()
    {
        var providerId = StringOption("--provider-id", "Provider id to query.");
        var stdin = Flag("--stdin", "Read the request from standard input.");
        var command = new Command("model-catalog", "Render the model catalog as JSON.")
        {
            providerId,
            stdin
        };
        command.Hidden = true;
        command.SetAction((parseResult, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.ModelCatalog,
            SetupProviderId = parseResult.GetValue(providerId),
            ModelCatalogReadStdin = parseResult.GetValue(stdin),
            ReservesStdout = true
        }, cancellationToken));
        return command;
    }

    private static Command CreateWorkflowWorkerCommand()
    {
        var command = new Command("workflow-worker", "Run the Dynamic Workflow worker protocol.")
        {
            Hidden = true
        };
        command.SetAction((_, cancellationToken) => RunApplicationAsync(new CommandLineArgs
        {
            Mode = CommandLineArgs.RunMode.WorkflowWorker,
            ReservesStdout = true
        }, cancellationToken));
        return command;
    }
}
