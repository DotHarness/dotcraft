using DotCraft.Oratorio.Services;

namespace DotCraft.Oratorio.Tests;

public sealed class TaskShortIdAllocatorTests
{
    [Theory]
    [InlineData("default", "DEF")]
    [InlineData("oratorio", "ORA")]
    [InlineData("---", "WS")]
    [InlineData("acme-bot-1", "ACM")]
    public void DerivePrefix_UsesWorkspaceIdCharacters(string workspaceId, string expected)
    {
        Assert.Equal(expected, TaskShortIdAllocator.DerivePrefix(workspaceId));
    }
}
