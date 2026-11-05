using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

/// <summary>
/// Wire carrier used for thread context items on the current protocol.
/// </summary>
internal enum ThreadContextCarrier
{
    /// <summary>Protocols with a first-class developer role, such as <c>openai-responses</c>.</summary>
    DeveloperMessage,

    /// <summary>Protocols without a developer role: a user message wrapped in a runtime reminder.</summary>
    UserReminder
}

/// <summary>
/// Model-visible context specific to a thread, kept in conversation history so a SubAgent's
/// instruction channel stays byte-identical to its parent's.
/// </summary>
internal static class ThreadContextItems
{
    public const string KindMetadataKey = "dotcraft.thread_context_item";
    public const string SubAgentRoleKind = "subagent_role";

    private const string ReminderOpenTag = "<system-reminder>";
    private const string ReminderCloseTag = "</system-reminder>";

    private static readonly ChatRole DeveloperRole = new("developer");

    /// <summary>
    /// Builds a marked context item for the given carrier.
    /// </summary>
    public static ChatMessage Create(ThreadContextCarrier carrier, string kind, string text)
    {
        var role = carrier == ThreadContextCarrier.DeveloperMessage ? DeveloperRole : ChatRole.User;
        var content = carrier == ThreadContextCarrier.DeveloperMessage
            ? text
            : $"{ReminderOpenTag}\n{text}\n{ReminderCloseTag}";
        return new ChatMessage(role, content)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [KindMetadataKey] = kind
            }
        };
    }

    /// <summary>
    /// Reads the item kind marker, which lives in metadata rather than model-visible text.
    /// </summary>
    public static string? GetKind(ChatMessage message)
    {
        if (message.AdditionalProperties == null
            || !message.AdditionalProperties.TryGetValue(KindMetadataKey, out var value))
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element
                => element.GetString(),
            _ => null
        };
    }

    public static bool IsKind(ChatMessage message, string kind) =>
        string.Equals(GetKind(message), kind, StringComparison.Ordinal);

    /// <summary>
    /// Returns the payload of a context item without its carrier wrapper.
    /// </summary>
    public static string ReadText(ChatMessage message)
    {
        var text = message.Text ?? string.Empty;
        var start = text.IndexOf(ReminderOpenTag, StringComparison.Ordinal);
        if (start < 0)
            return text.Trim();

        var contentStart = start + ReminderOpenTag.Length;
        var end = text.IndexOf(ReminderCloseTag, contentStart, StringComparison.Ordinal);
        return end < 0
            ? text[contentStart..].Trim()
            : text[contentStart..end].Trim();
    }

    /// <summary>
    /// Finds the most recent item of a kind, which is the value currently in effect.
    /// </summary>
    public static ChatMessage? FindLast(IList<ChatMessage> history, string kind)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (IsKind(history[i], kind))
                return history[i];
        }

        return null;
    }

    /// <summary>
    /// Resolves the carrier for a protocol. Protocols without a developer role fall back to a
    /// user message wrapped in a runtime reminder.
    /// </summary>
    public static ThreadContextCarrier ResolveCarrier(bool isOpenAIResponses) =>
        isOpenAIResponses ? ThreadContextCarrier.DeveloperMessage : ThreadContextCarrier.UserReminder;
}
