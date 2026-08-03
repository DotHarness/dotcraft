using DotCraft.Tools;
using DotCraft.Tools.Sandbox;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class SandboxFileToolsReadTests
{
    [Fact]
    public void ReadFile_LimitWithoutOffset_UsesHostPaginationSemantics()
    {
        var content = string.Join('\n', Enumerable.Range(1, 5).Select(i => $"line-{i}"));

        var sandboxResult = SandboxFileTools.FormatTextReadResult(content, offset: 0, limit: 2);
        var hostResult = TextFileReadLimiter.FormatInMemory(content, offset: 0, limit: 2);

        Assert.Equal(hostResult, sandboxResult);
        Assert.Contains("1: line-1", sandboxResult, StringComparison.Ordinal);
        Assert.Contains("2: line-2", sandboxResult, StringComparison.Ordinal);
        Assert.DoesNotContain("3: line-3", sandboxResult, StringComparison.Ordinal);
        Assert.Contains("Use offset=3 to read more", sandboxResult, StringComparison.Ordinal);
    }
}
