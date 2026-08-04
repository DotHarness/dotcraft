using DotCraft.Acp;
using Xunit;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerToolResultContentTests
{
    [Fact]
    public void BuildToolResultContent_TodoSuccess_ReturnsNull()
    {
        Assert.Null(AcpBridgeHandler.BuildToolResultContent("TodoWrite", "Plan updated", success: true));
        Assert.Null(AcpBridgeHandler.BuildToolResultContent("UpdateTodos", "Plan updated", success: true));
    }

    [Fact]
    public void BuildToolResultContent_TodoFailure_KeepsResultPreview()
    {
        var content = AcpBridgeHandler.BuildToolResultContent("TodoWrite", "Failed to update plan", success: false);

        Assert.NotNull(content);
        Assert.Equal("Failed to update plan", content![0].Text);
    }

    [Fact]
    public void BuildToolResultContent_OtherToolSuccess_KeepsResultPreview()
    {
        var content = AcpBridgeHandler.BuildToolResultContent("ReadFile", "file content", success: true);

        Assert.NotNull(content);
        Assert.Equal("file content", content![0].Text);
    }
}
