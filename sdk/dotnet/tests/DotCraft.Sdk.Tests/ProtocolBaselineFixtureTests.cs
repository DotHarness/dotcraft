using System.Text.Json;
using DotCraft.Sdk.Wire;

namespace DotCraft.Sdk.Tests;

public sealed class ProtocolBaselineFixtureTests
{
    [Fact]
    public async Task WireClient_PreservesUnknownNotificationFixture()
    {
        using var fixtures = ReadFixtures();
        var message = FindCase(fixtures.RootElement, "unknown-notification")
            .GetProperty("messages")[0]
            .Clone();

        await using var transport = new TestJsonRpcTransport();
        await using var client = new DotCraftWireClient(transport);
        client.Start();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var notifications = client.ReadNotificationsAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);
        await transport.PushInboundAsync(message);

        Assert.True(await notifications.MoveNextAsync());
        Assert.Equal("fixture/unknownNotification", notifications.Current.Method);
        Assert.True(notifications.Current.Params.GetProperty("preserveMe").GetBoolean());
        Assert.Equal("two", notifications.Current.Params.GetProperty("future").GetProperty("nested")[1].GetString());
    }

    [Fact]
    public void Fixture_KeepsOpaqueMcpFieldsAvailableToRawSdkConsumers()
    {
        using var fixtures = ReadFixtures();
        var result = FindCase(fixtures.RootElement, "opaque-mcp-result")
            .GetProperty("messages")[1]
            .GetProperty("result");

        Assert.Equal("kept", result.GetProperty("content")[0].GetProperty("futureContentField").GetString());
        Assert.True(result.GetProperty("futureResultField").GetProperty("kept").GetBoolean());
        Assert.Equal("fixture", result.GetProperty("_meta").GetProperty("source").GetString());
    }

    private static JsonElement FindCase(JsonElement root, string name) =>
        root.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);

    private static JsonDocument ReadFixtures()
    {
        using var stream = typeof(ProtocolBaselineFixtureTests).Assembly
            .GetManifestResourceStream("DotCraft.Sdk.Tests.AppServerMessagesV1.json")
            ?? throw new InvalidOperationException("Missing AppServer fixture resource.");
        return JsonDocument.Parse(stream);
    }
}
