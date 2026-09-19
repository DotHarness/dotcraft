using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ShellToolsStdinProcessTests : IAsyncLifetime
{
    private const string Marker = "dotcraft-stdin-marker";

    private readonly string _root = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "DotCraftStdinGate_" + Guid.NewGuid().ToString("N"));

    private readonly string _outside = Path.Combine(
        Directory.GetCurrentDirectory(),
        "TestArtifacts",
        "DotCraftStdinOutside_" + Guid.NewGuid().ToString("N"));

    private BackgroundTerminalService? _terminals;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
        // Output is captured per line, so a marker without a terminator never leaves the reader.
        File.WriteAllText(Path.Combine(_outside, "secret.txt"), Marker + Environment.NewLine);
        _terminals = new BackgroundTerminalService(
            _root,
            new AppConfig.ShellBackgroundConfig { DefaultYieldTimeMs = 200, MaxYieldTimeMs = 10000 });
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WriteStdin_OutsideWorkspaceReadInAnInteractiveShell_RunsOnlyWhenApproved(bool approve)
    {
        var terminals = _terminals!;
        var tools = new ShellTools(_root, terminals, approvalService: new FixedApprovalService(approve));

        // A bare interpreter is an ordinary in-workspace command, so it starts without a prompt.
        await tools.Exec(InteractiveShellCommand(), runInBackground: true, yieldTimeMs: 3000, interactive: true);
        var session = Assert.Single(await terminals.ListAsync());

        var result = await tools.WriteStdin(session.SessionId, ReadOutsideCommand(), yieldTimeMs: 3000);

        if (approve)
        {
            Assert.Contains(Marker, await PollAsync(tools, session.SessionId));
        }
        else
        {
            Assert.Contains("rejected", result);
            Assert.DoesNotContain(Marker, await PollAsync(tools, session.SessionId));
        }
    }

    public async Task DisposeAsync()
    {
        if (_terminals is not null)
            await _terminals.DisposeAsync();
        foreach (var directory in new[] { _root, _outside })
        {
            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static string InteractiveShellCommand() =>
        OperatingSystem.IsWindows() ? "powershell -NoLogo -NoProfile" : "echo ready";

    private string ReadOutsideCommand()
    {
        var path = Path.Combine(_outside, "secret.txt");
        return OperatingSystem.IsWindows() ? $"Get-Content {path}\n" : $"cat {path}\n";
    }

    private static async Task<string> PollAsync(ShellTools tools, string sessionId)
    {
        var output = string.Empty;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            output = await tools.WriteStdin(sessionId, string.Empty, yieldTimeMs: 1000);
            if (output.Contains(Marker, StringComparison.Ordinal))
                break;
        }

        return output;
    }

    private sealed class FixedApprovalService(bool approve) : IApprovalService
    {
        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null) =>
            Task.FromResult(approve);

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            Task.FromResult(approve);

        public Task<bool> RequestResourceApprovalAsync(
            string kind,
            string operation,
            string target,
            ApprovalContext? context = null) => Task.FromResult(approve);
    }
}
