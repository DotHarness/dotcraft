using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadWorkingSetTests
{
    [Fact]
    public void Trim_ReplacesTurnsListWithoutMutatingPriorSnapshot()
    {
        var first = new SessionTurn { Id = "turn_001", Status = TurnStatus.Completed };
        var second = new SessionTurn { Id = "turn_002", Status = TurnStatus.Cancelled };
        var thread = new SessionThread
        {
            Id = "thread_trim",
            Turns = [first, second]
        };

        var snapshot = thread.Turns;

        ThreadWorkingSet.Trim(thread);

        Assert.NotSame(snapshot, thread.Turns);
        Assert.Equal(["turn_001", "turn_002"], snapshot.Select(turn => turn.Id).ToArray());
        Assert.Equal(["turn_002"], thread.Turns.Select(turn => turn.Id).ToArray());
        Assert.Equal(1, thread.OmittedTurnCount);
    }
}
