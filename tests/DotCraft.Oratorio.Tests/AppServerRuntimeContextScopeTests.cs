using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.Oratorio.Domain;
using DotCraft.Oratorio.Integrations;

namespace DotCraft.Oratorio.Tests;

public sealed class AppServerRuntimeContextScopeTests
{
    private static readonly (string Page, string Tool)[] DraftPages =
    [
        ("oratorio.reviewDraft", OratorioDynamicToolCatalog.SubmitReviewDraftName),
        ("oratorio.implementationDraft", OratorioDynamicToolCatalog.SubmitImplementationDraftName)
    ];

    private static OratorioDynamicToolCatalog Catalog() =>
        new(NullLogger<OratorioDynamicToolCatalog>.Instance);

    public static TheoryData<RunPurpose, ItemKind, string?> RunShapes()
    {
        var data = new TheoryData<RunPurpose, ItemKind, string?>();
        foreach (var purpose in new[] { RunPurpose.ReviewAnalysis, RunPurpose.Implementation })
        {
            foreach (var kind in new[] { ItemKind.PullRequest, ItemKind.Issue, ItemKind.LocalTask })
            {
                foreach (var source in new string?[] { "github", "gitlab", "local", null })
                    data.Add(purpose, kind, source);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RunShapes))]
    public void RuntimeContext_ShipsADraftPageOnlyWhenItsToolIsExposed(
        RunPurpose purpose,
        ItemKind kind,
        string? source)
    {
        var tools = Catalog().CreateRunToolSet(purpose, kind, source).AllowedLocalNames;
        var pages = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(purpose, kind, source);

        foreach (var (page, tool) in DraftPages)
            Assert.Equal(tools.Contains(tool), pages.ContainsKey(page));
    }

    [Theory]
    [MemberData(nameof(RunShapes))]
    public void RuntimeContext_AlwaysShipsTheContractAndDiscussionPages(
        RunPurpose purpose,
        ItemKind kind,
        string? source)
    {
        var pages = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(purpose, kind, source);

        Assert.Contains("oratorio.runContract", pages.Keys);
        Assert.Contains("oratorio.discussionTurn", pages.Keys);
        Assert.Contains("oratorio.followUpDraft", pages.Keys);
    }

    [Fact]
    public void RuntimeContext_DropsTheReviewContractForItemsThatCanNeverSubmitOne()
    {
        var localTask = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(
            RunPurpose.Implementation, ItemKind.LocalTask, "local");

        Assert.DoesNotContain("oratorio.reviewDraft", localTask.Keys);
        Assert.Contains("oratorio.implementationDraft", localTask.Keys);
    }

    [Fact]
    public void RuntimeContext_IsStableAcrossPurposesWhenTheToolSetIs()
    {
        var catalog = Catalog();
        var review = catalog.CreateRunToolSet(RunPurpose.ReviewAnalysis, ItemKind.PullRequest, "github");
        var implementation = catalog.CreateRunToolSet(RunPurpose.Implementation, ItemKind.PullRequest, "github");
        Assert.Equal(review.QualifiedIds, implementation.QualifiedIds);

        Assert.Equal(
            AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(
                RunPurpose.ReviewAnalysis, ItemKind.PullRequest, "github").Keys,
            AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(
                RunPurpose.Implementation, ItemKind.PullRequest, "github").Keys);
    }
}
