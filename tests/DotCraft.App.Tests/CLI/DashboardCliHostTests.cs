using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class DashboardCliHostTests
{
    [Fact]
    public async Task MissingWorkspace_ReturnsFailure()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "DashboardCliHostMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var originalError = Console.Error;
        using var error = new StringWriter();

        try
        {
            Console.SetError(error);
            var args = new CommandLineArgs
            {
                Mode = CommandLineArgs.RunMode.Dashboard,
                DashboardWorkspacePath = workspace
            };

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
