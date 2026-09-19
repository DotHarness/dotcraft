using System.Text;

namespace DotCraft.Sessions;

/// <summary>The kinds a persisted rollout record can declare. Readers resolve their literals from here.</summary>
public static class RolloutKinds
{
    public const string ThreadOpened = "thread_opened";
    public const string TurnStarted = "turn_started";
    public const string ItemAppended = "item_appended";
    public const string TurnCompleted = "turn_completed";
    public const string ThreadStatusChanged = "thread_status_changed";
    public const string ThreadNameUpdated = "thread_name_updated";
    public const string ThreadRolledBack = "thread_rolled_back";
    public const string TurnStateReplaced = "turn_state_replaced";
    public const string QueuedInputAdded = "queued_input_added";
    public const string QueuedInputRemoved = "queued_input_removed";
    public const string QueuedInputUpdated = "queued_input_updated";
    public const string QueuedInputReordered = "queued_input_reordered";
    public const string ContextCompacted = "context_compacted";
    public const string ModelHistoryMessagesAppended = "model_history_messages_appended";
    public const string WorldState = "world_state";
    public const string ProviderHistoryItemsAppended = "provider_history_items_appended";
    public const string ProviderHistoryReplaced = "provider_history_replaced";
    public const string ProviderHistoryAttemptAborted = "provider_history_attempt_aborted";

    /// <summary>Kinds the thread projection folds into its snapshot.</summary>
    public static IReadOnlySet<string> Domain { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ThreadOpened,
        TurnStarted,
        ItemAppended,
        TurnCompleted,
        ThreadStatusChanged,
        ThreadNameUpdated,
        ThreadRolledBack,
        TurnStateReplaced,
        QueuedInputAdded,
        QueuedInputRemoved,
        QueuedInputUpdated,
        QueuedInputReordered
    };

    /// <summary>Every kind this build writes. A kind outside it is from another build.</summary>
    public static IReadOnlySet<string> All { get; } = Domain.Concat(
    [
        ContextCompacted,
        ModelHistoryMessagesAppended,
        WorldState,
        ProviderHistoryItemsAppended,
        ProviderHistoryReplaced,
        ProviderHistoryAttemptAborted
    ]).ToHashSet(StringComparer.Ordinal);

    /// <summary>False for a kind this build does not know, which the projection reports as an error.</summary>
    public static bool IsIgnoredByProjection(string kind) => All.Contains(kind) && !Domain.Contains(kind);

    /// <summary>
    /// Only the batch that carries a turn's exact model history can cost the turn that history when
    /// it cannot be read.
    /// </summary>
    public static bool RebuildsTurnHistory(string? kind) =>
        string.Equals(kind, ModelHistoryMessagesAppended, StringComparison.Ordinal);

    /// <summary>The record property carrying this kind's payload, spelled as the serializer writes it.</summary>
    public static string PayloadProperty(string kind)
    {
        var name = new StringBuilder(kind.Length);
        var upperNext = false;
        foreach (var character in kind)
        {
            if (character == '_')
            {
                upperNext = true;
                continue;
            }

            name.Append(upperNext ? char.ToUpperInvariant(character) : character);
            upperNext = false;
        }

        return name.ToString();
    }
}
