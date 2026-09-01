using System.CommandLine;
using System.CommandLine.Invocation;

namespace DotCraft.CLI;

/// <summary>Defines and invokes the DotCraft command-line surface.</summary>
public static partial class DotCraftCommandLine
{
    /// <summary>Parses and executes a DotCraft command.</summary>
    public static Task<int> RunAsync(
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
        return root.Parse(effectiveArgs).InvokeAsync(invocation, cancellationToken);
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
        root.Subcommands.Add(CreateStackCommand());
        root.Subcommands.Add(CreateToolHostCommand());
        root.Subcommands.Add(CreateModelCatalogCommand());
        root.Subcommands.Add(CreateWorkflowWorkerCommand());
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
