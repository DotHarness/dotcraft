using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class SkillsSectionTests
{
    private static readonly SkillCatalogEntry Review = new("code-review", "Review code changes", "/skills/code-review/SKILL.md", false);
    private static readonly SkillCatalogEntry Release = new("release-notes", "Write release notes", "/skills/release-notes/SKILL.md", false);

    private readonly WorldState _world = WorldStateComposer.Build();
    private List<SkillCatalogEntry> _skills = [Review];

    [Fact]
    public void AChangedCatalogIsRestatedWhole()
    {
        var first = _world.RenderFull(Context());
        _skills = [Review, Release];

        var text = SkillsText(_world.Render(Context(), first.Snapshot, sectionIdsInHistory: null))!;

        Assert.Contains("<name>code-review</name>", text, StringComparison.Ordinal);
        Assert.Contains("<name>release-notes</name>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingEverySkillRetractsTheListOnce()
    {
        var first = _world.RenderFull(Context());
        _skills = [];

        var second = _world.Render(Context(), first.Snapshot, sectionIdsInHistory: null);

        Assert.NotNull(SkillsText(second));
        Assert.Null(SkillsText(_world.Render(Context(), second.Snapshot, sectionIdsInHistory: null)));
    }

    private WorldStateContext Context() => new()
    {
        Thread = new SessionThread { Id = "thread_test" },
        Skills = () => _skills
    };

    private static string? SkillsText(WorldStateRender render) =>
        render.Fragments.SingleOrDefault(fragment => fragment.SectionId == SkillsSection.SectionId)?.Text;
}
