using System.Text;
using System.Text.Json;
using DotCraft.Sessions;

namespace DotCraft.SessionImport;

internal static class ExternalSessionText
{
    private const string FallbackTitle = "Imported session";
    private const int ToolInputMaxLength = 2000;
    private const int ToolResultMaxLength = 4000;
    private const int TitleMaxLength = 120;
    private const string ToolCallTag = "external_agent_tool_call";
    private const string ToolResultTag = "external_agent_tool_result";
    private const string UserQueryOpen = "<user_query>";
    private const string UserQueryClose = "</user_query>";

    private static readonly (string Open, string Close)[] CursorContextWrappers =
    [
        ("<timestamp>", "</timestamp>"),
        ("<cursor_commands>", "</cursor_commands>")
    ];

    public sealed record ExtractedMessage(IReadOnlyList<string> Parts, bool OnlyToolResult)
    {
        public string Text => JoinParts(Parts);
    }

    public static ExtractedMessage? ExtractMessage(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var text = content.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : new ExtractedMessage([text], false);
        }

        if (content.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        var onlyToolResult = true;
        foreach (var block in content.EnumerateArray())
        {
            switch (block.ReadString("type"))
            {
                case "text":
                    if (block.ReadString("text") is { Length: > 0 } text)
                    {
                        parts.Add(text);
                        onlyToolResult = false;
                    }
                    break;
                case "tool_use":
                    parts.Add(ToolCallNote(block.ReadString("name"), block.ReadProperty("input")));
                    onlyToolResult = false;
                    break;
                case "tool_result":
                    parts.Add(ToolResultNote(ToolResultText(block.ReadProperty("content")), block.ReadBool("is_error")));
                    break;
                case "thinking" or "redacted_thinking":
                    break;
                case { } other:
                    parts.Add(UnsupportedBlock(other));
                    onlyToolResult = false;
                    break;
            }
        }

        var visible = parts.Where(static part => !string.IsNullOrWhiteSpace(part)).ToArray();
        return visible.Length == 0 ? null : new ExtractedMessage(visible, onlyToolResult);
    }

    public static string ToolCallNote(string? name, JsonElement input)
    {
        var lines = new List<string>();
        if (input.ValueKind == JsonValueKind.Object)
        {
            if (NonEmpty(input.ReadString("description")) is { } description)
                lines.Add($"description: {description}");
            if (CommandText(input.ReadProperty("command")) is { } command)
                lines.Add($"command: {command}");
            if (NonEmpty(input.ReadString("file_path") ?? input.ReadString("file")) is { } file)
                lines.Add($"file: {file}");
        }

        if (lines.Count == 0)
        {
            var raw = input.ValueKind switch
            {
                JsonValueKind.String => input.GetString(),
                JsonValueKind.Undefined or JsonValueKind.Null => null,
                _ => input.GetRawText()
            };
            if (!string.IsNullOrEmpty(raw))
                lines.Add($"input: {Truncate(raw, ToolInputMaxLength)}");
        }

        return ToolCallNote(name, lines);
    }

    public static string ToolCallNote(string? name, IReadOnlyList<string> parameterLines) =>
        string.Join('\n', [$"[{ToolCallTag}: {(string.IsNullOrWhiteSpace(name) ? "unknown" : name)}]", .. parameterLines, $"[/{ToolCallTag}]"]);

    public static string? CommandText(JsonElement command)
    {
        if (command.ValueKind == JsonValueKind.String)
            return NonEmpty(command.GetString());
        if (command.ValueKind != JsonValueKind.Array)
            return null;
        var parts = command.EnumerateArray()
            .Where(static part => part.ValueKind == JsonValueKind.String)
            .Select(static part => part.GetString()!)
            .ToArray();
        return parts.Length == 0 ? null : string.Join(' ', parts);
    }

    public static string ToolResultNote(string? text, bool isError)
    {
        var label = isError ? $"[{ToolResultTag}: error]" : $"[{ToolResultTag}]";
        return string.IsNullOrEmpty(text)
            ? $"{label}\n[/{ToolResultTag}]"
            : $"{label}\n{Truncate(text, ToolResultMaxLength)}\n[/{ToolResultTag}]";
    }

