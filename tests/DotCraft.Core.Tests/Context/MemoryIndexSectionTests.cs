using DotCraft.Context.WorldState;
using DotCraft.Memory;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class MemoryIndexSectionTests : IDisposable
{
    private const string Branches = "- [Branches](no-branches.md) — never branch unasked";
    private const string Release = "- [Release](release.md) — 0.8.6 copy is written";
    private const string Tests = "- [Tests](focused-tests.md) — run affected tests only";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "MemoryIndexSection_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly MemoryStore _store;
    private readonly WorldState _world = WorldStateComposer.Build();

    public MemoryIndexSectionTests()
    {
        Directory.CreateDirectory(_root);
        _store = new MemoryStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public void MemoryDisabled_ContributesNoTextAndEnablingShowsTheIndex()
    {
        WriteIndex(Branches);

        var disabled = _world.RenderFull(Context(memory: null));
        Assert.Null(MemoryTextOrNull(disabled));

        var enabled = _world.Render(Context(), disabled.Snapshot, sectionIdsInHistory: null);
        Assert.Contains(Branches, MemoryText(enabled), StringComparison.Ordinal);
    }

    [Fact]
    public void FirstStep_ShowsTheWholeIndexStaysSilentAndBaselineResetOrMissingBaselineRestatesIt()
    {
        WriteIndex(Branches, Release);

        var first = _world.RenderFull(Context());
        var text = MemoryText(first);
        Assert.Contains(Branches, text, StringComparison.Ordinal);
        Assert.Contains(Release, text, StringComparison.Ordinal);

        var next = _world.Render(Context(), first.Snapshot, sectionIdsInHistory: null);
        Assert.Null(MemoryTextOrNull(next));
        Assert.Contains(MemoryIndexSection.SectionId, next.SilentSectionIds);

        var afterCompaction = MemoryText(_world.RenderFull(Context()));
        Assert.Contains(Branches, afterCompaction, StringComparison.Ordinal);
        Assert.Contains(Release, afterCompaction, StringComparison.Ordinal);

        WriteIndex(Branches, Release);
        var baselineWithoutMemory = WorldStateSnapshot.FromSections([]);

        var render = _world.Render(
            Context(),
            baselineWithoutMemory,
            new HashSet<string>(StringComparer.Ordinal) { MemoryIndexSection.SectionId });

        var historyText = MemoryText(render);
        Assert.Contains(Branches, historyText, StringComparison.Ordinal);
        Assert.Contains(Release, historyText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyIndex_IsShownAndRecordedDistinctlyFromAnAbsentOne()
    {
        var first = _world.RenderFull(Context());

        Assert.NotNull(MemoryTextOrNull(first));
        Assert.True(first.Snapshot.TryGetSection(MemoryIndexSection.SectionId, out _));

        WriteIndex(Branches);
        var next = _world.Render(Context(), first.Snapshot, sectionIdsInHistory: null);

        Assert.Contains(Branches, MemoryText(next), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangeFromAnotherWriterBetweenSteps_RestatesOnlyTheEntriesThatMoved()
    {
        WriteIndex(Branches, Release, "Legacy note that is not a pointer.");
        var first = _world.RenderFull(Context());

        var otherThreadStore = new MemoryStore(_root);
        const string editedRelease = "- [Release](release.md) — 0.8.6 copy is published";
        File.WriteAllText(otherThreadStore.IndexFilePath, string.Join('\n', Branches, editedRelease, Tests));

        var text = MemoryText(_world.Render(Context(), first.Snapshot, sectionIdsInHistory: null));

        Assert.Contains(editedRelease, text, StringComparison.Ordinal);
        Assert.Contains(Tests, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Branches, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Release, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy note", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovedEntries_AreNamedByTheirFile()
    {
        WriteIndex(Branches, Release);
        var first = _world.RenderFull(Context());

        WriteIndex(Branches);
        var text = MemoryText(_world.Render(Context(), first.Snapshot, sectionIdsInHistory: null));

        Assert.Contains("release.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Branches, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Snapshot_SurvivesAMergePatchRoundTrip()
    {
        WriteIndex(Branches, Release);
        var first = _world.RenderFull(Context());
        WriteIndex(Release, Tests);
        var second = _world.Render(Context(), first.Snapshot, sectionIdsInHistory: null);

        var replayed = WorldStateSnapshot.FromJsonObject(first.Snapshot.ToJsonObject());
        replayed.ApplyMergePatch(second.Snapshot.MergePatchFrom(first.Snapshot)!);

        Assert.Null(MemoryTextOrNull(_world.Render(Context(), replayed, sectionIdsInHistory: null)));
    }

    private void WriteIndex(params string[] lines) =>
        File.WriteAllText(_store.IndexFilePath, string.Join('\n', lines));

    [Fact]
    public void AttachedStores_FollowTheOwnIndexAndUpdateSeparately()
    {
        WriteIndex(Branches);
        var shared = Attached("org", "Organization");
        File.WriteAllText(Path.Combine(shared.DirectoryPath, MemoryStore.IndexFileName), Release);

        var first = _world.RenderFull(Context([shared]));
        var text = MemoryText(first);
        Assert.True(text.IndexOf(Branches, StringComparison.Ordinal) < text.IndexOf("Organization", StringComparison.Ordinal));
        Assert.Contains(Release, text, StringComparison.Ordinal);

        Assert.Null(MemoryTextOrNull(_world.Render(Context([shared]), first.Snapshot, sectionIdsInHistory: null)));

        File.WriteAllText(Path.Combine(shared.DirectoryPath, MemoryStore.IndexFileName), Release + "\n" + Tests);
        var update = MemoryText(_world.Render(Context([shared]), first.Snapshot, sectionIdsInHistory: null));
        Assert.Contains(Tests, update, StringComparison.Ordinal);
        Assert.DoesNotContain(Branches, update, StringComparison.Ordinal);
        Assert.DoesNotContain(Release, update, StringComparison.Ordinal);
    }

    [Fact]
    public void AttachingAndDetachingAStore_IsAnnounced()
    {
        WriteIndex(Branches);
        var shared = Attached("org", "Organization");
        File.WriteAllText(Path.Combine(shared.DirectoryPath, MemoryStore.IndexFileName), Release);

        var alone = _world.RenderFull(Context());
        var attached = _world.Render(Context([shared]), alone.Snapshot, sectionIdsInHistory: null);
        Assert.Contains(Release, MemoryText(attached), StringComparison.Ordinal);
        Assert.DoesNotContain(Branches, MemoryText(attached), StringComparison.Ordinal);

        var detached = MemoryText(_world.Render(Context(), attached.Snapshot, sectionIdsInHistory: null));
        Assert.Contains("Organization", detached, StringComparison.Ordinal);
        Assert.DoesNotContain(Release, detached, StringComparison.Ordinal);
    }

    private AttachedMemoryStore Attached(string id, string name) =>
        Assert.Single(MemoryScopes.AttachedStores(
            new ThreadConfiguration { MemoryStores = [new MemoryStoreAttachment(id, name, "Shared knowledge.", MemoryStoreAccess.ReadWrite)] },
            _root));

    private WorldStateContext Context(IReadOnlyList<AttachedMemoryStore> attached) => new()
    {
        Thread = new SessionThread { Id = "thread_memory", WorkspacePath = _root },
        MemoryStore = _store,
        AttachedMemoryStores = attached
    };

    private WorldStateContext Context(MemoryStore? memory) => new()
    {
        Thread = new SessionThread { Id = "thread_memory", WorkspacePath = _root },
        MemoryStore = memory
    };

    private WorldStateContext Context() => Context(_store);

    private static string MemoryText(WorldStateRender render) =>
        MemoryTextOrNull(render) ?? throw new Xunit.Sdk.XunitException("The memory index section rendered nothing.");

    private static string? MemoryTextOrNull(WorldStateRender render) =>
        render.Fragments.SingleOrDefault(static fragment => fragment.SectionId == MemoryIndexSection.SectionId)?.Text;
}
