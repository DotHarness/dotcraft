using System.Diagnostics;

namespace DotCraft.Tests.CLI;

public sealed class SetupProcessTests
{
    [Theory]
    [InlineData("medium")]
    [InlineData("extraHigh")]
    public async Task Setup_AcceptsModelPreferenceWireValues(string reasoningEffort)
    {
        var workspacePath = CreateTemporaryWorkspace();
        try
        {
            var preferenceJson = $$"""
                {
                  "model": "gpt-5.6-sol",
                  "reasoning": {
                    "enabled": true,
                    "effort": "{{reasoningEffort}}",
                    "output": "summary"
                  },
                  "speed": "fast",
                  "contextWindow": {
                    "mode": "max"
                  }
                }
                """;

            var result = await RunSetupAsync(workspacePath, preferenceJson);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(Path.Combine(workspacePath, ".craft", "config.json")));
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    [Fact]
    public async Task Setup_RejectsUnknownModelPreferenceWireValueWithoutCreatingWorkspace()
    {
        var workspacePath = CreateTemporaryWorkspace();
        try
        {
            const string preferenceJson = """
                {
                  "model": "gpt-5.6-sol",
                  "reasoning": {
                    "enabled": true,
                    "effort": "unlimited",
                    "output": "summary"
                  },
                  "speed": "fast",
                  "contextWindow": {
                    "mode": "max"
                  }
                }
                """;

            var result = await RunSetupAsync(workspacePath, preferenceJson);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Unknown reasoning effort value", result.StandardError, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(workspacePath, ".craft")));
        }
        finally
        {
            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private static string CreateTemporaryWorkspace()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-setup-process-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<ProcessResult> RunSetupAsync(string workspacePath, string preferenceJson)
    {
        var dotcraftDll = Path.Combine(AppContext.BaseDirectory, "dotcraft.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(dotcraftDll);
        startInfo.ArgumentList.Add("setup");
        startInfo.ArgumentList.Add("--skip-provider");
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add("default");
        startInfo.ArgumentList.Add("--preference-json");
        startInfo.ArgumentList.Add(preferenceJson);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start DotCraft.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