    public static string ToolResultText(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => content.GetString() ?? string.Empty,
        JsonValueKind.Array => string.Join('\n', content.EnumerateArray()
            .Select(static item => item.ReadString("text"))
            .Where(static text => !string.IsNullOrEmpty(text))),
        _ => string.Empty
    };

    public static string UnsupportedBlock(string type) => $"[external unsupported block: {type}]";

    public static string JoinParts(IEnumerable<string> parts) =>
        string.Join("\n\n", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string Truncate(string text, int maxLength)
    {
        var keep = maxLength - 3;
        var count = 0;
        var index = 0;
        var cut = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count == keep)
                cut = index;
            count++;
            if (count > maxLength)
                return string.Concat(text.AsSpan(0, cut), "...");
            index += rune.Utf16SequenceLength;
        }

        return text;
    }

    public static string UnwrapUserQuery(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith(UserQueryOpen, StringComparison.Ordinal) || !trimmed.EndsWith(UserQueryClose, StringComparison.Ordinal))
            return text;

        var inner = trimmed[UserQueryOpen.Length..^UserQueryClose.Length].Trim();
        return inner.Length == 0 ? text : inner;
    }

    public static string UnwrapCursorUserQuery(string text)
    {
        var trimmed = text.Trim();
        var queryStart = trimmed.IndexOf(UserQueryOpen, StringComparison.Ordinal);
        if (queryStart < 0)
            return text;
        var contentStart = queryStart + UserQueryOpen.Length;
        var queryEnd = trimmed.IndexOf(UserQueryClose, contentStart, StringComparison.Ordinal);
        if (queryEnd < 0 || !string.IsNullOrWhiteSpace(trimmed[(queryEnd + UserQueryClose.Length)..]))
            return text;

        var leading = trimmed[..queryStart].Trim();
        while (leading.Length > 0)
        {
            var wrapper = CursorContextWrappers.FirstOrDefault(entry => leading.StartsWith(entry.Open, StringComparison.Ordinal));
            if (wrapper.Open is null)
                return text;
            var wrapperEnd = leading.IndexOf(wrapper.Close, wrapper.Open.Length, StringComparison.Ordinal);
            if (wrapperEnd < 0)
                return text;
            leading = leading[(wrapperEnd + wrapper.Close.Length)..].Trim();
        }

        var inner = trimmed[contentStart..queryEnd].Trim();
        return inner.Length == 0 ? text : inner;
    }

    private static string? TitleFromUserText(string text)
    {
        var remainder = StripLeadingControlWrappers(text);
        foreach (var line in remainder.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return Truncate(trimmed, TitleMaxLength);
        }

        return null;
    }

    public static string SelectTitle(IReadOnlyList<ImportedTurnInput> turns, params string?[] preferred)
    {
        foreach (var candidate in preferred)
        {
            if (NonEmpty(candidate) is { } title)
                return title;
        }

        return turns.Select(static turn => TitleFromUserText(turn.UserText)).FirstOrDefault(static title => title is not null)
            ?? FallbackTitle;
    }

    public static long EstimateTokens(IEnumerable<ImportedTurnInput> turns) =>
        turns.Sum(static turn => (long)Encoding.UTF8.GetByteCount(turn.UserText)
            + turn.AgentTexts.Sum(static text => (long)Encoding.UTF8.GetByteCount(text))) / 4;

    /// <summary>Skips leading tag-wrapped blocks (system reminders, attachments) and unwraps a leading user query.</summary>
    private static string StripLeadingControlWrappers(string message)
    {
        var remainder = message.TrimStart();
        while (LeadingTag(remainder) is { } tag)
        {
            var close = remainder.IndexOf($"</{tag.Name}>", tag.Length, StringComparison.Ordinal);
            if (close < 0)
                break;
            if (tag.Name == "user_query")
                return remainder[tag.Length..close].Trim();
            remainder = remainder[(close + tag.Name.Length + 3)..].TrimStart();
        }

        return remainder;
    }

    private static (string Name, int Length)? LeadingTag(string text)
    {
        if (text.Length < 3 || text[0] != '<' || !char.IsAsciiLetterLower(text[1]))
            return null;
        var end = 1;
        while (end < text.Length && (char.IsAsciiLetterLower(text[end]) || char.IsAsciiDigit(text[end]) || text[end] is '_' or '-'))
            end++;
        return end < text.Length && text[end] == '>' ? (text[1..end], end + 1) : null;
    }

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
