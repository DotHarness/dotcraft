using System.Text.Json;
using DotCraft.Tracing;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerProfileInsightsTests
{
    [Fact]
    public async Task ProfileInsights_RequiresTracingAndReturnsEmptyMetricsWithoutData()
    {
        using (var withoutTracing = new AppServerTestHarness())
        {
            await withoutTracing.InitializeAsync();
            await withoutTracing.ExecuteRequestAsync(withoutTracing.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProfileInsights, new { }));
            AppServerTestHarness.AssertIsErrorResponse(withoutTracing.Transport.TryReadSent()!, AppServerErrors.MethodNotFoundCode);
        }

        using var h = new AppServerTestHarness(traceStore: new TraceStore());
        await h.InitializeAsync();
        var result = await InsightsAsync(h, new { });
        Assert.False(result.TryGetProperty("topModel", out _));
        Assert.False(result.TryGetProperty("topReasoning", out _));
        Assert.Equal(0, result.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(0, result.GetProperty("totalSkillsUsed").GetInt64());
        Assert.Equal(0, result.GetProperty("totalThreads").GetInt32());
        Assert.Equal(0, result.GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task ProfileInsights_RanksModelsReasoningAndSkillsAndCountsWorkspaceThreads()
    {
        var traceStore = new TraceStore();
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "high");
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "high");
        RecordResponse(traceStore, "t1", model: "gpt-5", reasoning: "low");
        RecordResponse(traceStore, "t1", model: "claude-opus-4-8", reasoning: null);
        RecordResponse(traceStore, "t1", model: null, reasoning: null);
        foreach (var skill in new[] { "dotcraft-error-diagnosis", "dotcraft-error-diagnosis", "dotcraft-error-diagnosis", "skill-creator", "skill-creator", "alpha" })
            RecordSkill(traceStore, "t1", skill);
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();
        await h.Service.CreateThreadAsync(h.Identity with { ChannelContext = "workspace:ws" });

        var result = await InsightsAsync(h, new { });

        var model = result.GetProperty("topModel");
        Assert.Equal("gpt-5", model.GetProperty("key").GetString());
        Assert.Equal(3, model.GetProperty("count").GetInt64());
        Assert.Equal(4, model.GetProperty("total").GetInt64());
        var reasoning = result.GetProperty("topReasoning");
        Assert.Equal("high", reasoning.GetProperty("key").GetString());
        Assert.Equal(2, reasoning.GetProperty("count").GetInt64());
        Assert.Equal(3, reasoning.GetProperty("total").GetInt64());

        Assert.Equal(3, result.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(6, result.GetProperty("totalSkillsUsed").GetInt64());
        var skills = result.GetProperty("skills");
        Assert.Equal(["dotcraft-error-diagnosis", "skill-creator", "alpha"], skills.EnumerateArray().Select(skill => skill.GetProperty("name").GetString()));
        Assert.Equal([3L, 2L, 1L], skills.EnumerateArray().Select(skill => skill.GetProperty("count").GetInt64()));
        Assert.False(skills[0].TryGetProperty("pluginId", out _));
        Assert.Equal(1, result.GetProperty("totalThreads").GetInt32());

        var clamped = await InsightsAsync(h, new { topSkills = 1 });
        Assert.Equal(3, clamped.GetProperty("skillsExplored").GetInt32());
        Assert.Equal(1, clamped.GetProperty("skills").GetArrayLength());
    }

    private static async Task<JsonElement> InsightsAsync(AppServerTestHarness harness, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.ProfileInsights, parameters));
        using var response = harness.Transport.TryReadSent()!;
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
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
