using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ShellToolsStdinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shell_tools_stdin_{Guid.NewGuid():N}");

    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"shell_tools_out_{Guid.NewGuid():N}");

    public ShellToolsStdinTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    [Fact]
    public async Task WriteStdin_RejectedInput_IsNeverWrittenToTheProcess()
    {
        var terminals = new StdinTerminalService(_root);
        var tools = Tools(terminals, approve: false);

        var result = await tools.WriteStdin("term_1", $"cat {Path.Combine(_outside, "secret.txt")}\n");

        Assert.Contains("rejected", result);
        Assert.Empty(terminals.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u0003")]
    [InlineData("\u0003\n")]
    [InlineData("y\n")]
    public async Task WriteStdin_InputThatRaisesNothing_ReachesTheProcessWithoutAPrompt(string input)
    {
        var terminals = new StdinTerminalService(_root);
        var tools = Tools(terminals, approve: false);

        await tools.WriteStdin("term_1", input);

        Assert.Equal(input, Assert.Single(terminals.Writes));
    }

    public void Dispose()
    {
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

    private ShellTools Tools(IBackgroundTerminalService terminals, bool approve) =>
        new(_root, terminals, approvalService: new FixedApprovalService(approve));

    private sealed class StdinTerminalService(string workingDirectory) : StubBackgroundTerminalService
    {
        private readonly ShellStdinSession _session = new(HostShell(), workingDirectory);

        public List<string> Writes { get; } = [];

        public override ShellStdinSession? GetStdinSession(string sessionId) => _session;

        public override Task<BackgroundTerminalSnapshot> WriteStdinAsync(
            string sessionId,
            string input,
            int yieldTimeMs = 1000,
            int? maxOutputChars = null,
            CancellationToken ct = default)
        {
            Writes.Add(input);
            return Task.FromResult(new BackgroundTerminalSnapshot { SessionId = sessionId });
        }

        private static ShellIdentity HostShell()
        {
            Assert.True(ShellIdentityResolver.Host.TryResolve(null, out var shell, out var reason), reason);
            return shell!;
        }
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
