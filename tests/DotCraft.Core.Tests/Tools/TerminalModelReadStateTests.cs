using DotCraft.Tools.BackgroundTerminals;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class TerminalModelReadStateTests
{
    [Fact]
    public void CompletionBeforeHandOff_IsNotifiedOnceAtHandOff()
    {
        var state = new TerminalModelReadState();

        var atCompletion = state.Complete(suppressed: false);
        var atHandOff = state.HandOff(end: 0, running: true, armed: true);
        var again = state.LeaveWait(suppressed: false);

        Assert.False(atCompletion);
        Assert.True(atHandOff);
        Assert.False(again);
    }

    [Fact]
    public void CompletionDuringAWait_IsLeftToTheWaiterUnlessItReadsAFinalStatus()
    {
        var reported = new TerminalModelReadState();
        reported.HandOff(end: 0, running: true, armed: true);
        reported.EnterWait();
        var duringWait = reported.Complete(suppressed: false);
        reported.Read(end: 10, final: true);

        var missed = new TerminalModelReadState();
        missed.HandOff(end: 0, running: true, armed: true);
        missed.EnterWait();
        missed.Complete(suppressed: false);
        missed.Read(end: 10, final: false);

        Assert.False(duringWait);
        Assert.False(reported.LeaveWait(suppressed: false));
        Assert.True(missed.LeaveWait(suppressed: false));
    }
}
