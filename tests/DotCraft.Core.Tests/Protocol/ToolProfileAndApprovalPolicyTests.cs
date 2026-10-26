using System.Text.Json;
using DotCraft.Agents;
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

    [Theory]
    [InlineData(ApprovalPolicy.AutoApprove, "autoApprove")]
    [InlineData(ApprovalPolicy.Prompt, "prompt")]
    public void ThreadConfiguration_SerializesApprovalPolicyAsWireStrings(ApprovalPolicy policy, string wire)
    {
        var cfg = new ThreadConfiguration
        {
            ApprovalPolicy = policy,
            ToolProfile = "local-task",
            RequireApprovalOutsideWorkspace = false
        };
        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains($"\"approvalPolicy\":\"{wire}\"", json);
        Assert.Contains("\"requireApprovalOutsideWorkspace\":false", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal(policy, roundTrip.ApprovalPolicy);
        Assert.Equal("local-task", roundTrip.ToolProfile);
        Assert.False(roundTrip.RequireApprovalOutsideWorkspace);
    }

    [Fact]
    public void ThreadConfiguration_RoundTripsAgentProfilePolicyFields()
    {
        var cfg = new ThreadConfiguration
        {
            AgentProfileId = "reviewer",
            AgentProfileSource = "workspace",
            AgentProfileFingerprint = "sha256:abc",
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile", "GrepFiles"],
                Deny = ["WriteFile"],
                AgentControl = "allowList",
                AllowedAgentControlTools = ["ListAgents"]
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
                Deny = ["sample-plugin"]
            },
            SkillsPolicy = new ThreadSkillsPolicy
            {
                Allow = ["code-review", "repo-style"],
                Deny = ["dangerous-skill"]
            }
        };

        var json = JsonSerializer.Serialize(cfg, SessionJsonOptions.Default);
        Assert.Contains("\"agentProfileId\":\"reviewer\"", json);
        Assert.Contains("\"toolPolicy\"", json);
        Assert.Contains("\"mcpPolicy\"", json);
        Assert.Contains("\"skillsPolicy\"", json);

        var roundTrip = JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
        Assert.NotNull(roundTrip);
        Assert.Equal("reviewer", roundTrip!.AgentProfileId);
        Assert.Equal("workspace", roundTrip.AgentProfileSource);
        Assert.Equal("sha256:abc", roundTrip.AgentProfileFingerprint);
        Assert.Equal(["ReadFile", "GrepFiles"], roundTrip.ToolPolicy!.Allow!);
        Assert.Equal(["WriteFile"], roundTrip.ToolPolicy.Deny!);
        Assert.Equal("allowList", roundTrip.ToolPolicy.AgentControl);
        Assert.Equal(["ListAgents"], roundTrip.ToolPolicy.AllowedAgentControlTools!);
        Assert.Equal(["github-readonly"], roundTrip.McpPolicy!.Servers!);
        Assert.Equal(["mcp__github-readonly__get_*"], roundTrip.McpPolicy.Tools!.Allow!);
        Assert.Equal(["*write*"], roundTrip.McpPolicy.Tools.Deny!);
        Assert.Equal(["github"], roundTrip.PluginPolicy!.Allow!);
        Assert.Equal(["sample-plugin"], roundTrip.PluginPolicy.Deny!);
        Assert.Equal(["code-review", "repo-style"], roundTrip.SkillsPolicy!.Allow!);
    }
}
