using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

namespace DotCraft.CLI;

/// <summary>Defines and invokes the DotCraft command-line surface.</summary>
public static partial class DotCraftCommandLine
{
    private const int UsageErrorExitCode = 2;

    /// <summary>Parses and executes a DotCraft command.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter? output = null,
        TextWriter? error = null,
        CancellationToken cancellationToken = default)
    {
        var root = CreateRootCommand();
        var invocation = new InvocationConfiguration
        {
            Output = output ?? Console.Out,
            Error = error ?? Console.Error
        };
        var effectiveArgs = args.Length == 0 ? ["--help"] : args;
        var parseResult = root.Parse(effectiveArgs);
        if (IsExecCommand(parseResult.CommandResult))
            invocation.ProcessTerminationTimeout = null;
        var exitCode = await parseResult.InvokeAsync(invocation, cancellationToken);
        return parseResult.Errors.Count > 0 ? UsageErrorExitCode : exitCode;
    }

    // Exec handles Ctrl+C itself: it interrupts the running Turn and waits for it to stop.
    private static bool IsExecCommand(CommandResult? result)
    {
        for (; result != null; result = result.Parent as CommandResult)
        {
            if (result.Command.Name == "exec" && result.Parent is CommandResult { Command: RootCommand })
                return true;
        }

        return false;
    }

    internal static RootCommand CreateRootCommand()
    {
        var root = new RootCommand("Run DotCraft agents, services, and workspace tools.");
        root.Subcommands.Add(CreateSetupCommand());
        root.Subcommands.Add(CreateExecCommand());
        root.Subcommands.Add(CreateAppServerCommand());
        root.Subcommands.Add(CreateAcpCommand());
        root.Subcommands.Add(CreateHubCommand());
        root.Subcommands.Add(CreateDashboardCommand());
        root.Subcommands.Add(CreateAuthCommand());
        root.Subcommands.Add(CreateSkillCommand());
        root.Subcommands.Add(CreateContextCommand());
        root.Subcommands.Add(CreateConfigCommand());
        root.Subcommands.Add(CreateStackCommand());
        root.Subcommands.Add(CreateToolHostCommand());
        root.Subcommands.Add(CreateModelCatalogCommand());
        root.Subcommands.Add(CreateModelServiceCommand());
        root.Subcommands.Add(CreateOratorioCommand());
        root.Subcommands.Add(CreateRelayCommand());
        root.Subcommands.Add(CreateScriptWorkerCommand());
        return root;
    }

    private static Task<int> RunApplicationAsync(CommandLineArgs args, CancellationToken cancellationToken) =>
        DotCraftApplication.RunAsync(args, cancellationToken);

    private static Option<string?> StringOption(string name, string description) => new(name)
    {
        Description = description
    };

    private static Option<bool> Flag(string name, string description) => new(name)
    {
        Description = description
    };
}
