using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using Tomlyn;

namespace DotCraft.SessionImport;

internal sealed class SetupImportScanner(SetupImportPaths paths)
{
    public IReadOnlyList<SetupImportItem> Scan(string source)
    {
        var result = new List<SetupImportItem>();
        if (!paths.SourceRoots.TryGetValue(source, out var home)) return result;
        foreach (var scope in new[] { "user", "workspace" })
        {
            var root = scope == "user" ? home : Path.Combine(paths.Workspace, source switch { "claude-code" => ".claude", "codex" => ".codex", _ => ".cursor" });
            foreach (var category in ImportCategories.Setup)
            {
                if (category == "plugins" && scope != "user") continue;
                try { ScanCategory(result, source, scope, root, category); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    result.Add(Create(source, scope, category, root, "", category, state: "unsupported",
                        reason: ex is NotSupportedException ? ex.Message : "import_source_invalid"));
                }
            }
        }
        return result.DistinctBy(item => item.Candidate.SourceId).ToArray();
    }

    private void ScanCategory(List<SetupImportItem> result, string source, string scope, string root, string category)
    {
        var target = paths.Root(scope);
        switch (category)
        {
            case "skills":
                var skillRoots = new List<string> { Path.Combine(root, "skills") };
                if (source == "cursor" && scope == "user") skillRoots.Add(Path.Combine(root, "skills-cursor"));
                if (source == "codex") skillRoots.Add(Path.Combine(scope == "user" ? paths.Home : paths.Workspace, ".agents", "skills"));
                foreach (var skillRoot in skillRoots)
                {
                    if (!Directory.Exists(skillRoot)) continue;
                    AtomicConfigDocument.RejectLinks(skillRoot);
                    foreach (var skill in Directory.EnumerateDirectories(skillRoot).Order(StringComparer.Ordinal))
                    {
                        if (!File.Exists(Path.Combine(skill, "SKILL.md"))) continue;
                        var name = Path.GetFileName(skill);
                        var destination = Path.Combine(target, "skills", name);
                        var shared = Path.Combine(paths.Home, ".agents", "skills", name, "SKILL.md");
                        var present = Directory.Exists(destination) || scope == "user" && SharedSkillsEnabled() && File.Exists(shared);
                        AddItem(result, source, scope, category, skill, destination, name,
                            () => Create(source, scope, category, skill, destination, name, directory: skill, state: present ? "current" : "new"));
                    }
                }
                break;
            case "instructions":
                var instruction = source switch
                {
                    "claude-code" => scope == "user" ? Path.Combine(root, "CLAUDE.md")
                        : File.Exists(Path.Combine(paths.Workspace, "CLAUDE.md")) ? Path.Combine(paths.Workspace, "CLAUDE.md") : Path.Combine(root, "CLAUDE.md"),
                    "cursor" => scope == "workspace" ? Path.Combine(paths.Workspace, ".cursorrules") : "",
                    _ => Path.Combine(scope == "user" ? root : paths.Workspace, "AGENTS.md")
                };
                if (!File.Exists(instruction)) break;
                var instructionTarget = Path.Combine(scope == "user" ? target : paths.Workspace, "AGENTS.md");
                var overridePath = Path.Combine(Path.GetDirectoryName(instructionTarget)!, "AGENTS.override.md");
                result.Add(Create(source, scope, category, instruction, instructionTarget, "AGENTS.md",
                    text: ImportTextRewrite.Apply(ReadText(instruction), source),
                    state: NonEmpty(instructionTarget) || NonEmpty(overridePath) ? "current" : "new"));
                break;
            case "commands":
                if (source == "codex") break;
                var commands = Path.Combine(root, "commands");
                if (!Directory.Exists(commands)) break;
                PluginContentTree.Fingerprint(commands, []);
                foreach (var file in Directory.EnumerateFiles(commands, "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(commands, file);
                    var destination = Path.Combine(target, "commands", relative);
                    var text = ReadText(file);
                    var supported = ImportTextRewrite.SupportedCommand(text);
                    result.Add(Create(source, scope, category, file, destination, Path.ChangeExtension(relative, null).Replace('\\', ':').Replace('/', ':'),
                        text: ImportTextRewrite.Apply(text, source), state: !supported ? "unsupported" : File.Exists(destination) ? "current" : "new",
                        reason: supported ? "" : "import_command_semantics_unsupported"));
                }
                break;
            case "hooks":
                var hookFile = Path.Combine(root, source == "claude-code" ? "settings.json" : "hooks.json");
                var settings = source == "claude-code" ? ClaudeSettings(root) : ReadJson(hookFile);
                if (settings["hooks"] is not JsonObject { Count: > 0 }) break;
                var hooks = SetupConfigConverters.Hooks(settings, source, root, target);
                if (hooks["hooks"] is not JsonObject { Count: > 0 }) break;
                var hooksTarget = Path.Combine(target, "hooks.json");
                result.Add(Create(source, scope, category, hookFile, hooksTarget, "Hooks", config: hooks,
                    scripts: Directory.Exists(Path.Combine(root, "hooks")) ? Path.Combine(root, "hooks") : null,
                    state: NonEmpty(hooksTarget) ? "current" : "new"));
                break;
            case "mcp":
                var servers = McpServers(source, scope, root);
                var configPath = Path.Combine(target, "config.json");
                var document = AtomicConfigDocument.Read(configPath);
                var existing = document[AtomicConfigDocument.Key(document, "McpServers") ?? "McpServers"] as JsonObject;
                var enabledSettings = source == "claude-code" ? ClaudeSettings(root) : new JsonObject();
                foreach (var (name, node) in servers)
                {
                    if (node is not JsonObject declaration) continue;
                    if (enabledSettings["disabledMcpjsonServers"] is JsonArray disabled && disabled.Any(n => n?.GetValue<string>() == name)) continue;
                    if (enabledSettings["enabledMcpjsonServers"] is JsonArray { Count: > 0 } enabled && !enabled.Any(n => n?.GetValue<string>() == name)) continue;
                    try
                    {
                        var converted = SetupConfigConverters.Mcp(declaration, source == "codex");
                        if (converted == null) continue;
                        result.Add(Create(source, scope, category, root, configPath, name, config: converted,
                            state: existing != null && AtomicConfigDocument.Key(existing, name) != null ? "current" : "new"));
                    }
                    catch (NotSupportedException ex)
                    {
                        result.Add(Create(source, scope, category, root, configPath, name, state: "unsupported", reason: ex.Message));
                    }
                }
                break;
            case "plugins":
                foreach (var plugin in ExternalPluginSource.Discover(source, root))
                {
                    var destination = Path.Combine(target, "plugins", plugin.Id);
                    AddItem(result, source, scope, category, plugin.ManifestPath, destination, plugin.Id,
                        () => Create(source, scope, category, plugin.ManifestPath, destination, plugin.Id,
                            directory: plugin.LocalRoot, plugin: plugin, state: plugin.ErrorCode != null ? "unsupported" : Directory.Exists(destination) ? "current" : "new",
                            reason: plugin.ErrorCode ?? ""));
                }
                break;
        }
    }

    private JsonObject McpServers(string source, string scope, string root)
    {
        if (source == "codex") return ReadToml(Path.Combine(root, "config.toml"))["mcp_servers"] as JsonObject ?? new();
        if (source == "cursor") return ReadJson(Path.Combine(root, "mcp.json"))["mcpServers"] as JsonObject ?? new();
        var servers = new JsonObject();
        var sourceHome = Path.GetDirectoryName(paths.SourceRoots[source])!;
        var scopeRoot = scope == "user" ? sourceHome : paths.Workspace;
        foreach (var file in new[] { Path.Combine(scopeRoot, ".mcp.json"), Path.Combine(scopeRoot, ".claude.json") })
        {
            var document = ReadJson(file);
            MergeServers(servers, document["mcpServers"] as JsonObject, true);
            MergeProject(servers, document, scopeRoot, true);
        }
        if (scope == "workspace") MergeProject(servers, ReadJson(Path.Combine(sourceHome, ".claude.json")), scopeRoot, false);
        return servers;
    }

    private bool SharedSkillsEnabled()
    {
        var enabled = true;
        foreach (var directory in new[] { paths.UserData, paths.Data })
        {
            var config = ReadJson(Path.Combine(directory, "config.json"));
            if (config[AtomicConfigDocument.Key(config, "Skills") ?? "Skills"] is JsonObject skills
                && skills[AtomicConfigDocument.Key(skills, "IncludeSharedSkills") ?? "IncludeSharedSkills"] is JsonValue value)
                enabled = value.GetValue<bool>();
        }
        return enabled;
    }

    private static void MergeProject(JsonObject target, JsonObject root, string path, bool overwrite)
    {
        if (root["projects"] is not JsonObject projects) return;
        foreach (var (name, value) in projects)
            if (Path.IsPathFullyQualified(name) && WorkspacePathMatcher.IsSameDirectory(name, path))
                MergeServers(target, value?["mcpServers"] as JsonObject, overwrite);
    }

    private static void MergeServers(JsonObject target, JsonObject? source, bool overwrite)
    {
        if (source == null) return;
        foreach (var (key, value) in source)
            if (overwrite || !target.ContainsKey(key)) target[key] = value?.DeepClone();
    }

    internal static JsonObject ClaudeSettings(string root)
    {
        var result = ReadJson(Path.Combine(root, "settings.json"));
        var local = ReadJson(Path.Combine(root, "settings.local.json"));
        foreach (var (key, value) in local)
        {
            if (key == "hooks" && value is JsonObject localHooks && result[key] is JsonObject hooks)
            {
                foreach (var (eventName, groups) in localHooks)
                    if (groups is JsonArray incoming && hooks[eventName] is JsonArray existing)
                        foreach (var group in incoming) existing.Add(group?.DeepClone());
                    else hooks[eventName] = groups?.DeepClone();
            }
            else result[key] = value?.DeepClone();
        }
        return result;
    }

    internal static JsonObject ReadJson(string path) => AtomicConfigDocument.Read(path);
    internal static JsonObject ReadToml(string path) => !File.Exists(path) ? new JsonObject()
        : JsonSerializer.SerializeToNode(Toml.ToModel(ReadText(path))) as JsonObject ?? throw new InvalidDataException("Invalid TOML document.");
    internal static string ReadText(string path) { AtomicConfigDocument.RejectLinks(path); return File.ReadAllText(path); }
    internal static bool NonEmpty(string path) => File.Exists(path) && !string.IsNullOrWhiteSpace(ReadText(path));

    private static SetupImportItem Create(string source, string scope, string category, string sourcePath,
        string target, string title, string? text = null, JsonObject? config = null, string? directory = null,
        string? scripts = null, ExternalPluginSource? plugin = null, string state = "new", string reason = "")
    {
        var identity = $"{source}\n{scope}\n{category}\n{title}";
        var content = text ?? config?.ToJsonString() ?? plugin?.ToString() ?? "";
        var directoryHash = directory == null ? null : PluginContentTree.Fingerprint(directory, []);
        var scriptsHash = scripts == null ? null : PluginContentTree.Fingerprint(scripts, []);
        content += directoryHash;
        content += scriptsHash;
        return new SetupImportItem(new ImportCandidate
        {
            Source = source, SourceId = category + "_" + Hash(identity), Category = category, Scope = scope,
            Fingerprint = Hash(content), SourcePath = sourcePath, TargetPath = target, Title = title,
            Cwd = "", UpdatedAt = DateTimeOffset.UnixEpoch, TurnCount = 0, State = state, Reason = reason,
            FallbackText = reason.Length == 0 ? "" : ImportDiagnostics.Fallback(reason)
        }, text, config, directory, scripts, plugin, directoryHash, scriptsHash);
    }

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static void AddItem(List<SetupImportItem> result, string source, string scope, string category,
        string path, string target, string title, Func<SetupImportItem> create)
    {
        try { result.Add(create()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            result.Add(Create(source, scope, category, path, target, title, state: "unsupported", reason: "import_source_invalid"));
        }
    }
}
