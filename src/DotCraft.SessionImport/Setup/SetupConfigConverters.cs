using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotCraft.SessionImport;

internal static class SetupConfigConverters
{
    private static readonly HashSet<string> HookEvents = new(StringComparer.Ordinal)
    {
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PermissionRequest", "PostToolUse",
        "PostToolUseFailure", "PreCompact", "PostCompact", "SubagentStart", "SubagentStop", "Stop", "StopFailure"
    };
    private static readonly Dictionary<string, string> CursorEvents = new(StringComparer.Ordinal)
    {
        ["preToolUse"] = "PreToolUse", ["postToolUse"] = "PostToolUse", ["preCompact"] = "PreCompact",
        ["postCompact"] = "PostCompact", ["sessionStart"] = "SessionStart", ["subagentStart"] = "SubagentStart",
        ["subagentStop"] = "SubagentStop", ["beforeSubmitPrompt"] = "UserPromptSubmit", ["stop"] = "Stop"
    };

    public static JsonObject Hooks(JsonObject input, string source, string sourceRoot, string targetRoot)
    {
        var result = new JsonObject();
        if (input["disableAllHooks"]?.GetValue<bool>() == true) return new JsonObject { ["hooks"] = result };
        if (input["hooks"] is not JsonObject events) return new JsonObject { ["hooks"] = result };
        foreach (var (name, value) in events)
        {
            var targetEvent = source == "cursor" ? CursorEvents.GetValueOrDefault(name) : name;
            if (targetEvent == null || !HookEvents.Contains(targetEvent) || value is not JsonArray groups)
                throw new NotSupportedException("import_hook_event_unsupported");
            var converted = new JsonArray();
            foreach (var node in groups)
            {
                if (node is not JsonObject group) throw new NotSupportedException("import_hook_unsupported");
                var handlers = source == "cursor" ? new JsonArray(group.DeepClone()) : group["hooks"] as JsonArray;
                if (handlers == null) throw new NotSupportedException("import_hook_unsupported");
                var commands = new JsonArray();
                foreach (var handler in handlers.OfType<JsonObject>())
                {
                    if (handler["type"]?.GetValue<string>() is { } type && type != "command")
                        throw new NotSupportedException("import_hook_type_unsupported");
                    var command = handler["command"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(command)) throw new NotSupportedException("import_hook_unsupported");
                    var allowed = new HashSet<string>(["type", "command", "timeout", "timeoutSec", "matcher", "statusMessage", "failClosed", "async", "shell", "environmentVariables"], StringComparer.Ordinal);
                    if (handler.Any(p => !allowed.Contains(p.Key))) throw new NotSupportedException("import_hook_field_unsupported");
                    var payload = new JsonObject { ["type"] = "command", ["command"] = ImportHookPathRewrite.Apply(command, sourceRoot, targetRoot) };
                    foreach (var key in new[] { "timeout", "async", "shell", "environmentVariables" })
                        if (handler[key] != null) payload[key] = handler[key]!.DeepClone();
                    if (handler["timeoutSec"] != null && payload["timeout"] == null) payload["timeout"] = handler["timeoutSec"]!.DeepClone();
                    if (handler["statusMessage"] is JsonValue message) payload["statusMessage"] = ImportTextRewrite.Apply(message.GetValue<string>(), source);
                    commands.Add(payload);
                }
                if (commands.Count == 0) continue;
                var mapped = new JsonObject { ["hooks"] = commands };
                if (group["matcher"] != null) mapped["matcher"] = group["matcher"]!.DeepClone();
                converted.Add(mapped);
            }
            if (converted.Count > 0) result[targetEvent] = converted;
        }
        return new JsonObject { ["hooks"] = result };
    }

