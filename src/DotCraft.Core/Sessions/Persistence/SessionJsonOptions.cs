using System.Text.Json;
using System.Text.Json.Serialization;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Sessions;

/// <summary>
/// Shared JSON serialization options for Session Protocol types.
/// Handles polymorphic payload deserialization keyed on ItemType / SessionEventType.
/// </summary>
public static class SessionJsonOptions
{
    /// <summary>
    /// The canonical options used by Session Core for persisting Thread/Turn/Item data.
    /// Based on JsonSerializerOptions.Web (camelCase, case-insensitive) plus Session converters.
    /// </summary>
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions(JsonSerializerOptions.Web);
        opts.Converters.Add(new ReasoningEffortJsonConverter());
        opts.Converters.Add(new ReasoningOutputJsonConverter());
        opts.Converters.Add(new SessionItemConverter());
        opts.Converters.Add(new SessionEventConverter());
        return opts;
    }
}

/// <summary>
/// Custom converter for SessionItem that deserializes Payload based on the ItemType field.
/// </summary>
internal sealed class SessionItemConverter : JsonConverter<SessionItem>
{
    public override SessionItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var item = new SessionItem
        {
            Id = root.GetStringOrEmpty("id"),
            TurnId = root.GetStringOrEmpty("turnId"),
            Status = root.GetEnum<ItemStatus>("status"),
            CreatedAt = root.GetDateTimeOffset("createdAt"),
            CompletedAt = root.GetNullableDateTimeOffset("completedAt")
        };

        var itemType = root.GetEnum<ItemType>("type");
        item.Type = itemType;

        if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind != JsonValueKind.Null)
        {
            item.Payload = DeserializePayload(itemType, payloadEl, options);
        }

        return item;
    }

    private static object? DeserializePayload(ItemType itemType, JsonElement payload, JsonSerializerOptions options) =>
        itemType switch
        {
            ItemType.UserMessage => payload.Deserialize<UserMessagePayload>(options),
            ItemType.AgentMessage => payload.Deserialize<AgentMessagePayload>(options),
            ItemType.ReasoningContent => payload.Deserialize<ReasoningContentPayload>(options),
            ItemType.CommandExecution => payload.Deserialize<CommandExecutionPayload>(options),
            ItemType.ToolExecution => payload.Deserialize<ToolExecutionPayload>(options),
            ItemType.ImageGeneration => payload.Deserialize<ImageGenerationPayload>(options),
            ItemType.ToolCall => payload.Deserialize<ToolCallPayload>(options),
            ItemType.McpToolCall => payload.Deserialize<McpToolCallPayload>(options),
            ItemType.DynamicToolCall => payload.Deserialize<DynamicToolCallPayload>(options),
            ItemType.ToolResult => payload.Deserialize<ToolResultPayload>(options),
            ItemType.ApprovalRequest => payload.Deserialize<ApprovalRequestPayload>(options),
            ItemType.ApprovalResponse => payload.Deserialize<ApprovalResponsePayload>(options),
            ItemType.UserInputRequest => payload.Deserialize<UserInputRequestPayload>(options),
            ItemType.UserInputResponse => payload.Deserialize<UserInputResponsePayload>(options),
            ItemType.Error => payload.Deserialize<ErrorPayload>(options),
            ItemType.SystemNotice => payload.Deserialize<SystemNoticePayload>(options),
            _ => null
        };

    public override void Write(Utf8JsonWriter writer, SessionItem value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("turnId", value.TurnId);
        writer.WriteString("type", value.Type.ToString());
        writer.WriteString("status", value.Status.ToString());
        writer.WriteString("createdAt", value.CreatedAt);
        if (value.CompletedAt.HasValue)
            writer.WriteString("completedAt", value.CompletedAt.Value);
        else
            writer.WriteNull("completedAt");

        writer.WritePropertyName("payload");
        if (value.Payload is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);

        writer.WriteEndObject();
    }
}

