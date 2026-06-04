using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class AgentPathTests
{
    [Theory]
    [InlineData("/root")]
    [InlineData("/root/worker")]
    [InlineData("/root/worker_review_1")]
    [InlineData("/root/worker/phase_2")]
    public void Parse_AcceptsValidAbsolutePaths(string value)
    {
        var path = AgentPath.Parse(value);

        Assert.Equal(value, path.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("root/worker")]
    [InlineData("/worker")]
    [InlineData("/root/")]
    [InlineData("/morpheus")]
    [InlineData("/root/Worker")]
    [InlineData("/root/worker.review")]
    [InlineData("/root/worker-review")]
    [InlineData("/root/has space")]
    [InlineData("/root/../worker")]
    [InlineData("/root/root")]
    [InlineData("/root/.")]
    public void Parse_RejectsInvalidPaths(string value)
    {
        Assert.Throws<ArgumentException>(() => AgentPath.Parse(value));
    }

    [Theory]
    [InlineData("/root", "worker", "/root/worker")]
    [InlineData("/root/worker", "review", "/root/worker/review")]
    [InlineData("/root/worker", "review/phase_2", "/root/worker/review/phase_2")]
    [InlineData("/root/worker", "/root/review", "/root/review")]
    public void Resolve_HandlesRelativeAndAbsoluteTargets(string current, string target, string expected)
    {
        var resolved = AgentPath.Parse(current).Resolve(target);

        Assert.Equal(expected, resolved.Value);
    }

    [Fact]
    public void Resolve_RejectsDotAndParentNavigation()
    {
        Assert.Throws<ArgumentException>(() => AgentPath.RootPath.Resolve("."));
        Assert.Throws<ArgumentException>(() => AgentPath.RootPath.Resolve(".."));
        Assert.Throws<ArgumentException>(() => AgentPath.Parse("/root/worker").Resolve("../review"));
    }
}
