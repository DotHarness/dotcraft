using System.Text;
using DotCraft.Tools;

namespace DotCraft.CodeMode;

internal static class CodeModeDeclarations
{
    public const int MaxTypeChars = 16_000;
    private const int MaxInlineShapeChars = 1_000;

    public const string CommandResultType =
        "{ sessionId: string; status: \"running\" | \"completed\" | \"failed\"; output: string; exitCode: number | null; truncated: boolean; outputPath?: string }";

    public const string McpPreamble = """
        type TextContent = { type: "text"; text: string };
        type ImageContent = { type: "image"; data: string; mimeType: string };
        type ContentBlock = TextContent | ImageContent | { type: string; [key: string]: unknown };
        type CallToolResult<T = unknown> = { content: ContentBlock[]; structuredContent?: T; isError?: boolean };
        """;

    public static bool IsMcp(ToolDefinition definition) => definition.Id.Kind == ToolSourceKind.Mcp;

    public static bool IsCommandExecution(ToolDefinition definition) =>
        definition.Id.Kind == ToolSourceKind.CoreNative
        && definition.Id.SourceId == "core-native"
        && definition.Id.SourceToolId.Value is "Exec" or "WriteStdin";

    public static string Declaration(string jsName, ToolDefinition definition)
    {
        var input = Bounded(CodeModeTypeScript.Render(definition.InputSchema, indent: 2), "unknown");
        return new StringBuilder()
            .Append(definition.Description.Trim()).Append('\n')
            .Append("declare const tools: {\n")
            .Append("  ").Append(jsName).Append("(args: ").Append(input).Append("): Promise<")
            .Append(ResultType(definition, indent: 2, inline: false)).Append(">;\n")
            .Append("};")
            .ToString();
    }

    public static string CallLine(string jsName, ToolDefinition definition)
    {
        var shape = ResultType(definition, indent: 0, inline: true);
        var phrase = shape switch
        {
            "string" => "a string",
            { Length: > MaxInlineShapeChars } => "its structured result",
            _ => $"`{shape}`"
        };
        return $"Code mode: `tools.{jsName}(args)` resolves to {phrase}.";
    }

    private static string ResultType(ToolDefinition definition, int indent, bool inline)
    {
        if (IsMcp(definition))
        {
            return definition.OutputSchema is { } mcpOutput
                ? Bounded($"CallToolResult<{CodeModeTypeScript.Render(mcpOutput, indent, inline)}>", "CallToolResult")
                : "CallToolResult";
        }
        if (definition.OutputSchema is { } output)
            return Bounded(CodeModeTypeScript.Render(output, indent, inline), "unknown");
        return IsCommandExecution(definition) ? CommandResultType : "string";
    }

    private static string Bounded(string type, string fallback) => type.Length > MaxTypeChars ? fallback : type;
}
