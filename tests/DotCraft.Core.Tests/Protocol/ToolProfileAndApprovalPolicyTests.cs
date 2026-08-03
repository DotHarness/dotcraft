using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Protocol;
using DotCraft.Tools;
using DotCraft.Sessions;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;

namespace DotCraft.Tests.Protocol;

public class ToolProfileAndApprovalPolicyTests
{
    [Fact]
    public void ToolProfileRegistry_RegisterTryGet_IsCaseInsensitive()
    {
        var registry = new ToolProfileRegistry();
        var sources = (IReadOnlyList<IToolSource>)Array.Empty<IToolSource>();
        registry.Register("local-task", sources);

        Assert.True(registry.TryGet("local-task", out var p1));
        Assert.Same(sources, p1);

        Assert.True(registry.TryGet("LOCAL-TASK", out var p2));
        Assert.Same(sources, p2);

        Assert.False(registry.TryGet("missing", out _));
    }

    [Fact]
    public void ThreadConfiguration_SerializesApprovalPolicyAsWireStrings()
    {
        var cfg = new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.AutoApprove };
        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains("\"approvalPolicy\":\"autoApprove\"", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal(ApprovalPolicy.AutoApprove, roundTrip.ApprovalPolicy);
    }

    [Fact]
    public void ThreadConfiguration_SerializesPromptApprovalPolicyAsWireString()
    {
        var cfg = new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.Prompt };
        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains("\"approvalPolicy\":\"prompt\"", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal(ApprovalPolicy.Prompt, roundTrip.ApprovalPolicy);
    }

    [Fact]
    public void ThreadConfiguration_RoundTripsToolProfile()
    {
        var cfg = new ThreadConfiguration { ToolProfile = "local-task" };
        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.Equal("local-task", roundTrip?.ToolProfile);
    }

    [Fact]
    public void ThreadConfiguration_RoundTripsRequireApprovalOutsideWorkspace()
    {
        var cfg = new ThreadConfiguration { RequireApprovalOutsideWorkspace = false };
        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains("\"requireApprovalOutsideWorkspace\":false", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.False(roundTrip.RequireApprovalOutsideWorkspace);
    }

    [Fact]
    public void ThreadConfiguration_RoundTripsAgentProfilePolicyFields()
    {
        var cfg = new ThreadConfiguration
        {
            AgentProfileId = "team-reviewer",
            AgentProfileSource = "workspace",
            AgentProfileFingerprint = "sha256:abc",
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile", "GrepFiles"],
                Deny = ["WriteFile"],
                AgentControl = "allowList",
                AllowedAgentControlTools = ["WaitAgent"]
            },
            McpPolicy = new ThreadMcpPolicy
            {
                Servers = ["github-readonly"],
                Tools = new ThreadNamePolicy
                {
                    Allow = ["mcp__github-readonly__get_*"],
                    Deny = ["*write*"]
                }
            },
            PluginPolicy = new ThreadPluginPolicy
            {
                Allow = ["github"],
                Deny = ["agent-teams"]
            },
            SkillsPolicy = new ThreadSkillsPolicy
            {
                Preload = ["code-review"],
                Allow = ["code-review", "repo-style"],
                Deny = ["dangerous-skill"],
                AllowManage = false
            },
            TeamsPolicy = new ThreadTeamsPolicy
            {
                ReservedTools = "keep"
            }
        };

        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains("\"agentProfileId\":\"team-reviewer\"", json);
        Assert.Contains("\"toolPolicy\"", json);
        Assert.Contains("\"mcpPolicy\"", json);
        Assert.Contains("\"skillsPolicy\"", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal("team-reviewer", roundTrip!.AgentProfileId);
        Assert.Equal("workspace", roundTrip.AgentProfileSource);
        Assert.Equal("sha256:abc", roundTrip.AgentProfileFingerprint);
        Assert.Equal(["ReadFile", "GrepFiles"], roundTrip.ToolPolicy!.Allow!);
        Assert.Equal(["WriteFile"], roundTrip.ToolPolicy.Deny!);
        Assert.Equal("allowList", roundTrip.ToolPolicy.AgentControl);
        Assert.Equal(["WaitAgent"], roundTrip.ToolPolicy.AllowedAgentControlTools!);
        Assert.Equal(["github-readonly"], roundTrip.McpPolicy!.Servers!);
        Assert.Equal(["mcp__github-readonly__get_*"], roundTrip.McpPolicy.Tools!.Allow!);
        Assert.Equal(["*write*"], roundTrip.McpPolicy.Tools.Deny!);
        Assert.Equal(["github"], roundTrip.PluginPolicy!.Allow!);
        Assert.Equal(["agent-teams"], roundTrip.PluginPolicy.Deny!);
        Assert.Equal(["code-review"], roundTrip.SkillsPolicy!.Preload!);
        Assert.False(roundTrip.SkillsPolicy?.AllowManage);
        Assert.Equal("keep", roundTrip.TeamsPolicy?.ReservedTools);
    }
}
