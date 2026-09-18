using DotCraft.Hooks;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tests.Security.ShellCommands;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class HookApprovalServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "HookApproval_" + Guid.NewGuid().ToString("N")[..8]);

    public HookApprovalServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task PermissionRequestHookBlocksBeforeInnerApproval()
    {
        var inner = new RecordingApprovalService();
        var runner = new HookRunner(new HooksFileConfig
        {
            Hooks =
            {
                [nameof(HookEvent.PermissionRequest)] =
                [
                    new HookMatcherGroup
                    {
                        Hooks =
                        [
                            new HookEntry
                            {
                                Type = "command",
                                Command = StderrAndExitCommand("permission denied", 2)
                            }
                        ]
                    }
                ]
            }
        }, _tempDir);
        var service = new HookApprovalService(
            inner,
            runner,
            "thread_1",
            "turn_1",
            _tempDir,
            stopHookActive: false);

        var approved = await service.RequestShellApprovalAsync(ShellApprovalRequests.For("dotnet test", _tempDir));

        Assert.False(approved);
        Assert.Empty(inner.Requests);
    }

    private static string StderrAndExitCommand(string output, int exitCode) =>
        OperatingSystem.IsWindows()
            ? $"[Console]::Error.WriteLine('{output}'); exit {exitCode}"
            : $"printf '%s\\n' '{output}' >&2; exit {exitCode}";

    private sealed class RecordingApprovalService : IApprovalService
    {
        public List<(string Kind, string Operation, string Target)> Requests { get; } = [];

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            Requests.Add(("file", operation, path));
            return Task.FromResult(true);
        }

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
        {
            Requests.Add(("shell", request.Command, request.WorkingDirectory));
            return Task.FromResult(true);
        }

        public Task<bool> RequestResourceApprovalAsync(
            string kind,
            string operation,
            string target,
            ApprovalContext? context = null)
        {
            Requests.Add((kind, operation, target));
            return Task.FromResult(true);
        }
    }
}
