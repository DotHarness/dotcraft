using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellExecutionGateStdinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shell_stdin_{Guid.NewGuid():N}");

    private readonly string _outside = Path.Combine(Path.GetTempPath(), $"shell_stdin_out_{Guid.NewGuid():N}");

    public ShellExecutionGateStdinTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_PathsAndDirectoryChanges_KeepLaunchContextWithoutAsking()
    {
        var approvals = new RecordingApprovalService(approve: false);
        var gate = Gate(approvals);
        var session = Session();
        string[] inputs =
        [
            $"cat {PosixPath(_outside)}/secret.txt\n", $"cd {PosixPath(_outside)}\n",
            "for d in a b; do cd $d; done\n", "popd\n", "git status\n"
        ];

        foreach (var input in inputs)
        {
            var result = await gate.AuthorizeStdinAsync(session, input, default);
            Assert.True(result.IsAllowed);
            Assert.Equal(_root, session.WorkingDirectory);
        }

        Assert.Empty(approvals.Requests);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_OutsideLaunchDirectory_AsksWithLaunchContext()
    {
        var approvals = new RecordingApprovalService(approve: false);
        var session = new ShellStdinSession(Session().Shell, _outside);
        const string input = "git status\n";

        var result = await Gate(approvals).AuthorizeStdinAsync(session, input, default);

        Assert.False(result.IsAllowed);
        var request = Assert.Single(approvals.Requests);
        Assert.Equal(input, request.Command);
        Assert.Equal(_outside, request.WorkingDirectory);
        Assert.Contains("running terminal", request.ReasonText);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_DangerousInputWithoutApprovalService_IsDenied()
    {
        var result = await Gate(approvals: null).AuthorizeStdinAsync(Session(), "rm -rf build\n", default);

        Assert.False(result.IsAllowed);
        Assert.Contains("no approval service", result.Error);
    }

    [Fact]
    public async Task EnterAsync_WhileAnotherInteractionIsHeld_WaitsForItToFinish()
    {
        var session = Session();
        var first = await session.EnterAsync(default);

        var second = session.EnterAsync(default);
        Assert.False(second.IsCompleted);

        first.Dispose();
        (await second).Dispose();
        (await session.EnterAsync(default)).Dispose();
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

    private static string PosixPath(string path) => path.Replace('\\', '/');

    private ShellStdinSession Session() =>
        new(new ShellIdentity(ShellKind.Bash, "/bin/bash"), _root);

    private ShellExecutionGate Gate(IApprovalService? approvals) =>
        new(
            new ShellCommandSafetyKernel(
                new ShellIdentityResolver(new ShellExecutableProbe(
                    IsWindows: false,
                    SystemRoot: null,
                    FileExists: _ => true,
                    FindOnPath: name => "/usr/bin/" + name)),
                CommandPlatform.Posix),
            new WorkspaceBoundary([_root]),
            ShellPolicySource.Empty,
            requireApprovalOutsideWorkspace: true,
            approvals);

    private sealed class RecordingApprovalService(bool approve) : IApprovalService
    {
        public List<ShellApprovalRequest> Requests { get; } = [];

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
        {
            Requests.Add(request);
            return Task.FromResult(approve);
        }

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null) =>
            throw new NotSupportedException();

        public Task<bool> RequestResourceApprovalAsync(
            string kind,
            string operation,
            string target,
            ApprovalContext? context = null) => throw new NotSupportedException();
    }
}
