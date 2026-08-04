using System.Collections.ObjectModel;
using System.Text.Json;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Protocol;

/// <summary>One canonical Session item payload registration.</summary>
public sealed record SessionItemPayloadRegistration(string PayloadKind, Type PayloadType);

/// <summary>The canonical mapping from Session item payload kinds to Contracts DTO types.</summary>
public static class SessionItemPayloadCatalog
{
    private static readonly IReadOnlyList<SessionItemPayloadRegistration> Registrations =
    [
        new("agentMessage", typeof(AgentMessagePayload)),
        new("approvalRequest", typeof(ApprovalRequestPayload)),
        new("approvalResponse", typeof(ApprovalResponsePayload)),
        new("commandExecution", typeof(CommandExecutionPayload)),
        new("dynamicToolCall", typeof(DynamicToolCallPayload)),
        new("error", typeof(ErrorPayload)),
        new("imageGeneration", typeof(ImageGenerationPayload)),
        new("mcpToolCall", typeof(McpToolCallPayload)),
        new("reasoningContent", typeof(ReasoningContentPayload)),
        new("systemNotice", typeof(SystemNoticePayload)),
        new("toolCall", typeof(ToolCallPayload)),
        new("toolExecution", typeof(ToolExecutionPayload)),
        new("toolResult", typeof(ToolResultPayload)),
        new("userInputRequest", typeof(UserInputRequestPayload)),
        new("userInputResponse", typeof(UserInputResponsePayload)),
        new("userMessage", typeof(UserMessagePayload))
    ];

    private static readonly IReadOnlyDictionary<string, Type> Types = new ReadOnlyDictionary<string, Type>(
        Registrations.ToDictionary(static registration => registration.PayloadKind, static registration => registration.PayloadType, StringComparer.Ordinal));

    /// <summary>All canonical registrations in deterministic payload-kind order.</summary>
    public static IReadOnlyList<SessionItemPayloadRegistration> All => Registrations;

    /// <summary>Finds the Contracts DTO registered for a payload kind.</summary>
    public static bool TryGetPayloadType(string payloadKind, out Type payloadType) =>
        Types.TryGetValue(payloadKind, out payloadType!);
}

/// <summary>The result of parsing one Session item payload.</summary>
public sealed class SessionItemPayloadParseResult
{
    internal SessionItemPayloadParseResult(
        string? payloadKind,
        bool hasPayload,
        JsonElement? raw,
        bool isKnown,
        object? value)
    {
        PayloadKind = payloadKind;
        HasPayload = hasPayload;
        Raw = raw;
        IsKnown = isKnown;
        Value = value;
    }

    /// <summary>The payload discriminator supplied by the item.</summary>
    public string? PayloadKind { get; }

    /// <summary>Whether the wire item contained the payload property.</summary>
    public bool HasPayload { get; }

    /// <summary>The original JSON payload, including a JSON null when explicitly supplied.</summary>
    public JsonElement? Raw { get; }

    /// <summary>Whether the payload kind is registered by the canonical catalog.</summary>
    public bool IsKnown { get; }

    /// <summary>The parsed canonical DTO, or null for missing, null, or unknown payloads.</summary>
    public object? Value { get; }

    /// <summary>Gets the parsed payload when it has the requested canonical DTO type.</summary>
    public bool TryGet<TPayload>(out TPayload? payload) where TPayload : class
    {
        payload = Value as TPayload;
        return payload is not null;
    }
}

/// <summary>Parses canonical Session item payloads while retaining the original JSON.</summary>
public static class SessionItemPayloadParser
{
    /// <summary>Parses the payload selected by <see cref="SessionItem.PayloadKind"/>.</summary>
    /// <exception cref="JsonException">A known payload kind contains an invalid payload value.</exception>
    public static SessionItemPayloadParseResult Parse(SessionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var hasPayload = item.Payload.IsSet;
        JsonElement? raw = null;
        if (hasPayload)
            raw = item.Payload.Value ?? JsonSerializer.SerializeToElement<object?>(null, AppServerContractJson.Options);

        Type? payloadType = null;
        var isKnown = item.PayloadKind is not null &&
                      SessionItemPayloadCatalog.TryGetPayloadType(item.PayloadKind, out payloadType);
        if (!isKnown || raw is null || raw.Value.ValueKind == JsonValueKind.Null)
            return new(item.PayloadKind, hasPayload, raw, isKnown, null);

        var value = raw.Value.Deserialize(payloadType!, AppServerContractJson.Options)
                    ?? throw new JsonException($"Payload '{item.PayloadKind}' produced no value.");
        return new(item.PayloadKind, hasPayload, raw, isKnown: true, value);
    }
}
