using DotCraft.Dreams;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadVisibilityTests
{
    [Theory]
    [InlineData("dotcraft-desktop", WelcomeSuggestionConstants.InternalMetadataValue, true)]
    [InlineData("dotcraft-desktop", CommitMessageSuggestConstants.InternalMetadataValue, true)]
    [InlineData(WelcomeSuggestionConstants.ChannelName, null, true)]
    [InlineData(DreamsConstants.ChannelName, null, true)]
    [InlineData(CommitMessageSuggestConstants.ChannelName, null, true)]
    [InlineData("dotcraft-desktop", null, false)]
    [InlineData("cli", null, false)]
    public void IsInternal_Summary_UsesInternalMarkerOrKnownInternalOrigin(string originChannel, string? marker, bool expected)
    {
        var summary = new ThreadSummary
        {
            Id = "thread_1",
            WorkspacePath = "F:\\dotcraft",
            OriginChannel = originChannel,
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };
        if (marker != null)
            summary.Metadata[ThreadVisibility.InternalMetadataKey] = marker;

        Assert.Equal(expected, ThreadVisibility.IsInternal(summary));
    }

    [Fact]
    public void IsInternal_SessionThread_HonorsMetadataAndAgentBuilderTargetAndProjectsItToTheSummary()
    {
        var helper = new SessionThread { Id = "thread_internal", OriginChannel = "dotcraft-desktop" };
        helper.Metadata[ThreadVisibility.InternalMetadataKey] = "background-helper";
        var builder = new SessionThread
        {
            Id = "thread_builder",
            OriginChannel = "dotcraft-desktop",
            Configuration = new ThreadConfiguration { AgentBuilderTargetId = "release-notes-writer" }
        };

        Assert.True(ThreadVisibility.IsInternal(helper));
        Assert.True(ThreadVisibility.IsInternal(builder));
        var summary = ThreadSummary.FromThread(builder);
        Assert.Equal(ThreadVisibility.AgentBuilderInternalValue, summary.Metadata[ThreadVisibility.InternalMetadataKey]);
        Assert.True(ThreadVisibility.IsInternal(summary));
    }
}
