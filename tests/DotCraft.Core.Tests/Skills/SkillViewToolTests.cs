using DotCraft.Skills;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Skills;

public sealed class SkillViewToolTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "dotcraft-skillview-tool-tests", Guid.NewGuid().ToString("N"));
    private readonly SkillsLoader _skillsLoader;

    public SkillViewToolTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _skillsLoader = new SkillsLoader(_tempRoot);
    }

    [Fact]
    public void SkillView_RecordsSkillReferencedOnlyOnSuccessfulLoad()
    {
        WriteSourceSkill("demo-skill", "Body.");
        var store = new TraceStore();
        var tool = new SkillViewTool(_skillsLoader, new TraceCollector(store));

        var previous = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = "thread-x";
        try
        {
            Assert.Contains("not found", tool.SkillView("missing-skill"));
            Assert.DoesNotContain(store.GetEvents("thread-x"), e => e.Type == TraceEventType.SkillReferenced);

            var result = tool.SkillView("demo-skill");
            Assert.Contains("Body.", result);

            var evt = Assert.Single(
                store.GetEvents("thread-x"), e => e.Type == TraceEventType.SkillReferenced);
            Assert.Equal("demo-skill", evt.ToolName);

            var insights = store.GetProfileInsights();
            Assert.Equal(1, insights.DistinctSkillCount);
            Assert.Equal(1, insights.TotalSkillCount);
            Assert.Equal("demo-skill", insights.TopSkills[0].Name);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
            TracingChatClient.ClearActiveSession("thread-x");
        }
    }

    private void WriteSourceSkill(string name, string body)
    {
        var skillDir = Path.Combine(_skillsLoader.WorkspaceSkillsPath, name);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), ValidSkill(name, body));
    }

    private static string ValidSkill(string name, string body) =>
        $"""
        ---
        name: {name}
        description: Test skill for {name}
        version: 0.1.0
        ---

        # {name}

        {body}
        """;

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); }
        catch { }
    }
}
