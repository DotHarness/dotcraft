using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.RemoteTools;
using DotCraft.Tests.Runtime.Plugins;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class RemoteExecutionLspTests
{
    [Fact]
    public async Task ClosingSessionDuringLanguageServerStartupStopsOnlyItsProcess()
    {
        await using var fixture = await RemoteExecutionFixture.CreateAsync(enableLsp: true);
        var log = Path.Combine(fixture.Workspace.Path, "servers.log");
        var command = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash";
        var arguments = OperatingSystem.IsWindows()
            ? new[] { "-NoProfile", "-NonInteractive", "-Command", $"[IO.File]::AppendAllText('{log.Replace("'", "''")}', \"$PID`n\"); Start-Sleep -Seconds 120" }
            : new[] { "-c", $"echo $$ >> '{log}'; sleep 120" };
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = true;
        config.LspServers = [new LspServerConfig
        {
            Name = "probe", Command = command, Arguments = [.. arguments],
            ExtensionToLanguage = new() { [".cs"] = "csharp" }, StartupTimeoutMs = 60_000
        }];
        RemoteToolHostTestHost.WriteConfig(fixture.Storage.GlobalConfigPath, config);
        await File.WriteAllTextAsync(Path.Combine(fixture.Workspace.Path, "file.cs"), "class Example {}");
        await using var first = await fixture.OpenAsync("first");
        await using var second = await fixture.OpenAsync("second");
        var a = fixture.InvokeAsync(first, "LSP", new() { ["operation"] = "hover", ["filePath"] = "file.cs", ["line"] = 1, ["character"] = 1 }).AsTask();
        await fixture.WaitAsync(() => a.IsCompleted || PluginLogFile.ReadLines(log).Length == 1, TimeSpan.FromSeconds(10));
        Assert.False(a.IsCompleted, a.IsCompleted ? System.Text.Json.JsonSerializer.Serialize(await a) : null);
        var b = fixture.InvokeAsync(second, "LSP", new() { ["operation"] = "hover", ["filePath"] = "file.cs", ["line"] = 1, ["character"] = 1 }).AsTask();
        await fixture.WaitAsync(() => b.IsCompleted || PluginLogFile.ReadLines(log).Length == 2, TimeSpan.FromSeconds(10));
        Assert.False(b.IsCompleted, b.IsCompleted ? System.Text.Json.JsonSerializer.Serialize(await b) : null);
        var pids = PluginLogFile.ReadLines(log).Select(int.Parse).ToArray();
        using var firstProcess = System.Diagnostics.Process.GetProcessById(pids[0]);
        using var secondProcess = System.Diagnostics.Process.GetProcessById(pids[1]);

        await first.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False((await a).Success); } catch (OperationCanceledException) { }
        Assert.True(firstProcess.HasExited);
        Assert.False(secondProcess.HasExited);
        Assert.False(b.IsCompleted);
        Assert.True((await fixture.InvokeAsync(second, "ReadFile", new() { ["path"] = "file.cs" })).Success);
        await second.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.False((await b).Success); } catch (OperationCanceledException) { }
        Assert.True(secondProcess.HasExited);
    }
}
