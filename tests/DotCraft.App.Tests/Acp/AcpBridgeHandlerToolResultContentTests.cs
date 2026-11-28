using DotCraft.Acp;
using DotHarness.Acp.V1;
using Xunit;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerToolResultContentTests
{
    [Theory]
    [InlineData("TodoWrite", "Plan updated", true, null)]
    [InlineData("TodoWrite", "Failed to update plan", false, "Failed to update plan")]
    [InlineData("ReadFile", "file content", true, "file content")]
    public void BuildToolResultContent_HidesOnlySuccessfulPlanUpdates(string tool, string result, bool success, string? expected)
    {
        var content = AcpBridgeHandler.BuildToolResultContent(tool, result, success);

        if (expected == null)
        {
            Assert.Null(content);
            return;
        }

        Assert.NotNull(content);
        var wrapped = Assert.IsType<ToolCallContent.Content>(Assert.Single(content!));
        var text = Assert.IsType<ContentBlock.Text>(wrapped.Value.ContentValue);
        Assert.Equal(expected, text.Value.Text);
    }
}
