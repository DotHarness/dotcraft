using DotCraft.Dreams;
using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadVisibilityTests
{
    [Fact]
    public void IsInternal_WhenWelcomeSuggestionMetadataIsSet_ReturnsTrue()
    {
        var summary = CreateSummary();
        summary.Metadata[ThreadVisibility.InternalMetadataKey] = WelcomeSuggestionConstants.InternalMetadataValue;

        Assert.True(ThreadVisibility.IsInternal(summary));
    }

    [Fact]
    public void IsInternal_WhenCommitSuggestionMetadataIsSet_ReturnsTrue()
    {
        var summary = CreateSummary();
        summary.Metadata[ThreadVisibility.InternalMetadataKey] = CommitMessageSuggestConstants.InternalMetadataValue;

        Assert.True(ThreadVisibility.IsInternal(summary));
    }

    [Theory]
    [InlineData(WelcomeSuggestionConstants.ChannelName)]
    [InlineData(DreamsConstants.ChannelName)]
    [InlineData(CommitMessageSuggestConstants.ChannelName)]
    public void IsInternal_WhenKnownInternalOriginIsSet_ReturnsTrue(string originChannel)
    {
        var summary = CreateSummary(originChannel);

        Assert.True(ThreadVisibility.IsInternal(summary));
    }

    [Theory]
    [InlineData("dotcraft-desktop")]
    [InlineData("cli")]
    public void IsInternal_WhenUserVisibleOriginHasNoMarker_ReturnsFalse(string originChannel)
    {
        var summary = CreateSummary(originChannel);

        Assert.False(ThreadVisibility.IsInternal(summary));
    }

    [Fact]
    public void IsInternal_WhenSessionThreadHasInternalMetadata_ReturnsTrue()
    {
        var thread = new SessionThread
        {
            Id = "thread_internal",
            OriginChannel = "dotcraft-desktop"
        };
        thread.Metadata[ThreadVisibility.InternalMetadataKey] = "background-helper";

        Assert.True(ThreadVisibility.IsInternal(thread));
    }

    private static ThreadSummary CreateSummary(string originChannel = "dotcraft-desktop") =>
        new()
        {
            Id = "thread_1",
            WorkspacePath = "F:\\dotcraft",
            OriginChannel = originChannel,
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };
}
