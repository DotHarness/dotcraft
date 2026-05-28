using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Abstractions;
using DotCraft.Protocol;

namespace DotCraft.Tests.Protocol;

public class ToolProfileAndApprovalPolicyTests
{
    [Fact]
    public void ToolProfileRegistry_RegisterTryGet_IsCaseInsensitive()
    {
        var registry = new ToolProfileRegistry();
        var providers = (IReadOnlyList<IAgentToolProvider>)Array.Empty<IAgentToolProvider>();
        registry.Register("local-task", providers);

        Assert.True(registry.TryGet("local-task", out var p1));
        Assert.Same(providers, p1);

        Assert.True(registry.TryGet("LOCAL-TASK", out var p2));
        Assert.Same(providers, p2);

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
}
