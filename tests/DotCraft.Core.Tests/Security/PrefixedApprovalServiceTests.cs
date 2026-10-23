using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Tests.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security;

public sealed class PrefixedApprovalServiceTests
{
    [Fact]
    public async Task FileAndResourceApprovals_PrefixTheirTarget()
    {
        var inner = new RecordingApprovalService();
        var service = new PrefixedApprovalService(inner, "[subagent:test] ");

        Assert.True(await service.RequestFileApprovalAsync("read", "/tmp/demo.txt"));
        Assert.True(await service.RequestResourceApprovalAsync("remoteResource", "create", "doc-123"));

        Assert.Equal("[subagent:test] /tmp/demo.txt", inner.LastFilePath);
        Assert.Equal("[subagent:test] doc-123", inner.LastResourceTarget);
    }

    [Fact]
    public async Task RequestShellApprovalAsync_LabelsTheRequestWithoutRewritingTheCommand()
    {
        var inner = new RecordingApprovalService();
        var service = new PrefixedApprovalService(inner, "[subagent:test] ");
        var request = ShellApprovalRequests.For("dotnet test");

        var approved = await service.RequestShellApprovalAsync(request);

        Assert.True(approved);
        Assert.Equal("dotnet test", inner.LastShellRequest!.Command);
        Assert.Equal("[subagent:test]", inner.LastShellRequest.Label);
        Assert.Equal(request.ApprovalKey, inner.LastShellRequest.ApprovalKey);
    }

    private sealed class RecordingApprovalService : IApprovalService
    {
        public string LastFilePath { get; private set; } = string.Empty;
        public ShellApprovalRequest? LastShellRequest { get; private set; }
        public string LastResourceTarget { get; private set; } = string.Empty;

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            LastFilePath = path;
            return Task.FromResult(true);
        }

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
        {
            LastShellRequest = request;
            return Task.FromResult(true);
        }

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            LastResourceTarget = target;
            return Task.FromResult(true);
        }
    }
}
