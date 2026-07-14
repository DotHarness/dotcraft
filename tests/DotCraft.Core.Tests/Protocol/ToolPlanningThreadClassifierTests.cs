using DotCraft.Protocol;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ToolPlanningThreadClassifierTests
{
    [Theory]
    [InlineData("cli")]
    [InlineData("dotcraft-desktop")]
    [InlineData("acp")]
    [InlineData("telegram")]
    public void Classify_UserConversation_ReturnsUserTopLevel(string originChannel)
    {
        var thread = CreateThread(originChannel);

        Assert.Equal(ToolPlanningThreadKind.UserTopLevel, ToolPlanningThreadClassifier.Classify(thread));
    }

    [Fact]
    public void Classify_OrdinarySiblingOrFork_RemainsUserTopLevel()
    {
        var sibling = CreateThread("dotcraft-desktop");
        sibling.Source = ThreadSource.SpawnedFromThread("thread_parent");
        var fork = CreateThread("dotcraft-desktop");
        fork.ForkedFromId = "thread_parent";

        Assert.Equal(ToolPlanningThreadKind.UserTopLevel, ToolPlanningThreadClassifier.Classify(sibling));
        Assert.Equal(ToolPlanningThreadKind.UserTopLevel, ToolPlanningThreadClassifier.Classify(fork));
    }

    [Fact]
    public void Classify_TeamsMission_ReturnsModuleManaged()
    {
        var thread = CreateThread("teams");

        Assert.Equal(ToolPlanningThreadKind.ModuleManaged, ToolPlanningThreadClassifier.Classify(thread));
    }

    [Fact]
    public void Classify_SubAgentSourceOrOrigin_ReturnsSubAgentChild()
    {
        var sourceThread = CreateThread("dotcraft-desktop");
        sourceThread.Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            ParentThreadId = "parent",
            RootThreadId = "parent"
        });
        var originThread = CreateThread(SubAgentThreadOrigin.ChannelName);

        Assert.Equal(ToolPlanningThreadKind.SubAgentChild, ToolPlanningThreadClassifier.Classify(sourceThread));
        Assert.Equal(ToolPlanningThreadKind.SubAgentChild, ToolPlanningThreadClassifier.Classify(originThread));
    }

    [Theory]
    [InlineData("automations")]
    [InlineData("cron")]
    [InlineData("heartbeat")]
    public void Classify_UnattendedOrigin_ReturnsUnattended(string originChannel)
    {
        var thread = CreateThread(originChannel);

        Assert.Equal(ToolPlanningThreadKind.Unattended, ToolPlanningThreadClassifier.Classify(thread));
    }

    [Fact]
    public void Classify_AutomationConfiguration_ReturnsUnattended()
    {
        var thread = CreateThread("dotcraft-desktop");
        thread.Configuration = new ThreadConfiguration { AutomationTaskDirectory = "tasks/release" };

        Assert.Equal(ToolPlanningThreadKind.Unattended, ToolPlanningThreadClassifier.Classify(thread));
    }

    [Fact]
    public void Classify_InternalAndEphemeralThreads_ReturnInternal()
    {
        var metadataThread = CreateThread("dotcraft-desktop");
        metadataThread.Metadata[ThreadVisibility.InternalMetadataKey] = "helper";
        var builderThread = CreateThread("dotcraft-desktop");
        builderThread.Configuration = new ThreadConfiguration { AgentBuilderTargetId = "builder-target" };
        var ephemeralThread = CreateThread("dotcraft-desktop");
        ephemeralThread.Ephemeral = true;

        Assert.Equal(ToolPlanningThreadKind.Internal, ToolPlanningThreadClassifier.Classify(metadataThread));
        Assert.Equal(ToolPlanningThreadKind.Internal, ToolPlanningThreadClassifier.Classify(builderThread));
        Assert.Equal(ToolPlanningThreadKind.Internal, ToolPlanningThreadClassifier.Classify(ephemeralThread));
    }

    [Fact]
    public void Classify_UnknownOrBlankSource_FailsClosed()
    {
        var unknownSource = CreateThread("dotcraft-desktop");
        unknownSource.Source = new ThreadSource { Kind = "future-source" };
        var blankOrigin = CreateThread(string.Empty);

        Assert.Equal(ToolPlanningThreadKind.Unknown, ToolPlanningThreadClassifier.Classify(unknownSource));
        Assert.Equal(ToolPlanningThreadKind.Unknown, ToolPlanningThreadClassifier.Classify(blankOrigin));
    }

    private static SessionThread CreateThread(string originChannel) => new()
    {
        Id = "thread_1",
        WorkspacePath = "workspace",
        OriginChannel = originChannel,
        Source = ThreadSource.User(),
        Configuration = new ThreadConfiguration(),
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow
    };
}
