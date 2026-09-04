using System.CommandLine;

namespace DotCraft.CLI;

public static partial class DotCraftCommandLine
{
    private static Command CreateConfigCommand()
    {
        var config = new Command("config", "Inspect DotCraft configuration.");
        config.Subcommands.Add(CreateConfigSchemaCommand());
        config.Subcommands.Add(CreateConfigShowCommand());
        return config;
    }

    private static Command CreateConfigSchemaCommand()
    {
        var section = StringOption("--section", "Show only this section, by display name or JSON path.");
        var json = Flag("--json", "Write the result as JSON.");
        var command = new Command("schema", "Show the configuration sections and fields this build understands.")
        {
            section,
            json
        };
        command.SetAction((parseResult, cancellationToken) => ConfigCliRunner.SchemaAsync(
            new ConfigSchemaCommandOptions(
                parseResult.GetValue(section),
                parseResult.GetValue(json)),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }

    private static Command CreateConfigShowCommand()
    {
        var workspace = StringOption("--workspace", "Workspace directory to read. Defaults to the current directory.");
        var json = Flag("--json", "Write the result as JSON. The default output is already indented JSON.");
        var command = new Command("show", "Show the merged configuration with sensitive values masked.")
        {
            workspace,
            json
        };
        command.SetAction((parseResult, cancellationToken) => ConfigCliRunner.ShowAsync(
            new ConfigShowCommandOptions(
                parseResult.GetValue(workspace),
                parseResult.GetValue(json)),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            cancellationToken));
        return command;
    }
}
