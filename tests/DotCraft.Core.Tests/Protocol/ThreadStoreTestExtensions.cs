using DotCraft.Protocol;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;
using Xunit;

namespace DotCraft.Protocol;

internal static class ThreadStoreTestExtensions
{
    public static bool TryGetInMemoryChatHistory(
        this List<ChatMessage> messages,
        out List<ChatMessage> history,
        string? _ = null,
        System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null)
    {
        history = messages;
        return true;
    }

    public static void SetInMemoryChatHistory(
        this List<ChatMessage> messages,
        IEnumerable<ChatMessage> history,
        string? _ = null,
        System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null)
    {
        messages.Clear();
        messages.AddRange(history);
    }

    public static Task<List<ChatMessage>> LoadOrCreateSessionAsync(
        this ThreadStore store,
        object _,
        string threadId,
        CancellationToken cancellationToken = default) =>
        store.LoadModelHistoryAsync(threadId, cancellationToken);

    public static Task<List<ChatMessage>> LoadOrCreateSessionAsync(
        this ThreadStore store,
        object _,
        SessionThread thread,
        string? excludedTurnId = null,
        CancellationToken cancellationToken = default) =>
        store.LoadModelHistoryAsync(thread, excludedTurnId, cancellationToken);
}
