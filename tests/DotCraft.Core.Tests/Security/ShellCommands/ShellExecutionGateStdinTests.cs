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
    public async Task AuthorizeStdinAsync_CommandInsideTheWorkspace_IsAllowedWithoutAsking()
    {
        var approvals = new RecordingApprovalService(approve: false);

        var result = await Gate(approvals).AuthorizeStdinAsync(Session(), "git status\n", default);

        Assert.True(result.IsAllowed);
        Assert.Empty(approvals.Requests);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_ReadOutsideTheWorkspace_AsksAndCarriesTheInputAsTheCommand()
    {
        var approvals = new RecordingApprovalService(approve: false);
        var input = $"cat {PosixPath(_outside)}/secret.txt\n";

        var result = await Gate(approvals).AuthorizeStdinAsync(Session(), input, default);

        Assert.False(result.IsAllowed);
        var request = Assert.Single(approvals.Requests);
        Assert.Equal(input, request.Command);
        Assert.Contains("running terminal", request.ReasonText);
        Assert.Contains("rejected", result.Error);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_ApprovedDirectoryChange_MovesTheSessionAndChecksLaterInputThere()
    {
        var approvals = new RecordingApprovalService(approve: true);
        var gate = Gate(approvals);
        var session = Session();

        await gate.AuthorizeStdinAsync(session, $"cd {PosixPath(_outside)}\n", default);
        Assert.Equal(_outside, session.WorkingDirectory);

        var result = await gate.AuthorizeStdinAsync(session, "cat secret.txt\n", default);

        Assert.True(result.IsAllowed);
        Assert.Equal(2, approvals.Requests.Count);
        Assert.Contains("outside the workspace", approvals.Requests[1].ReasonText);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_DirectoryChangeItCannotFollow_MakesEveryLaterWriteUndeterminable()
    {
        var approvals = new RecordingApprovalService(approve: true);
        var gate = Gate(approvals);
        var session = Session();

        await gate.AuthorizeStdinAsync(session, "for d in a b; do cd $d; done\n", default);
        Assert.False(session.WorkingDirectoryIsKnown);

        await gate.AuthorizeStdinAsync(session, "git status\n", default);

        Assert.Equal(2, approvals.Requests.Count);
        Assert.Contains("cannot be determined", approvals.Requests[1].ReasonText);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_SessionThatLostItsDirectory_NeverRegainsOne()
    {
        var approvals = new RecordingApprovalService(approve: true);
        var gate = Gate(approvals);
        var session = Session();

        await gate.AuthorizeStdinAsync(session, "popd\n", default);
        await gate.AuthorizeStdinAsync(session, $"cd {PosixPath(_root)}\n", default);

        Assert.False(session.WorkingDirectoryIsKnown);
    }

    [Fact]
    public async Task AuthorizeStdinAsync_WithoutAnApprovalService_DeniesInsteadOfWriting()
    {
        var input = $"cat {PosixPath(_outside)}/secret.txt\n";

        var result = await Gate(approvals: null).AuthorizeStdinAsync(Session(), input, default);

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
            blacklist: null,
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