/// <summary>
/// Custom converter for SessionEvent that handles Payload polymorphism.
/// </summary>
internal sealed class SessionEventConverter : JsonConverter<SessionEvent>
{
    public override SessionEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var evt = new SessionEvent
        {
            EventId = root.GetStringOrEmpty("eventId"),
            EventType = root.GetEnum<SessionEventType>("eventType"),
            ThreadId = root.GetStringOrEmpty("threadId"),
            TurnId = root.TryGetProperty("turnId", out var turnId) ? turnId.GetString() : null,
            ItemId = root.TryGetProperty("itemId", out var itemId) ? itemId.GetString() : null,
            Timestamp = root.GetDateTimeOffset("timestamp")
        };

        if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind != JsonValueKind.Null)
        {
            evt.Payload = DeserializeEventPayload(evt.EventType, payloadEl, options);
        }

        return evt;
    }

    private static object? DeserializeEventPayload(SessionEventType eventType, JsonElement payload, JsonSerializerOptions options) =>
        eventType switch
        {
            SessionEventType.ThreadCreated =>
                payload.Deserialize<SessionThread>(options),
            SessionEventType.ThreadResumed =>
                payload.Deserialize<ThreadResumedPayload>(options),
            SessionEventType.ThreadStatusChanged =>
                payload.Deserialize<ThreadStatusChangedPayload>(options),
            SessionEventType.TurnStarted or SessionEventType.TurnCompleted =>
                payload.Deserialize<SessionTurn>(options),
            SessionEventType.TurnFailed =>
                payload.Deserialize<TurnFailedPayload>(options),
            SessionEventType.TurnCancelled =>
                payload.Deserialize<TurnCancelledPayload>(options),
            SessionEventType.ItemStarted or SessionEventType.ItemCompleted
                or SessionEventType.ApprovalRequested or SessionEventType.ApprovalResolved
                or SessionEventType.UserInputRequested or SessionEventType.UserInputResolved =>
                payload.Deserialize<SessionItem>(options),
            SessionEventType.ItemDelta => DeserializeDeltaPayload(payload, options),
            _ => null
        };

    private static object? DeserializeDeltaPayload(JsonElement payload, JsonSerializerOptions options)
    {
        if (payload.TryGetProperty("deltaKind", out var deltaKind))
        {
            return (deltaKind.GetString() ?? string.Empty) switch
            {
                "reasoningContent" => payload.Deserialize<ReasoningContentDelta>(options),
                "commandExecution" => payload.Deserialize<CommandExecutionOutputDelta>(options),
                "agentMessage" => payload.Deserialize<AgentMessageDelta>(options),
                _ => payload.Deserialize<AgentMessageDelta>(options)
            };
        }

        if (payload.TryGetProperty("textDelta", out _))
        {
            // Backward compatibility for payloads persisted before deltaKind existed.
            return payload.Deserialize<AgentMessageDelta>(options);
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, SessionEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("eventId", value.EventId);
        writer.WriteString("eventType", value.EventType.ToString());
        writer.WriteString("threadId", value.ThreadId);
        if (value.TurnId is not null) writer.WriteString("turnId", value.TurnId);
        else writer.WriteNull("turnId");
        if (value.ItemId is not null) writer.WriteString("itemId", value.ItemId);
        else writer.WriteNull("itemId");
        writer.WriteString("timestamp", value.Timestamp);

        writer.WritePropertyName("payload");
        if (value.Payload is null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Payload, value.Payload.GetType(), options);

        writer.WriteEndObject();
    }
}

/// <summary>
/// Helpers for reading JSON element values safely.
/// </summary>
internal static class JsonElementExtensions
{
    public static string GetStringOrEmpty(this JsonElement el, string property) =>
        el.TryGetProperty(property, out var val) ? val.GetString() ?? string.Empty : string.Empty;

    public static T GetEnum<T>(this JsonElement el, string property) where T : struct, Enum
    {
        if (!el.TryGetProperty(property, out var val))
            return default;
        var str = val.GetString();
        return str is not null && Enum.TryParse<T>(str, ignoreCase: true, out var result) ? result : default;
    }

    public static DateTimeOffset GetDateTimeOffset(this JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var val) && val.TryGetDateTimeOffset(out var dt))
            return dt;
        return default;
    }

    public static DateTimeOffset? GetNullableDateTimeOffset(this JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var val)) return null;
        if (val.ValueKind == JsonValueKind.Null) return null;
        return val.TryGetDateTimeOffset(out var dt) ? dt : null;
    }
}
