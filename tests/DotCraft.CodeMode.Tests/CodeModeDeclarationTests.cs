using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.CodeMode.Tests;

public sealed class CodeModeDeclarationTests
{
    [Fact]
    public void Declaration_RendersTheInputAndOutputSchemasAsTypeScript()
    {
        var declaration = CodeModeDeclarations.Declaration("mcp__docs__search", SearchDefinition());

        Assert.Equal(
            """
            Search the docs.
            declare const tools: {
              mcp__docs__search(args: {
                extra?: {
                  [key: string]: boolean;
                };
                filter?: {
                  child?: unknown;
                  owner: string;
                };
                mode?: "fast" | "full";
                // Search text.
                query: string;
                tags?: (string | number)[];
              }): Promise<CallToolResult<{
                count: number;
              }>>;
            };
            """.ReplaceLineEndings("\n"),
            declaration);
    }

    [Fact]
    public void CallLine_NamesTheCallFormAndResultShape()
    {
        Assert.Equal(
            "Code mode: `tools.mcp__docs__search(args)` resolves to `CallToolResult<{ count: number }>`.",
            CodeModeDeclarations.CallLine("mcp__docs__search", SearchDefinition()));
        Assert.Equal(
            "Code mode: `tools.Exec(args)` resolves to `{ sessionId: string; status: \"running\" | \"completed\" | \"failed\"; output: string; exitCode: number | null; truncated: boolean; outputPath?: string }`.",
            CodeModeDeclarations.CallLine("Exec", Definition(
                new ToolDefinitionId(ToolSourceKind.CoreNative, "core-native", new SourceToolId("Exec")),
                new ToolName(null, "Exec"),
                new { type = "object", properties = new { command = new { type = "string" } } })));
    }

    [Fact]
    public void Declaration_FallsBackToUnknownForOversizedTypes()
    {
        var properties = Enumerable.Range(0, 800).ToDictionary(index => $"property{index:D4}", _ => new { type = "string" });
        var declaration = CodeModeDeclarations.Declaration("Big", Definition(
            new ToolDefinitionId(ToolSourceKind.CoreNative, "test", new SourceToolId("Big")),
            new ToolName(null, "Big"),
            new { type = "object", properties },
            new { type = "object", properties }));

        Assert.Contains("  Big(args: unknown): Promise<unknown>;", declaration);
    }

    private static ToolDefinition SearchDefinition() => Definition(
        new ToolDefinitionId(ToolSourceKind.Mcp, "docs", new SourceToolId("search")),
        new ToolName("mcp__docs", "search"),
        JsonSerializer.Deserialize<JsonElement>("""
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Search text." },
                "mode": { "enum": ["fast", "full"] },
                "filter": { "$ref": "#/$defs/filter" },
                "tags": { "type": "array", "items": { "anyOf": [{ "type": "string" }, { "type": "number" }] } },
                "extra": { "type": "object", "additionalProperties": { "type": "boolean" } }
              },
              "required": ["query"],
              "$defs": {
                "filter": {
                  "type": "object",
                  "properties": { "owner": { "type": "string" }, "child": { "$ref": "#/$defs/filter" } },
                  "required": ["owner"]
                }
              }
            }
            """),
        new { type = "object", properties = new { count = new { type = "integer" } }, required = new[] { "count" } });

    private static ToolDefinition Definition(ToolDefinitionId id, ToolName name, object input, object? output = null) =>
        new(
            id,
            name,
            name.Name == "search" ? "Search the docs." : $"{name.Name} tool.",
            input as JsonElement? ?? JsonSerializer.SerializeToElement(input),
            output is null ? null : JsonSerializer.SerializeToElement(output));
}
