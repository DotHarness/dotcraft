using DotCraft.Context;
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
/// Model-visible context that depends on the running thread or on an attached client connection.
/// These items live in conversation history instead of the generated base instructions so a
/// SubAgent's instruction channel stays byte-identical to its parent's, and so a client rebinding
/// cannot invalidate a thread's cached prompt prefix.
/// </summary>
internal static class ThreadContextItems
{
    public const string KindMetadataKey = "dotcraft.thread_context_item";
    public const string SubAgentRoleKind = "subagent_role";
    public const string ClientContextKind = "client_context";

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

    /// <summary>
    /// Appends an updated client context item when the resolved content changed. Previously sent
    /// items are never rewritten, so an appended update cannot invalidate the cached prefix.
    /// </summary>
    public static bool ReconcileClientContext(
        IList<ChatMessage> history,
        IReadOnlyList<IThreadSystemPromptContextProvider> providers,
        ThreadSystemPromptContext context,
        ThreadContextCarrier carrier)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(providers);

        var desired = BuildClientContext(providers, context);
        var previous = FindLast(history, ClientContextKind);
        var previousText = previous == null ? string.Empty : ReadText(previous);
        if (string.Equals(desired, previousText, StringComparison.Ordinal))
            return false;

        if (desired.Length == 0 && previous == null)
            return false;

        var text = desired.Length > 0 ? desired : ClearedClientContext;
        history.Add(Create(carrier, ClientContextKind, text));
        return true;
    }

    private const string ClearedClientContext =
        "## Client Runtime Context\n\nThe client runtime context described earlier is no longer active.";

    private static string BuildClientContext(
        IReadOnlyList<IThreadSystemPromptContextProvider> providers,
        ThreadSystemPromptContext context)
    {
        var sections = providers
            .Where(static provider => provider.Placement == ThreadPromptPlacement.ThreadContextItem)
            .OrderBy(static provider => provider.ContextPageKey.Scope, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ContextPageKey.Name, StringComparer.Ordinal)
            .Select(provider => provider.GetSystemPromptSection(context))
            .Where(static section => !string.IsNullOrWhiteSpace(section))
            .Select(static section => section!.Trim())
            .ToArray();

        return sections.Length == 0
            ? string.Empty
            : string.Join("\n\n", sections);
    }
}
