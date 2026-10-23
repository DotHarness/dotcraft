using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ContributionRegistryReplacementTests
{
    private static ContributionOptions Named(string targetName, int order = 0) =>
        new(Order: order) { TargetName = targetName };

    [Fact]
    public void Replacement_ShadowsTheNamedDefaultOfItsOwnPointOnly_AndRestoresItOnDisposal()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("default-identity"), Named("identity"));
        registry.Add<ILabelContract>(new LabelContribution("other"));
        registry.Add<INoteContract>(new NoteContribution("note-default"), Named("identity"));

        var replacement = registry.Add<ILabelContract>(
            new LabelContribution("plugin-identity"),
            new ContributionOptions(ReplaceTarget: "identity"));
        registry.Add<ILabelContract>(
            new LabelContribution("unmatched"),
            new ContributionOptions(ReplaceTarget: "missing"));

        Assert.Equal(
            ["other", "plugin-identity", "unmatched"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
        Assert.Equal(["note-default"], registry.Resolve<INoteContract>().Select(contribution => contribution.Note));

        replacement.Dispose();

        Assert.Equal(
            ["default-identity", "other", "unmatched"],
            registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void CompetingReplacements_LaterRegistrationWinsWhateverTheOrder_AndTheLoserIsLoggedOnce()
    {
        var logLines = new List<string>();
        var registry = new ContributionRegistry(new CollectingLogger<ContributionRegistry>(logLines));
        registry.Add<ILabelContract>(new LabelContribution("default"), Named("identity"));
        var loser = registry.Add<ILabelContract>(
            new LabelContribution("first"),
            new ContributionOptions(Order: 5, ReplaceTarget: "identity"));
        var winner = registry.Add<ILabelContract>(
            new LabelContribution("second"),
            new ContributionOptions(Order: 1, ReplaceTarget: "identity"));

        Assert.Equal(["second"], registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
        Assert.Equal(["second"], registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label));

        var logged = Assert.Single(logLines);
        Assert.Contains(ContributionDiagnosticCodes.ReplaceConflict, logged, StringComparison.Ordinal);
        Assert.Contains(loser.Id.ToString(), logged, StringComparison.Ordinal);
        Assert.Contains(nameof(ILabelContract), logged, StringComparison.Ordinal);

        winner.Dispose();

        Assert.Equal(["first"], registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));
    }

    [Fact]
    public void ThreadScopedReplacement_WinsInsideItsThreadOnly()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("default"), Named("identity"));
        registry.Add<ILabelContract>(
            new LabelContribution("thread-replacement"),
            new ContributionOptions(ContributionScope.Thread, "thread-1", ReplaceTarget: "identity"));
        // Registered last, so only the scope rule can keep it out of thread-1.
        registry.Add<ILabelContract>(
            new LabelContribution("workspace-replacement"),
            new ContributionOptions(ReplaceTarget: "identity"));

        Assert.Equal(
            ["thread-replacement"],
            registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label));
        Assert.Equal(
            ["workspace-replacement"],
            registry.Resolve<ILabelContract>("thread-2").Select(contribution => contribution.Label));
    }

    [Fact]
    public void ThreadScopedReplacement_ShadowsTheWorkspaceDefaultOnlyForThatThread()
    {
        var registry = new ContributionRegistry();
        registry.Add<ILabelContract>(new LabelContribution("default"), Named("identity"));
        var replacement = registry.Add<ILabelContract>(
            new LabelContribution("thread-only"),
            new ContributionOptions(ContributionScope.Thread, "thread-1", ReplaceTarget: "identity"));

        Assert.Equal(["thread-only"], registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label));
        Assert.Equal(["default"], registry.Resolve<ILabelContract>().Select(contribution => contribution.Label));

        replacement.Dispose();

        Assert.Equal(["default"], registry.Resolve<ILabelContract>("thread-1").Select(contribution => contribution.Label));
    }
}
