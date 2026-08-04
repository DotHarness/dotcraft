using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class CliStartupTests
{
    [Fact]
    public void DecideWorkspaceStartup_NoArgsWithoutWorkspace_InitializesInteractively()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.None,
            workspaceExists: false);

        Assert.Equal(WorkspaceStartupDecision.InitializeInteractively, decision);
    }

    [Fact]
    public void DecideWorkspaceStartup_NoArgsWithWorkspace_ShowsUsage()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.None,
            workspaceExists: true);

        Assert.Equal(WorkspaceStartupDecision.ShowUsage, decision);
    }

    [Fact]
    public void DecideWorkspaceStartup_ExecWithoutWorkspace_FailsAsHeadless()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.Exec,
            workspaceExists: false);

        Assert.Equal(WorkspaceStartupDecision.MissingWorkspace, decision);
    }

    [Fact]
    public void DecideWorkspaceStartup_DashboardWithoutWorkspace_FailsAsHeadless()
    {
        var decision = CliStartup.DecideWorkspaceStartup(
            CommandLineArgs.RunMode.Dashboard,
            workspaceExists: false);

        Assert.Equal(WorkspaceStartupDecision.MissingWorkspace, decision);
    }

    [Fact]
    public async Task DashboardCliHost_MissingWorkspace_ReturnsFailure()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "DashboardCliHostMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var originalError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Console.SetError(error);
            var args = CommandLineArgs.Parse(["dashboard", "--workspace", workspace]);

            var exitCode = await DashboardCliHost.RunAsync(args);

            Assert.Equal(1, exitCode);
            Assert.Contains(Path.Combine(workspace, ".craft"), error.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            Directory.Delete(workspace, recursive: true);
        }
    }
}
