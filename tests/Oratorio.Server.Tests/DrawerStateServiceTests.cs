using System.Text.Json;
using Oratorio.Server.Api;
using Oratorio.Server.Realtime;

namespace Oratorio.Server.Tests;

public sealed class DrawerStateServiceTests
{
    [Fact]
    public void UpsertItem_ReplacesDefaultPayloadWithPlaceholder()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");

        service.UpsertItem(
            "run-1",
            new ConversationItemDto(
                "item-1",
                "turn-1",
                "agentMessage",
                "completed",
                default,
                ts,
                ts,
                Streaming: false),
            ts);

        var snapshot = service.GetSnapshot("run-1");
        var payload = snapshot.Items[0].Payload;
        Assert.Equal("agentMessage", payload.GetProperty("type").GetString());
        Assert.Equal(DrawerPayloadSanitizer.PayloadUnavailableCode, payload.GetProperty("serializationError").GetString());
        Assert.Equal(DrawerPayloadSanitizer.PayloadUnavailableMessage, payload.GetProperty("message").GetString());
    }

    [Fact]
    public void UpsertItem_ReplacesDisposedPayloadWithPlaceholder()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");
        var document = JsonDocument.Parse("""{"text":"unavailable"}""");
        var disposedPayload = document.RootElement;
        document.Dispose();

        service.UpsertItem(
            "run-1",
            new ConversationItemDto(
                "item-1",
                "turn-1",
                "toolCall",
                "completed",
                disposedPayload,
                ts,
                ts,
                Streaming: false),
            ts);

        var snapshot = service.GetSnapshot("run-1");
        var payload = snapshot.Items[0].Payload;
        Assert.Equal("toolCall", payload.GetProperty("type").GetString());
        Assert.Equal(DrawerPayloadSanitizer.PayloadUnavailableCode, payload.GetProperty("serializationError").GetString());
    }

    [Fact]
    public void UpsertItem_PreservesSerializablePayload()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");

        service.UpsertItem(
            "run-1",
            new ConversationItemDto(
                "item-1",
                "turn-1",
                "agentMessage",
                "completed",
                JsonSerializer.SerializeToElement(new { text = "Hello" }),
                ts,
                ts,
                Streaming: false),
            ts);

        var snapshot = service.GetSnapshot("run-1");
        Assert.Equal("Hello", snapshot.Items[0].Payload.GetProperty("text").GetString());
    }

    [Fact]
    public void AppendDelta_MergesAgentMessageText()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");

        service.AppendDelta("run-1", "item-1", "turn-1", "agentMessage", "agentMessage", "Hello ", ts);
        service.AppendDelta("run-1", "item-1", "turn-1", "agentMessage", "agentMessage", "world", ts.AddSeconds(1));

        var snapshot = service.GetSnapshot("run-1");
        Assert.True(snapshot.FromCache);
        Assert.Single(snapshot.Items);
        Assert.Equal("Hello world", snapshot.Items[0].Payload.GetProperty("text").GetString());
    }

    [Fact]
    public void AppendDelta_DoesNotNestRawPayloads()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");

        for (var i = 0; i < 100; i++)
        {
            service.AppendDelta("run-1", "item-1", "turn-1", "reasoningContent", "reasoningContent", "x", ts.AddMilliseconds(i));
        }

        var snapshot = service.GetSnapshot("run-1");
        var payload = snapshot.Items[0].Payload;
        Assert.Equal(new string('x', 100), payload.GetProperty("text").GetString());
        Assert.False(payload.TryGetProperty("raw", out _));

        var serialized = JsonSerializer.SerializeToElement(snapshot.Items[0], new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(new string('x', 100), serialized.GetProperty("payload").GetProperty("text").GetString());
    }

    [Fact]
    public void MergeDeltaPayload_FallsBackWhenExistingPayloadIsDisposed()
    {
        var document = JsonDocument.Parse("""{"text":"old"}""");
        var disposedPayload = document.RootElement;
        document.Dispose();

        var merged = DrawerPayloadSanitizer.MergeDeltaPayload(
            "agentMessage",
            "agentMessage",
            disposedPayload,
            "fresh",
            out var error);

        Assert.NotNull(error);
        Assert.Equal("agentMessage", merged.GetProperty("type").GetString());
        Assert.Equal("fresh", merged.GetProperty("text").GetString());
        Assert.False(merged.TryGetProperty("raw", out _));
    }

    [Fact]
    public void ReplacePlan_StoresLatestSnapshot()
    {
        var service = new DrawerStateService();

        service.ReplacePlan(
            "run-1",
            new PlanSnapshotDto("Plan", "Overview", "Body", [new PlanTodoDto("todo", "Do it", "high", "pending")]),
            DateTimeOffset.Parse("2026-05-09T00:00:00Z"));

        var snapshot = service.GetSnapshot("run-1");
        Assert.Equal("Plan", snapshot.Plan?.Title);
        Assert.Single(snapshot.Plan?.Todos ?? []);
    }

    [Fact]
    public void UpsertItem_TrimsToRecentConversationWindow()
    {
        var service = new DrawerStateService();
        var ts = DateTimeOffset.Parse("2026-05-09T00:00:00Z");
        for (var i = 0; i < 205; i++)
        {
            service.UpsertItem(
                "run-1",
                new ConversationItemDto(
                    $"item-{i}",
                    "turn-1",
                    "agentMessage",
                    "completed",
                    JsonSerializer.SerializeToElement(new { text = i.ToString() }),
                    ts.AddSeconds(i),
                    ts.AddSeconds(i),
                    Streaming: false),
                ts.AddSeconds(i));
        }

        var snapshot = service.GetSnapshot("run-1");
        Assert.Equal(200, snapshot.Items.Count);
        Assert.Equal("item-5", snapshot.Items[0].Id);
    }
}
