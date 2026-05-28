using DotCraft.Sdk.AppBinding;

namespace DotCraft.Sdk.Tests;

public sealed class AppBindingTests
{
    [Fact]
    public void Handoff_Parse_ReadsConnectionFields()
    {
        var handoff = AppBindingHandoff.Parse(
            "oratorio://dotcraft/bind?app=com.dotharness.oratorio&request=bind_req_1&token=tok&endpoint=ws%3A%2F%2F127.0.0.1%3A9100%2Fws",
            expectedScheme: "oratorio",
            expectedAppId: "com.dotharness.oratorio");

        Assert.Equal("bind", handoff.Operation);
        Assert.Equal("bind_req_1", handoff.RequestId);
        Assert.Equal("tok", handoff.RequestToken);
        Assert.Equal("ws://127.0.0.1:9100/ws", handoff.AppServerUrl);
    }

    [Fact]
    public void ToolError_UsesStandardShape()
    {
        var result = DotCraftAppBindingClient.ToolError(AppBindingErrorCodes.Offline, "App is offline.");

        Assert.False(result.Success);
        Assert.Equal(AppBindingErrorCodes.Offline, result.ErrorCode);
        Assert.Contains(AppBindingErrorCodes.Offline, result.ContentItems![0].Text);
    }
}
