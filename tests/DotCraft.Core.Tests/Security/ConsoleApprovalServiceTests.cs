using DotCraft.Security;
using Xunit;

namespace DotCraft.Tests.Security;

public sealed class ConsoleApprovalServiceTests
{
    [Fact]
    public async Task SessionDecision_ReusesApprovalWithoutPromptingAgain()
    {
        var prompt = new StubPrompt(InteractiveApprovalDecision.Session);
        var service = new ConsoleApprovalService(prompt);

        Assert.True(await service.RequestFileApprovalAsync("read", "outside-a.txt"));
        Assert.True(await service.RequestFileApprovalAsync("read", "outside-b.txt"));
        Assert.Equal(1, prompt.FileRequestCount);
    }

    [Fact]
    public async Task RejectDecision_DeniesShellRequest()
    {
        var prompt = new StubPrompt(InteractiveApprovalDecision.Reject);
        var service = new ConsoleApprovalService(prompt);

        Assert.False(await service.RequestShellApprovalAsync("dotnet test", "workspace"));
        Assert.Equal(1, prompt.ShellRequestCount);
    }

    [Fact]
    public async Task MissingHostPrompt_FailsClosed()
    {
        var service = new ConsoleApprovalService();

        Assert.False(await service.RequestResourceApprovalAsync("remoteResource", "publish", "target"));
    }

    private sealed class StubPrompt(InteractiveApprovalDecision decision) : IInteractiveApprovalPrompt
    {
        public int FileRequestCount { get; private set; }

        public int ShellRequestCount { get; private set; }

        public InteractiveApprovalDecision RequestFileApproval(string operation, string path)
        {
            FileRequestCount++;
            return decision;
        }

        public InteractiveApprovalDecision RequestShellApproval(string command, string? workingDirectory)
        {
            ShellRequestCount++;
            return decision;
        }
    }
}
