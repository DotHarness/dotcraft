using DotCraft.Security;

namespace DotCraft.Tests.Security;

public sealed class FileAccessGuardTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), $"file_access_guard_{Guid.NewGuid():N}");

    public FileAccessGuardTests()
    {
        Directory.CreateDirectory(_workspaceRoot);
    }

    [Fact]
    public async Task ValidatePathAsync_OutsideWorkspace_WhenApprovalRequired_RequestsApproval()
    {
        var approval = new RecordingApprovalService(fileApproved: true);
        var guard = new FileAccessGuard(
            _workspaceRoot,
            requireApprovalOutsideWorkspace: true,
            approvalService: approval);

        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", $"outside_{Guid.NewGuid():N}.txt"));
        var result = await guard.ValidatePathAsync(outsidePath, "read", outsidePath);

        Assert.Null(result);
        Assert.Equal(1, approval.FileApprovalCalls);
    }

    [Fact]
    public async Task ValidatePathAsync_OutsideWorkspace_WhenApprovalNotRequired_ReturnsBoundaryErrorWithoutApproval()
    {
        var approval = new RecordingApprovalService(fileApproved: true);
        var guard = new FileAccessGuard(
            _workspaceRoot,
            requireApprovalOutsideWorkspace: false,
            approvalService: approval);

        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "..", $"outside_{Guid.NewGuid():N}.txt"));
        var result = await guard.ValidatePathAsync(outsidePath, "read", outsidePath);

        Assert.NotNull(result);
        Assert.Contains("outside workspace boundary", result, StringComparison.Ordinal);
        Assert.Equal(0, approval.FileApprovalCalls);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspaceRoot))
                Directory.Delete(_workspaceRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp directories.
        }
    }

    private sealed class RecordingApprovalService(bool fileApproved) : IApprovalService
    {
        public int FileApprovalCalls { get; private set; }

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            FileApprovalCalls++;
            return Task.FromResult(fileApproved);
        }

        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
            => Task.FromResult(true);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
            => Task.FromResult(true);
    }
}
