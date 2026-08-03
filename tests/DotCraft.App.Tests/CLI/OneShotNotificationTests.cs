using System.Text.Json;
using DotCraft.CLI;
using Xunit;

namespace DotCraft.Tests.CLI;

public sealed class OneShotNotificationTests
{
    [Theory]
    [MemberData(nameof(NotificationCases))]
    public void From_AppServerNotification_ReturnsOneShotNotification(
        string json,
        string expectedKind,
        string expectedText,
        bool exactText)
    {
        using var doc = JsonDocument.Parse(json);

        var notification = OneShotNotification.From(doc);

        Assert.Equal(expectedKind, notification.Kind.ToString());
        if (exactText)
            Assert.Equal(expectedText, notification.Text);
        else
            Assert.Contains(expectedText, notification.Text, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> NotificationCases()
    {
        yield return
        [
            """
            {"jsonrpc":"2.0","method":"item/agentMessage/delta","params":{"threadId":"thread_1","delta":"hello"}}
            """,
            nameof(OneShotNotificationKind.AgentDelta),
            "hello",
            true
        ];
        yield return
        [
            """
            {"jsonrpc":"2.0","method":"turn/failed","params":{"threadId":"thread_1","error":"boom"}}
            """,
            nameof(OneShotNotificationKind.Failed),
            "boom",
            true
        ];
        yield return
        [
            """
            {"jsonrpc":"2.0","method":"item/completed","params":{"threadId":"thread_1","item":{"type":"agentMessage","text":"final answer"}}}
            """,
            nameof(OneShotNotificationKind.AgentCompleted),
            "final answer",
            true
        ];
        yield return
        [
            """
            {"jsonrpc":"2.0","method":"item/started","params":{"threadId":"thread_1","item":{"type":"toolCall","payload":{"name":"Shell"}}}}
            """,
            nameof(OneShotNotificationKind.Progress),
            "Shell",
            false
        ];
    }
}
