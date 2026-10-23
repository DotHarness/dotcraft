using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GetProfileInsights_RanksModelsReasoningAndSkills(bool stateBacked)
    {
        var store = stateBacked
            ? new TraceStore(_stateRuntime, maxEventsPerSession: 5000, synchronousPersist: true)
            : new TraceStore();
        Seed(store);

        AssertSeededInsights(store.GetProfileInsights());
    }

    private static void Seed(TraceStore store)
    {
        store.Record(Response("t1", "gpt-5", "high"));
        store.Record(Response("t1", "gpt-5", "high"));
        store.Record(Response("t1", "gpt-5", "low"));
        store.Record(Response("t1", "claude-opus-4-8", null));
        store.Record(Response("t1", null, null));

        store.Record(Skill("t1", "dotcraft-error-diagnosis"));
        store.Record(Skill("t1", "dotcraft-error-diagnosis"));
        store.Record(Skill("t1", "dotcraft-error-diagnosis"));
        store.Record(Skill("t1", "skill-creator"));
        store.Record(Skill("t1", "skill-creator"));
        store.Record(Skill("t1", "alpha"));
    }

    private static void AssertSeededInsights(ProfileInsights insights)
    {
        Assert.NotNull(insights.TopModel);
        Assert.Equal("gpt-5", insights.TopModel!.Key);
        Assert.Equal(3, insights.TopModel.Count);
        Assert.Equal(4, insights.TopModel.Total);

        Assert.NotNull(insights.TopReasoning);
        Assert.Equal("high", insights.TopReasoning!.Key);
        Assert.Equal(2, insights.TopReasoning.Count);
        Assert.Equal(3, insights.TopReasoning.Total);

        Assert.Equal(3, insights.DistinctSkillCount);
        Assert.Equal(6, insights.TotalSkillCount);
        Assert.Equal(
            new[] { "dotcraft-error-diagnosis", "skill-creator", "alpha" },
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
        catch { }
    }
}
