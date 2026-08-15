using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

/// <summary>
/// Metadata for a user-provided local image attachment.
/// </summary>
public sealed record UserMessageImage
{
    /// <summary>
    /// Absolute path to the local image attachment.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Optional MIME type hint supplied by the input channel.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional original file name supplied by the input channel.
    /// </summary>
    public string? FileName { get; init; }
}
/// <summary>
/// Payload for UserMessage items.
/// </summary>
public sealed record UserMessagePayload
{
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// How this user message entered the turn: normal input, queued input,
    /// same-turn guidance, or an internal SubAgent mailbox notification.
    /// </summary>
    public string? DeliveryMode { get; init; }

    /// <summary>
    /// Native transport-level input parts captured as the source of truth for
    /// history rendering and draft rehydration.
    /// </summary>
    public IReadOnlyList<SessionInputPart>? NativeInputParts { get; init; }

    /// <summary>
    /// Materialized input parts captured after transport-side expansion, matching
    /// the content snapshot that was sent to the model for this turn.
    /// </summary>
    public IReadOnlyList<SessionInputPart>? MaterializedInputParts { get; init; }

    /// <summary>
    /// Individual sender within a group session (nullable for single-user channels).
    /// </summary>
    public string? SenderId { get; init; }

    /// <summary>
    /// Display name of the sender (nullable).
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Role of the sender when the originating channel provides it.
    /// </summary>
    public string? SenderRole { get; init; }

    /// <summary>
    /// Originating channel for this user message.
    /// </summary>
    public string? ChannelName { get; init; }

    /// <summary>
    /// Channel-specific context for this user message.
    /// </summary>
    public string? ChannelContext { get; init; }

    /// <summary>
    /// Group or chat identifier when the message originates from a group context.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Optional local image metadata used by clients to rehydrate user message attachments.
    /// </summary>
    public IReadOnlyList<UserMessageImage>? Images { get; init; }

    /// <summary>
    /// Non-null when this user message was synthesized by an automation mechanism
    /// (heartbeat, cron, automations) rather than typed by a human. Clients use this
    /// to render a "Sent via automation" affordance.
    /// </summary>
    public string? TriggerKind { get; init; }

    /// <summary>
    /// Optional human-readable label for the automation source (e.g. cron job name, local task identifier).
    /// </summary>
    public string? TriggerLabel { get; init; }

    /// <summary>
    /// Optional routing id for client-side click-through (e.g. cron job id, task id).
    /// </summary>
    public string? TriggerRefId { get; init; }

    /// <summary>
    /// Queued input id that produced this user message when <see cref="DeliveryMode"/> is <c>queued</c>.
    /// </summary>
    public string? QueuedInputId { get; init; }

    /// <summary>
    /// Thread app binding id that should receive this turn's default assistant delivery.
    /// Only populated for app/social binding initiated turns.
    /// </summary>
    public string? DeliveryBindingId { get; init; }

    /// <summary>
    /// True when this user message established the thread goal — the objective was submitted
    /// "as a goal". Durable provenance for the client "sent as goal" badge
    /// (see specs/features/goal.md §7.5). Null/false for ordinary messages; clients must
    /// not infer goal origin by matching message text to the current objective.
    /// </summary>
    public bool? SentAsGoal { get; init; }
}

/// <summary>
/// Optional transport-supplied input snapshots associated with a turn submission.
/// </summary>
public sealed record SessionInputSnapshot
{
    /// <summary>
    /// Native transport parts (commandRef/skillRef/fileRef/text/etc.) as supplied by the client.
    /// </summary>
    public IReadOnlyList<SessionInputPart>? NativeInputParts { get; init; }

    /// <summary>
    /// Materialized parts after transport-side expansion, aligned to the content actually sent to the model.
    /// </summary>
    public IReadOnlyList<SessionInputPart>? MaterializedInputParts { get; init; }

    /// <summary>
    /// Compatibility/display text derived from the native transport parts.
    /// </summary>
    public string? DisplayText { get; init; }

    /// <summary>
    /// Optional delivery mode carried into the persisted UserMessage payload.
    /// </summary>
    public string? DeliveryMode { get; init; }

    /// <summary>
    /// Queued input id when this snapshot represents a dequeued input.
    /// </summary>
    public string? QueuedInputId { get; init; }

    /// <summary>
    /// Thread app binding id that should receive this turn's default assistant delivery.
    /// </summary>
    public string? DeliveryBindingId { get; init; }

    /// <summary>
    /// True when this submission establishes the thread goal; persisted onto the resulting
    /// UserMessage payload as <see cref="UserMessagePayload.SentAsGoal"/>.
    /// </summary>
    public bool? SentAsGoal { get; init; }
}

/// <summary>
/// Persisted FIFO input queued while a thread already has an active turn.
/// </summary>
public sealed record QueuedTurnInput
{
    public string Id { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public IReadOnlyList<SessionInputPart> NativeInputParts { get; init; } = [];

    public IReadOnlyList<SessionInputPart> MaterializedInputParts { get; init; } = [];

    public string DisplayText { get; init; } = string.Empty;

    public SenderContext? Sender { get; init; }

    public string Status { get; init; } = "queued";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ReadyAfterTurnId { get; init; }

    /// <summary>
    /// Non-null when this queued input was synthesized by a server or app trigger.
    /// Preserved into the future UserMessage payload when dequeued.
    /// </summary>
    public string? TriggerKind { get; init; }

    /// <summary>
    /// Optional human-readable label for the trigger source.
    /// </summary>
    public string? TriggerLabel { get; init; }

    /// <summary>
    /// Optional routing id for client-side click-through to the trigger source.
    /// </summary>
    public string? TriggerRefId { get; init; }

    /// <summary>
    /// Thread app binding id that should receive this queued input's default assistant delivery.
    /// </summary>
    public string? DeliveryBindingId { get; init; }

    /// <summary>
    /// True when this queued input was originally submitted as the thread goal objective.
    /// Preserved into the future UserMessage payload when dequeued.
    /// </summary>
    public bool? SentAsGoal { get; init; }
}
