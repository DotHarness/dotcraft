using DotCraft.Security;

namespace DotCraft.Tests.Security;

public sealed class PrefixedApprovalServiceTests
{
    [Fact]
    public async Task RequestFileApprovalAsync_PrefixesPath()
    {
        var inner = new RecordingApprovalService();
        var service = new PrefixedApprovalService(inner, "[subagent:test] ");

        var approved = await service.RequestFileApprovalAsync("read", "E:/tmp/demo.txt");

        Assert.True(approved);
        Assert.Equal("[subagent:test] E:/tmp/demo.txt", inner.LastFilePath);
    }

    [Fact]
    public async Task RequestShellApprovalAsync_PrefixesCommand()
    {
        var inner = new RecordingApprovalService();
        var service = new PrefixedApprovalService(inner, "[subagent:test] ");

        var approved = await service.RequestShellApprovalAsync("dotnet test", "E:/repo");

        Assert.True(approved);
        Assert.Equal("[subagent:test] dotnet test", inner.LastCommand);
    }

    [Fact]
    public async Task RequestResourceApprovalAsync_PrefixesTarget()
    {
        var inner = new RecordingApprovalService();
        var service = new PrefixedApprovalService(inner, "[subagent:test] ");

        var approved = await service.RequestResourceApprovalAsync("remoteResource", "create", "doc-123");

        Assert.True(approved);
        Assert.Equal("[subagent:test] doc-123", inner.LastResourceTarget);
    }

    private sealed class RecordingApprovalService : IApprovalService
    {
        public string LastFilePath { get; private set; } = string.Empty;
        public string LastCommand { get; private set; } = string.Empty;
        public string LastResourceTarget { get; private set; } = string.Empty;

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            LastFilePath = path;
            return Task.FromResult(true);
        }

        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
        {
            LastCommand = command;
            return Task.FromResult(true);
        }

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            LastResourceTarget = target;
            return Task.FromResult(true);
        }
    }
}
