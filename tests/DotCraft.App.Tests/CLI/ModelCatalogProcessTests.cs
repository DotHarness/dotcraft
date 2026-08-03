using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class ModelCatalogProcessTests
{
    [Fact]
    public async Task ModelCatalog_WritesMachineResultOnlyToStandardOutput()
    {
        var dotcraftDll = Path.Combine(AppContext.BaseDirectory, "dotcraft.dll");
        var providerId = $"missing-{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(dotcraftDll);
        startInfo.ArgumentList.Add("model-catalog");
        startInfo.ArgumentList.Add("--provider-id");
        startInfo.ArgumentList.Add(providerId);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start DotCraft.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(0, process.ExitCode);
        var line = Assert.Single(stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        using var result = JsonDocument.Parse(line);
        Assert.Equal("error", result.RootElement.GetProperty("kind").GetString());
        Assert.Contains(providerId, stderr);
        Assert.DoesNotContain("{\"kind\"", stderr, StringComparison.Ordinal);
    }
}
