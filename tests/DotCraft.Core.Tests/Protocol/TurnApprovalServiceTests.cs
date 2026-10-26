using DotCraft.Security;
using DotCraft.Sessions;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class TurnApprovalServiceTests
{
    private static (TurnApprovalService service, SessionApprovalService interactive, SessionTurn turn) Create(
        Func<ApprovalPolicy> policy)
    {
        var turn = new SessionTurn
        {
            Id = "turn_live_001",
            ThreadId = "thread_live_001",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        var seq = 1;
        var interactive = new SessionApprovalService(
            new SessionEventChannel(turn.ThreadId, turn.Id),
            turn,
            () => seq++,
            CancellationToken.None,
            () => { });
        return (new TurnApprovalService(policy, interactive), interactive, turn);
    }

    [Fact]
    public async Task SwitchingToAutoApprove_SettlesPendingAndSkipsLaterPrompts()
    {
        var policy = ApprovalPolicy.Prompt;
        var (service, interactive, turn) = Create(() => policy);

        var pending = service.RequestFileApprovalAsync("write", "/tmp/a.txt");
        Assert.True(interactive.HasPendingApproval);
        Assert.Equal(TurnStatus.WaitingApproval, turn.Status);

        policy = ApprovalPolicy.AutoApprove;
        interactive.ResolvePending(SessionApprovalDecision.AcceptOnce);

        Assert.True(await pending);
        Assert.Equal(TurnStatus.Running, turn.Status);
        Assert.True(await service.RequestShellApprovalAsync(
            DotCraft.Tests.Security.ShellCommands.ShellApprovalRequests.For("rm -rf /tmp/data", "/tmp")));
        Assert.Single(turn.Items, item => item.Type == ItemType.ApprovalRequest);
        Assert.Single(turn.Items, item => item.Type == ItemType.ApprovalResponse);
    }

    [Fact]
    public async Task SwitchingToDeny_RejectsPendingAndLaterRequestsWithoutPrompting()
    {
        var policy = ApprovalPolicy.Default;
        var (service, interactive, turn) = Create(() => policy);

        var first = service.RequestResourceApprovalAsync("remoteResource", "create", "doc-1");
        var second = service.RequestFileApprovalAsync("edit", "/tmp/b.txt");

        policy = ApprovalPolicy.Deny;
        interactive.ResolvePending(SessionApprovalDecision.Reject);

        Assert.False(await first);
        Assert.False(await second);
        Assert.False(await service.RequestFileApprovalAsync("write", "/tmp/c.txt"));
        Assert.False(interactive.HasPendingApproval);
        Assert.Equal(2, turn.Items.Count(item => item.Type == ItemType.ApprovalRequest));
        Assert.Equal(TurnStatus.Running, turn.Status);
    }

    [Fact]
    public void AutoApproveCheck_FollowsTheCurrentPolicy()
    {
        var policy = ApprovalPolicy.Prompt;
        var (service, _, _) = Create(() => policy);

        Assert.False(ApprovalServiceChain.AutoApproves(service, context: null));

        policy = ApprovalPolicy.AutoApprove;

        Assert.True(ApprovalServiceChain.AutoApproves(service, context: null));
    }
}
