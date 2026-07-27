using DotCraft.Persistence;
using DotCraft.Tracing;

namespace DotCraft.Tests.Tracing;

/// <summary>
/// Unit tests for <see cref="TraceStore.GetProfileInsights"/> (spec Section 27A.5),
/// exercising both the in-memory aggregation path (no state runtime) and the SQL
/// aggregation path (state-db backed).
/// </summary>
public sealed class ProfileInsightsStoreTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceStateDatabase _stateRuntime;

    public ProfileInsightsStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "profile-insights-tests", Guid.NewGuid().ToString("N"));
        var craftPath = Path.Combine(_root, ".craft");
        Directory.CreateDirectory(Path.Combine(craftPath, "tracing"));
        _stateRuntime = new WorkspaceStateDatabase(craftPath);
    }

    [Fact]
    public void GetProfileInsights_InMemory_RanksModelsReasoningAndSkills()
    {
        var store = new TraceStore();
        Seed(store);

        AssertSeededInsights(store.GetProfileInsights());
    }

    [Fact]
    public void GetProfileInsights_StateDb_RanksModelsReasoningAndSkills()
    {
        var store = new TraceStore(
            _stateRuntime,
            maxEventsPerSession: 5000,
            synchronousPersist: true);
        Seed(store);

        AssertSeededInsights(store.GetProfileInsights());
    }

    [Fact]
    public void GetProfileInsights_ReturnsEmpty_WhenNoData()
    {
        var insights = new TraceStore().GetProfileInsights();

        Assert.Null(insights.TopModel);
        Assert.Null(insights.TopReasoning);
        Assert.Equal(0, insights.DistinctSkillCount);
        Assert.Equal(0, insights.TotalSkillCount);
        Assert.Empty(insights.TopSkills);
    }

    [Fact]
    public void GetProfileInsights_HonorsTopSkillsLimit()
    {
        var store = new TraceStore();
        for (var i = 0; i < 6; i++)
            store.Record(Skill("t1", $"skill-{i:D2}"));

        var insights = store.GetProfileInsights(topSkills: 3);

        Assert.Equal(6, insights.DistinctSkillCount);
        Assert.Equal(6, insights.TotalSkillCount);
        Assert.Equal(3, insights.TopSkills.Count);
    }

    private static void Seed(TraceStore store)
    {
        // Models: gpt-5 ×3, claude ×1, plus one response with no model id (excluded from total).
        store.Record(Response("t1", "gpt-5", "high"));
        store.Record(Response("t1", "gpt-5", "high"));
        store.Record(Response("t1", "gpt-5", "low"));
        store.Record(Response("t1", "claude-opus-4-8", null));
        store.Record(Response("t1", null, null));

        // Skills: error-diagnosis ×3, skill-creator ×2, alpha ×1.
        store.Record(Skill("t1", "error-diagnosis"));
        store.Record(Skill("t1", "error-diagnosis"));
        store.Record(Skill("t1", "error-diagnosis"));
        store.Record(Skill("t1", "skill-creator"));
        store.Record(Skill("t1", "skill-creator"));
        store.Record(Skill("t1", "alpha"));
    }

    private static void AssertSeededInsights(ProfileInsights insights)
    {
        Assert.NotNull(insights.TopModel);
        Assert.Equal("gpt-5", insights.TopModel!.Key);
        Assert.Equal(3, insights.TopModel.Count);
        Assert.Equal(4, insights.TopModel.Total); // null-model response excluded

        Assert.NotNull(insights.TopReasoning);
        Assert.Equal("high", insights.TopReasoning!.Key);
        Assert.Equal(2, insights.TopReasoning.Count);
        Assert.Equal(3, insights.TopReasoning.Total); // only responses with an effort

        Assert.Equal(3, insights.DistinctSkillCount);
        Assert.Equal(6, insights.TotalSkillCount);
        Assert.Equal(
            new[] { "error-diagnosis", "skill-creator", "alpha" },
            insights.TopSkills.Select(s => s.Name).ToArray());
        Assert.Equal(3, insights.TopSkills[0].Count);
    }

    private static TraceEvent Response(string sessionKey, string? model, string? reasoning) => new()
    {
        SessionKey = sessionKey,
        Type = TraceEventType.Response,
        ModelId = model,
        ReasoningEffort = reasoning
    };

    private static TraceEvent Skill(string sessionKey, string name) => new()
    {
        SessionKey = sessionKey,
        Type = TraceEventType.SkillReferenced,
        ToolName = name
    };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
