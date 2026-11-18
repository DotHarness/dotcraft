using System.Text.Json;
using DotCraft.Sdk;
using Xunit;

namespace DotCraft.CodexGateway.Tests;

public sealed partial class RealWorkspaceGatewayTests
{
    [Fact]
    public async Task PhoneReadsRealQueueWithoutResumingAndRefreshesAfterDesktopChanges()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        await using var desktop = await fixture.A.ConnectAsync(null, ct);
        var source = await CreateSourceThreadAsync(desktop, fixture.A.Root, "queued conversation", ct);
        await using var phone = new RealGatewayPeer(fixture.State, fixture.A);
        await InitializeAsync(phone, ct);
        var listed = await phone.RequestAsync("thread/list", new { }, ct);
        var threadId = Assert.Single(listed.GetProperty("data").EnumerateArray()).GetProperty("id").GetString()!;

        fixture.A.Requests.Clear();
        var empty = await phone.RequestAsync("thread/queue/list", new { threadId, cursor = (string?)null }, ct);
        Assert.Empty(empty.GetProperty("data").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("nextCursor").ValueKind);

        var firstSourceId = await EnqueueAsync(desktop, source, "first queued message", "desktop-message", ct);
        var secondSourceId = await EnqueueAsync(desktop, source, "second queued message", null, ct);
        var first = await phone.RequestAsync("thread/queue/list", new { threadId, limit = 0 }, ct);
        var firstSubmission = Assert.Single(first.GetProperty("data").EnumerateArray());
        Assert.Equal("first queued message", firstSubmission.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.Equal("desktop-message", firstSubmission.GetProperty("clientUserMessageId").GetString());
        Assert.Equal(JsonValueKind.Null, firstSubmission.GetProperty("responsesapiClientMetadata").ValueKind);

        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);
        var second = await phone.RequestAsync("thread/queue/list", new { threadId, cursor, limit = 1 }, ct);
        var secondSubmission = Assert.Single(second.GetProperty("data").EnumerateArray());
        var secondId = secondSubmission.GetProperty("id").GetString();
        Assert.Equal("second queued message", secondSubmission.GetProperty("input")[0].GetProperty("text").GetString());
        Assert.Equal(secondId, secondSubmission.GetProperty("clientUserMessageId").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
        Assert.DoesNotContain(fixture.A.Requests, entry =>
            entry.Method is "thread/resume" or "thread/items/list" or "thread/turns/list" or "turn/start");

        await phone.RequestAsync("thread/resume", new { threadId, excludeTurns = true }, ct);
        await desktop.RequestRawAsync("turn/queue/remove", new { threadId = source, queuedInputId = firstSourceId }, ct);
        var changed = await phone.NextNotificationAsync("thread/queue/changed", threadId, ct);
        Assert.Equal("threadId", Assert.Single(changed.EnumerateObject()).Name);

        var refreshed = await phone.RequestAsync("thread/queue/list", new { threadId }, ct);
        Assert.Equal(secondId, Assert.Single(refreshed.GetProperty("data").EnumerateArray()).GetProperty("id").GetString());
        await desktop.RequestRawAsync("turn/queue/start", new { threadId = source, queuedInputId = secondSourceId }, ct);
        await phone.NextNotificationAsync("turn/completed", threadId, ct);
        var history = await phone.RequestAsync("thread/items/list", new { threadId }, ct);
        var user = Assert.Single(history.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("item")),
            item => item.GetProperty("type").GetString() == "userMessage");
        Assert.Equal(secondId, user.GetProperty("clientId").GetString());
    }

    [Fact]
    public async Task PhoneQueuePreservesClientIdWhenDesktopPromotesInputToGuidance()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var fixture = await Fixture.StartAsync(ct);
        await using var desktop = await fixture.A.ConnectAsync(null, ct);
        var source = await CreateSourceThreadAsync(desktop, fixture.A.Root, "running conversation", ct);
        await using var phone = new RealGatewayPeer(fixture.State, fixture.A);
        await InitializeAsync(phone, ct);
        var listed = await phone.RequestAsync("thread/list", new { }, ct);
        var threadId = Assert.Single(listed.GetProperty("data").EnumerateArray()).GetProperty("id").GetString()!;

        var started = await phone.RequestAsync("turn/start", new
        {
            threadId, input = new[] { new { type = "text", text = "question" } }
        }, ct);
        var externalTurnId = started.GetProperty("turn").GetProperty("id").GetString();
        var question = await phone.NextRequestAsync("item/tool/requestUserInput", ct);
        var running = await desktop.RequestRawAsync("thread/read", new { threadId = source }, ct);
        var turnId = running.GetProperty("thread").GetProperty("runtime").GetProperty("activeTurnId").GetString()!;
        await desktop.RequestRawAsync("turn/steer", new
        {
            threadId = source, expectedTurnId = turnId, clientUserMessageId = "pending-guidance",
            input = new[] { new { type = "text", text = "pending guidance" } }
        }, ct);
        var noticeId = await fixture.A.EnqueueBackgroundNoticeAsync(source, ct);
        const string queuedText = "future turn";
        var queuedInputId = await EnqueueAsync(desktop, source, queuedText, null, ct);
        var queue = await phone.RequestAsync("thread/queue/list", new { threadId }, ct);
        var submission = Assert.Single(queue.GetProperty("data").EnumerateArray());
        Assert.Equal(queuedText, submission.GetProperty("input")[0].GetProperty("text").GetString());
        var clientId = submission.GetProperty("clientUserMessageId").GetString();
        Assert.NotNull(clientId);

        await desktop.RequestRawAsync("turn/queue/remove", new { threadId = source, queuedInputId = noticeId }, ct);
        await desktop.RequestRawAsync("turn/queue/update", new
        {
            threadId = source, expectedTurnId = turnId, queuedInputId, status = "guidancePending"
        }, ct);
        await phone.AnswerAsync(question, new
        {
            answers = new Dictionary<string, object> { ["choice"] = new { answers = new[] { "B" } } }
        }, ct);
        await phone.NextNotificationAsync("turn/completed", threadId, ct);
        var history = await phone.RequestAsync("thread/items/list", new { threadId, turnId = externalTurnId }, ct);
        var promoted = Assert.Single(history.GetProperty("data").EnumerateArray().Select(row => row.GetProperty("item")),
            item => item.GetProperty("type").GetString() == "userMessage" &&
                item.GetProperty("content")[0].GetProperty("text").GetString() == queuedText);
        Assert.Equal(clientId, promoted.GetProperty("clientId").GetString());
    }

    private static async Task<string> EnqueueAsync(
        DotCraftClient desktop, string threadId, string text, string? clientUserMessageId, CancellationToken ct)
    {
        var result = await desktop.RequestRawAsync("turn/enqueue", new
        {
            threadId, input = new[] { new { type = "text", text } }, clientUserMessageId
        }, ct);
        return result.GetProperty("queuedInput").GetProperty("id").GetString()!;
    }
}
