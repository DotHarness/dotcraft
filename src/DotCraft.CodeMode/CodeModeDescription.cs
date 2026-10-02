using DotCraft.Configuration;

namespace DotCraft.CodeMode;

public static class CodeModeDescription
{
    public static string Build(CodeModeSurface surface, CodeModeLimits limits, AppConfig.CodeModeSetting mode)
    {
        var lines = new List<string>
        {
            "Run a JavaScript program that calls this conversation's tools as async functions. Use it to batch independent tool calls, chain calls that depend on earlier results, and reduce large results to what you need before they reach you.",
            "",
            "Runtime: send raw JavaScript source, not JSON, a quoted string, or a Markdown code fence. The program is the body of an async function, so top-level `await` and `return` work. There is no Node.js, file system, network, timers, modules, or `console`.",
            "",
            "Calling tools: `await tools.<name>(args)` resolves to the result type in the tool's declaration and rejects with an `Error` on failure. Calls run concurrently under `Promise.all`; calls still pending when the program ends are cancelled.",
            "",
            "Globals:",
            "- `text(value)` appends text to the output; non-strings are JSON-stringified.",
            "- `image(value)` appends an image from a base64 `data:` URL or an MCP image content block.",
            "- `exit()` ends the program successfully.",
            "- `store(key, value)` and `load(key)` keep JSON values across `exec` calls in this conversation; storing `undefined` deletes a key.",
            "- `ALL_TOOLS` lists `{ name, description }` for every callable tool; each description ends with the tool's declaration.",
            "Only `text` and `image` produce output; the program's return value is ignored.",
            "",
            $"An optional first line `// @exec: {{\"timeout_ms\": 60000, \"max_output_tokens\": 10000}}` sets this call's limits; `timeout_ms` defaults to {(long)limits.DefaultTimeout.TotalMilliseconds} and `max_output_tokens` to {limits.DefaultMaxOutputTokens}.",
            "",
            mode == AppConfig.CodeModeSetting.Only
                ? "The tools you can call from a program are declared below; they are not in your direct tool list."
                : "Each tool in your tool list that a program can call ends its description with its call form and result type."
        };
        if (surface.HasUnlistedTools)
            lines.Add("More callable tools are not listed; find them by filtering `ALL_TOOLS` by name or description.");
        if (surface.Tools.Any(static tool => CodeModeDeclarations.IsMcp(tool.Registration.Definition)))
            lines.AddRange(["", "MCP tools resolve to `CallToolResult`:", CodeModeDeclarations.McpPreamble]);
        if (mode == AppConfig.CodeModeSetting.Only)
            AppendDeclarations(lines, surface);
        return string.Join('\n', lines);
    }

    private static void AppendDeclarations(List<string> lines, CodeModeSurface surface)
    {
        foreach (var group in surface.Tools
                     .Where(static tool => tool.Listed)
                     .GroupBy(static tool => tool.Registration.Definition.Name.Namespace))
        {
            lines.Add("");
            if (group.Key is { } ns)
            {
                lines.Add(surface.NamespaceDescriptions.TryGetValue(ns, out var description)
                    ? $"Namespace `{ns}`: {description}"
                    : $"Namespace `{ns}`:");
            }
            else
            {
                lines.Add("Tools:");
            }
            foreach (var tool in group)
                lines.AddRange(["", tool.Declaration]);
        }
    }
}
