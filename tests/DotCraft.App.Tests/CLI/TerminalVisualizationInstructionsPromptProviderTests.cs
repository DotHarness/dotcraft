using DotCraft.Context;
using DotCraft.CLI;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.App.Tests.CLI;

public sealed class TerminalVisualizationInstructionsPromptProviderTests
{
    [Fact]
    public void Instructions_AreDisabledByDefault()
    {
        var provider = new TerminalVisualizationInstructionsPromptProvider(new AppConfig());

        Assert.Null(provider.GetSystemPromptSection(new ThreadSystemPromptContext("thread", ".", "cli")));
    }

    [Fact]
    public void Instructions_ApplyOnlyToCliThreadsWhenEnabled()
    {
        var config = new AppConfig();
        config.GetSection<CliConfig>("CLI").TerminalVisualizationInstructions = true;
        var provider = new TerminalVisualizationInstructionsPromptProvider(config);

        var cli = provider.GetSystemPromptSection(new ThreadSystemPromptContext("thread", ".", "cli"));

        Assert.NotNull(cli);
        Assert.Null(provider.GetSystemPromptSection(new ThreadSystemPromptContext("thread", ".", "desktop")));
    }
}