    public static JsonObject? Mcp(JsonObject input, bool codex)
    {
        if (input["disabled"]?.GetValue<bool>() == true || input["enabled"]?.GetValue<bool>() == false) return null;
        if (input.Any(pair => pair.Key is "enabled_tools" or "disabled_tools" or "approval_policy" or "tools" or "oauth"))
            throw new NotSupportedException("import_mcp_policy_unsupported");
        var result = new JsonObject { ["Enabled"] = true };
        var command = input["command"]?.GetValue<string>();
        var url = input["url"]?.GetValue<string>();
        var type = input["type"]?.GetValue<string>();
        if (command != null)
        {
            if (type != null && type != "stdio") throw new NotSupportedException("import_mcp_transport_unsupported");
            result["Transport"] = "stdio";
            result["Command"] = command;
            Copy(input, "args", result, "Arguments");
            Copy(input, "cwd", result, "Cwd");
        }
        else if (url != null)
        {
            if (type != null && type is not ("http" or "streamable_http" or "streamable-http"))
                throw new NotSupportedException("import_mcp_transport_unsupported");
            result["Transport"] = "http";
            result["Url"] = url;
        }
        else throw new NotSupportedException("import_mcp_transport_unsupported");
        if (ContainsPlaceholder(command) || ContainsPlaceholder(url)
            || input["args"] is JsonArray args && args.Any(a => ContainsPlaceholder(a?.GetValue<string>())))
            throw new NotSupportedException("import_mcp_variable_unsupported");
        var env = new JsonObject();
        var envVars = input["env_vars"]?.DeepClone() as JsonArray ?? new JsonArray();
        if (input["env"] is JsonObject sourceEnv)
            foreach (var (key, node) in sourceEnv)
            {
                var text = node?.GetValue<string>() ?? "";
                if (Variable(text) == key) envVars.Add(key);
                else if (ContainsPlaceholder(text)) throw new NotSupportedException("import_mcp_variable_unsupported");
                else env[key] = text;
            }
        result["EnvironmentVariables"] = env;
        result["EnvVars"] = envVars;
        var headers = new JsonObject();
        var envHeaders = input["env_http_headers"]?.DeepClone() as JsonObject ?? new JsonObject();
        if (input[codex ? "http_headers" : "headers"] is JsonObject sourceHeaders)
            foreach (var (key, node) in sourceHeaders)
            {
                var text = node?.GetValue<string>() ?? "";
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && text.StartsWith("Bearer ") && Variable(text[7..]) is { } bearer)
                    result["BearerTokenEnvVar"] = bearer;
                else if (Variable(text) is { } variable) envHeaders[key] = variable;
                else if (ContainsPlaceholder(text)) throw new NotSupportedException("import_mcp_variable_unsupported");
                else headers[key] = text;
            }
        result["Headers"] = headers;
        result["EnvHttpHeaders"] = envHeaders;
        Copy(input, "bearer_token_env_var", result, "BearerTokenEnvVar");
        Copy(input, "startup_timeout_sec", result, "StartupTimeoutSec");
        Copy(input, "tool_timeout_sec", result, "ToolTimeoutSec");
        return result;
    }

    public static bool NeedsEnvironment(JsonObject config)
    {
        var names = (config["EnvVars"] as JsonArray)?.Select(v => v!.GetValue<string>()).ToList() ?? [];
        if (config["EnvHttpHeaders"] is JsonObject headers) names.AddRange(headers.Select(p => p.Value!.GetValue<string>()));
        if (config["BearerTokenEnvVar"] is JsonValue token) names.Add(token.GetValue<string>());
        return names.Any(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name)));
    }

    private static string? Variable(string text) => Regex.Match(text, @"^\$\{(?:env:)?([A-Za-z_][A-Za-z0-9_]*)\}$") is { Success: true } match ? match.Groups[1].Value : null;
    private static bool ContainsPlaceholder(string? text) => text?.Contains("${", StringComparison.Ordinal) == true;
    private static void Copy(JsonObject source, string key, JsonObject target, string name)
    {
        if (source[key] != null) target[name] = source[key]!.DeepClone();
    }
}
