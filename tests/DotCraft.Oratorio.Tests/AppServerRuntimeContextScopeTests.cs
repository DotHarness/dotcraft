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

    [Fact]
    public void RuntimeContext_ShipsSharedPagesAndOnlyTheDraftPagesWhoseToolIsDeclared()
    {
        var catalog = Catalog();
        foreach (var kind in new[] { ItemKind.PullRequest, ItemKind.Issue, ItemKind.LocalTask })
        foreach (var source in new string?[] { "github", "gitlab", "local", null })
        {
            var tools = catalog.CreateRunToolSet(RunPurpose.Implementation, kind, source).AllowedLocalNames;
            var pages = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(kind, source);

            Assert.Contains("oratorio.runContract", pages.Keys);
            Assert.Contains("oratorio.discussionTurn", pages.Keys);
            Assert.Contains("oratorio.followUpDraft", pages.Keys);
            foreach (var (page, tool) in DraftPages)
                Assert.Equal(tools.Contains(tool), pages.ContainsKey(page));
        }
    }

    [Fact]
    public void RuntimeContext_DropsTheReviewContractForItemsThatCanNeverSubmitOne()
    {
        var localTask = AppServerPromptBuilder.BuildThreadRuntimeAdditionalContext(ItemKind.LocalTask, "local");

        Assert.DoesNotContain("oratorio.reviewDraft", localTask.Keys);
        Assert.Contains("oratorio.implementationDraft", localTask.Keys);
    }
}
