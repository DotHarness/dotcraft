using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

/// <summary>The single-authority shape: the last contribution of the resolved list owns the decision, and
/// an empty list falls back to the host's built-in. Every contribution point read through
/// <see cref="ContributionRead.Authority"/> inherits these.</summary>
public sealed class ContributionReadAuthorityTests
{
    [Fact]
    public void TheLastContribution_HoldsTheAuthority()
    {
        // Last wins so a contribution that neither replaces the target nor outranks it is still reachable.
        Assert.Equal("last", ContributionRead.Authority(["first", "middle", "last"], "built-in"));
        Assert.Equal("only", ContributionRead.Authority(["only"], "built-in"));
    }

    [Fact]
    public void AnEmptyOrAbsentContributionPoint_FallsBackToTheBuiltIn()
    {
        Assert.Equal("built-in", ContributionRead.Authority([], "built-in"));
        Assert.Equal("built-in", ContributionRead.Authority(null, "built-in"));
    }

    [Fact]
    public void AnEmptyContributionPointWithoutABuiltIn_HasNoAuthority()
    {
        Assert.Null(ContributionRead.Authority<string>([]));
        Assert.Null(ContributionRead.Authority<string>(null));
    }
}
