using System.Diagnostics;
using DotCraft.Processes;
using Xunit;

namespace DotCraft.Tests.Processes;

public sealed class ManagedChildProcessTests
{
    [Fact]
    public async Task Start_BindsProcessToJobObjectOnlyOnWindows()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "pause" } }
            : new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "read line" } };
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;

        await using var child = ManagedChildProcess.Start(start);

        Assert.Equal(OperatingSystem.IsWindows(), child.HasJobObject);
    }
}
