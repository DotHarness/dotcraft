using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;

namespace DotCraft.SessionImport;

internal static class ExternalPluginConverter
{
    public static bool Convert(string root, string source, string id, string finalRoot)
    {
        var sourceManifest = Path.Combine(root, source switch { "claude-code" => ".claude-plugin", "cursor" => ".cursor-plugin", _ => ".codex-plugin" }, "plugin.json");
        if (!File.Exists(sourceManifest)) throw new NotSupportedException("import_plugin_manifest_missing");
        var input = SetupImportScanner.ReadJson(sourceManifest);
        var output = new JsonObject
        {
            ["schemaVersion"] = 1, ["id"] = id,
            ["displayName"] = ImportTextRewrite.Apply(input["name"]?.GetValue<string>() ?? id, source),
            ["version"] = input["version"]?.GetValue<string>() ?? "1.0.0",
            ["description"] = ImportTextRewrite.Apply(input["description"]?.GetValue<string>() ?? "", source)
        };
        var contributions = 0;
        var partial = input["agents"] != null || input["lspServers"] != null || input["apps"] != null
            || Directory.Exists(Path.Combine(root, "agents")) || input["settings"] != null;
        var declaredSkills = input["skills"];
        if (declaredSkills != null && declaredSkills is not JsonValue) throw new NotSupportedException("import_plugin_path_unsupported");
        var directory = ExternalPluginSource.Confined(root, declaredSkills?.GetValue<string>() ?? "./skills");
        if (Directory.Exists(directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory, "SKILL.md", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                File.WriteAllText(file, ImportTextRewrite.Apply(text, source));
            }
            output["skills"] = "./" + Path.GetRelativePath(root, directory).Replace('\\', '/');
            contributions++;
        }
        JsonObject? mcp = null;
        if (input["mcpServers"] is JsonObject inline) mcp = inline;
        else
        {
            var mcpPath = ExternalPluginSource.Confined(root, input["mcpServers"]?.GetValue<string>() ?? "./.mcp.json");
            if (File.Exists(mcpPath)) mcp = SetupImportScanner.ReadJson(mcpPath);
        }
        if (mcp != null)
        {
            var servers = mcp["mcpServers"] as JsonObject ?? mcp;
            var converted = new JsonObject();
            foreach (var (name, node) in servers)
                if (node is JsonObject server)
                {
                    var payload = SetupConfigConverters.Mcp(ExpandRoot(server, finalRoot), source == "codex");
                    if (payload == null) continue;
                    converted[name] = new JsonObject(payload.Select(pair => new KeyValuePair<string, JsonNode?>(
                        char.ToLowerInvariant(pair.Key[0]) + pair.Key[1..], pair.Value?.DeepClone())));
                }
            if (converted.Count > 0)
            {
                File.WriteAllText(Path.Combine(root, ".mcp.json"), new JsonObject { ["mcpServers"] = converted }.ToJsonString());
                output["mcpServers"] = "./.mcp.json";
                contributions++;
            }
        }
        var hookDeclaration = input["hooks"];
        var hookPath = hookDeclaration is JsonValue path ? path.GetValue<string>() : "./hooks/hooks.json";
        var hooks = hookDeclaration as JsonObject;
        if (hooks == null && File.Exists(ExternalPluginSource.Confined(root, hookPath)))
            hooks = SetupImportScanner.ReadJson(ExternalPluginSource.Confined(root, hookPath));
        if (hooks != null)
        {
            hooks = ExpandRoot(hooks, finalRoot);
            var converted = SetupConfigConverters.Hooks(hooks, source, root, finalRoot);
            Directory.CreateDirectory(Path.Combine(root, "hooks"));
            File.WriteAllText(Path.Combine(root, "hooks", "hooks.json"), converted.ToJsonString());
            output["hooks"] = "./hooks/hooks.json";
            contributions++;
        }
        if (contributions == 0) throw new NotSupportedException("import_plugin_no_supported_content");
        var manifestPath = Path.Combine(root, ".craft-plugin", "plugin.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (PluginManifestParser.Load(root).Manifest == null) throw new InvalidDataException("Converted plugin manifest is invalid.");
        return partial || hooks != null;
    }

    private static JsonObject ExpandRoot(JsonObject value, string root)
    {
        JsonNode? Walk(JsonNode? node) => node switch
        {
            JsonObject obj => new JsonObject(obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Walk(p.Value)))),
            JsonArray array => new JsonArray(array.Select(Walk).ToArray()),
            JsonValue text when text.TryGetValue<string>(out var str) => JsonValue.Create(str.Replace("${CLAUDE_PLUGIN_ROOT}", root).Replace("${CURSOR_PLUGIN_ROOT}", root).Replace("${CODEX_PLUGIN_ROOT}", root)),
            _ => node?.DeepClone()
        };
        return (JsonObject)Walk(value)!;
    }
}
