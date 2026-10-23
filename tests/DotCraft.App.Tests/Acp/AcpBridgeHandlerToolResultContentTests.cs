using DotCraft.Acp;
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

        Assert.Equal(expected, content?[0].Text);
    }
}
