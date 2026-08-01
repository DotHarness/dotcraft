using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Conformance tests for the <c>profile/insights</c> method (spec Section 27A.5),
/// gated behind the same <c>usageTelemetry</c> capability as the other usage methods.
/// </summary>
public sealed class AppServerProfileInsightsTests
{
    [Fact]
    public async Task ProfileInsights_ReturnsMethodNotFound_WhenTracingDisabled()
    {
        using var h = new AppServerTestHarness();
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task ProfileInsights_ReturnsEmptyMetrics_WhenNoData()
    {
        var traceStore = new TraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        // Null ranked metrics are omitted from the wire (JsonIgnore WhenWritingNull).
        Assert.False(result.TryGetProperty("topModel", out _));
        Assert.False(result.TryGetProperty("topReasoning", out _));
        Assert.Equal(0, result.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(0, result.GetProperty("totalSkillsUsed").GetInt64());
        Assert.Equal(0, result.GetProperty("totalThreads").GetInt32());
        Assert.Equal(0, result.GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task ProfileInsights_RanksTopModelAndReasoning_ExcludingMissingValues()
    {
        var traceStore = new TraceStore();
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "high");
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "high");
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "low");
        RecordResponse(traceStore, "t1", model: "claude-opus-4-8", reasoning: null);
        RecordResponse(traceStore, "t1", model: null, reasoning: null);

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");

        var model = result.GetProperty("topModel");
        Assert.Equal("gpt-5", model.GetProperty("key").GetString());
        Assert.Equal(3, model.GetProperty("count").GetInt64());
        // Total excludes the response with no model id.
        Assert.Equal(4, model.GetProperty("total").GetInt64());

        var reasoning = result.GetProperty("topReasoning");
        Assert.Equal("high", reasoning.GetProperty("key").GetString());
        Assert.Equal(2, reasoning.GetProperty("count").GetInt64());
        // Total counts only responses with a recorded effort (3 of 5).
        Assert.Equal(3, reasoning.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task ProfileInsights_CountsSkills_AndRanksByRunCount()
    {
        var traceStore = new TraceStore();
        RecordSkill(traceStore, "t1", "error-diagnosis");
        RecordSkill(traceStore, "t1", "error-diagnosis");
        RecordSkill(traceStore, "t1", "error-diagnosis");
        RecordSkill(traceStore, "t1", "skill-creator");
        RecordSkill(traceStore, "t1", "skill-creator");
        RecordSkill(traceStore, "t1", "alpha");

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(3, result.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(6, result.GetProperty("totalSkillsUsed").GetInt64());

        var skills = result.GetProperty("skills");
        Assert.Equal(3, skills.GetArrayLength());
        Assert.Equal("error-diagnosis", skills[0].GetProperty("name").GetString());
        Assert.Equal(3, skills[0].GetProperty("count").GetInt64());
        Assert.Equal("skill-creator", skills[1].GetProperty("name").GetString());
        Assert.Equal(2, skills[1].GetProperty("count").GetInt64());
        Assert.Equal("alpha", skills[2].GetProperty("name").GetString());
        // No skills loader wired → no plugin attribution on the wire.
        Assert.False(skills[0].TryGetProperty("pluginDisplayName", out _));
    }

    [Fact]
    public async Task ProfileInsights_ClampsTopSkillsRequest()
    {
        var traceStore = new TraceStore();
        for (var i = 0; i < 8; i++)
            RecordSkill(traceStore, "t1", $"skill-{i:D2}");

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { topSkills = 2 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(8, result.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(2, result.GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task ProfileInsights_CountsWorkspaceThreads_RegardlessOfChannelContext()
    {
        var traceStore = new TraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        // Reproduces the Desktop scoping: threads carry a non-null channelContext. A
        // workspace-scoped count must still include them (an identity-scoped query would not).
        await h.Service.CreateThreadAsync(h.Identity with { ChannelContext = "workspace:ws" });

        var msg = h.BuildRequest(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ProfileInsights, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        Assert.Equal(1, resp.RootElement.GetProperty("result").GetProperty("totalThreads").GetInt32());
    }

    private static void RecordResponse(TraceStore store, string sessionKey, string? model, string? reasoning)
    {
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.Response,
            ModelId = model,
            ReasoningEffort = reasoning
        });
    }

    private static void RecordSkill(TraceStore store, string sessionKey, string skillName)
    {
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.SkillReferenced,
            ToolName = skillName
        });
    }
}
