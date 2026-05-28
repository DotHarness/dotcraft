using System.Diagnostics;
using DotCraft.Processes;

namespace DotCraft.Tests.Processes;

public sealed class ManagedChildProcessTests
{
    [Fact]
    public async Task Start_OnWindows_BindsProcessToJobObject()
    {
        if (!OperatingSystem.IsWindows())
            return;

        await using var child = ManagedChildProcess.Start(CreateLongRunningStartInfo());
        Assert.True(child.HasJobObject);
    }

    [Fact]
    public async Task Start_OnNonWindows_DoesNotCreateJobObject()
    {
        if (OperatingSystem.IsWindows())
            return;

        await using var child = ManagedChildProcess.Start(CreateLongRunningStartInfo());
        Assert.False(child.HasJobObject);
    }

    private static ProcessStartInfo CreateLongRunningStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = "powershell",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList =
                {
                    "-NoProfile",
                    "-Command",
                    "Start-Sleep -Seconds 30"
                }
            };
        }

        return new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-c",
                "sleep 30"
            }
        };
    }
}
