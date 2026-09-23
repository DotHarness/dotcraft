using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal sealed class CodexRecordConverter(bool paginated, DateTimeOffset fallbackTime)
{
    private readonly ImportTurnBuilder _builder = new(fallbackTime);
    private readonly HashSet<string> _seenItems = new(StringComparer.Ordinal);

    /// <summary>Whether the rollout carries the marker Codex writes into sessions it imported from another agent.</summary>
    public bool SawImportMarker { get; private set; }

    public IReadOnlyList<ImportedTurnInput> Build() => _builder.Build();

    public void Consume(JsonElement record)
    {
        if (!record.TryReadProperty("payload", JsonValueKind.Object, out var payload))
            return;
        var timestamp = record.ReadTimestamp();
        switch (record.ReadString("type"))
        {
            case "event_msg":
                ConsumeEvent(payload, timestamp);
                break;
            case "response_item" when !paginated:
                ConsumeLegacyResponseItem(payload, timestamp);
                break;
        }
    }

    private void ConsumeEvent(JsonElement payload, DateTimeOffset? timestamp)
    {
        switch (payload.ReadString("type"))
        {
            case "item_completed" when paginated:
                ConsumeItem(payload, timestamp);
                break;
            case "user_message" when !paginated:
                AddUser(payload.ReadString("message"), timestamp);
                break;
            case "agent_message" when !paginated:
                AddAgent(payload.ReadString("message"), timestamp);
                break;
        }
    }

    private void ConsumeItem(JsonElement payload, DateTimeOffset? timestamp)
    {
        if (!payload.TryReadProperty("item", JsonValueKind.Object, out var item))
            return;
        if (item.ReadString("id") is { } itemId && !_seenItems.Add($"{payload.ReadString("turn_id")}\n{itemId}"))
            return;

        switch (item.ReadString("type"))
        {
            case "UserMessage":
                AddUser(UserMessageText(item), timestamp);
                break;
            case "AgentMessage":
                AddAgent(ContentText(item, "Text"), timestamp);
                break;
            case "CommandExecution":
                AddAgent(CommandExecutionText(item), timestamp);
                break;
            case "FileChange":
                AddAgent(FileChangeText(item), timestamp);
                break;
            case "McpToolCall":
                AddAgent(McpToolCallText(item), timestamp);
                break;
        }
    }

    private void ConsumeLegacyResponseItem(JsonElement payload, DateTimeOffset? timestamp)
    {
        switch (payload.ReadString("type"))
        {
            case "function_call":
                AddToolCall(QualifiedName(payload), payload.ReadString("arguments"), timestamp);
                break;
            case "custom_tool_call":
                AddToolCall(QualifiedName(payload), payload.ReadString("input"), timestamp);
                break;
            case "local_shell_call":
                AddAgent(ExternalSessionText.ToolCallNote("local_shell", payload.ReadProperty("action")), timestamp);
                break;
            case "function_call_output" or "custom_tool_call_output":
                AddAgent(
                    ExternalSessionText.ToolResultNote(ExternalSessionText.ToolResultText(payload.ReadProperty("output")), isError: false),
                    timestamp);
                break;
        }
    }

    private void AddUser(string? text, DateTimeOffset? timestamp)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        MarkImportMarker(text);
        _builder.AddUser(text, timestamp);
    }

    private void AddAgent(string? text, DateTimeOffset? timestamp)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        MarkImportMarker(text);
        _builder.AddAgent(text, timestamp);
    }

    private void AddToolCall(string name, string? rawInput, DateTimeOffset? timestamp)
    {
        if (rawInput is not null && TryParseObject(rawInput) is { } document)
        {
            using (document)
                AddAgent(ExternalSessionText.ToolCallNote(name, document.RootElement), timestamp);
            return;
        }

        var input = rawInput is null ? default : JsonSerializer.SerializeToElement(rawInput);
        AddAgent(ExternalSessionText.ToolCallNote(name, input), timestamp);
    }

    private void MarkImportMarker(string text)
    {
        if (string.Equals(text.Trim(), ThreadImportConstants.Marker, StringComparison.Ordinal))
            SawImportMarker = true;
    }

    private static string UserMessageText(JsonElement item)
    {
        if (!item.TryReadProperty("content", JsonValueKind.Array, out var content))
            return string.Empty;
        var parts = new List<string>();
        foreach (var part in content.EnumerateArray())
        {
            var type = part.ReadString("type");
            if (type == "text")
                parts.Add(part.ReadString("text") ?? string.Empty);
            else if (type is not null)
                parts.Add(ExternalSessionText.UnsupportedBlock(type));
        }

        return ExternalSessionText.JoinParts(parts);
    }

    private static string ContentText(JsonElement item, string partType) =>
        item.TryReadProperty("content", JsonValueKind.Array, out var content)
            ? ExternalSessionText.JoinParts(content.EnumerateArray()
                .Where(part => part.ReadString("type") == partType)
                .Select(static part => part.ReadString("text") ?? string.Empty))
            : string.Empty;

    private static string CommandExecutionText(JsonElement item)
    {
        var command = ExternalSessionText.CommandText(item.ReadProperty("command"));
        var call = ExternalSessionText.ToolCallNote("shell", command is null ? [] : [$"command: {command}"]);
        var output = item.ReadString("aggregated_output")
            ?? JoinNonEmpty(item.ReadString("stdout"), item.ReadString("stderr"))
            ?? item.ReadString("formatted_output");
        var failed = item.ReadString("status") is "failed" or "declined"
            || item.ReadInt64("exit_code") is { } exitCode && exitCode != 0;
        return ExternalSessionText.JoinParts([call, ExternalSessionText.ToolResultNote(output, failed)]);
    }

    private static string FileChangeText(JsonElement item)
    {
        string[] files = item.TryReadProperty("changes", JsonValueKind.Object, out var changes)
            ? changes.EnumerateObject().Select(static change => $"file: {change.Name}").ToArray()
            : [];
        var call = ExternalSessionText.ToolCallNote("apply_patch", files);
        var output = JoinNonEmpty(item.ReadString("stdout"), item.ReadString("stderr"));
        var failed = item.ReadString("status") is "failed" or "declined";
        return ExternalSessionText.JoinParts([call, ExternalSessionText.ToolResultNote(output, failed)]);
    }

    private static string McpToolCallText(JsonElement item)
    {
        var call = ExternalSessionText.ToolCallNote(
            $"mcp__{item.ReadString("server")}__{item.ReadString("tool")}",
            item.ReadProperty("arguments"));
        string? output;
        bool failed;
        if (item.TryReadProperty("error", JsonValueKind.Object, out var error))
        {
            output = error.ReadString("message");
            failed = true;
        }
        else
        {
            var result = item.ReadProperty("result");
            output = ExternalSessionText.ToolResultText(result.ReadProperty("content"));
            failed = result.ReadBool("isError") || item.ReadString("status") == "failed";
        }

        return ExternalSessionText.JoinParts([call, ExternalSessionText.ToolResultNote(output, failed)]);
    }

    private static string QualifiedName(JsonElement payload)
    {
        var name = payload.ReadString("name") ?? "unknown";
        return payload.ReadString("namespace") is { Length: > 0 } ns && !name.StartsWith(ns, StringComparison.Ordinal)
            ? $"{ns}.{name}"
            : name;
    }

    private static JsonDocument? TryParseObject(string raw)
    {
        try
        {
            var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;
            document.Dispose();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? JoinNonEmpty(params string?[] values)
    {
        var present = values.Where(static value => !string.IsNullOrEmpty(value)).ToArray();
        return present.Length == 0 ? null : string.Join('\n', present);
    }
}
