using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.CodexGateway.Workspace;

namespace DotCraft.CodexGateway.Protocol;

public sealed partial class CodexGatewaySession
{
    private string ConfigurationPath(CodexWorkspace route) => Path.Combine(_stateDirectory, "projects", route.Id, "config.toml");

    private CodexWorkspace ConfigurationRoute(JsonElement p, IReadOnlyList<CodexWorkspace> workspaces)
    {
        if (p.Text("filePath") is not { } file) return CodexWorkspaceCatalog.Select(workspaces, p);
        var path = Path.GetFullPath(file);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(path, Path.Combine(_stateDirectory, "config.toml"), comparison)) return CodexWorkspaceCatalog.RequireDefault(workspaces);
        return workspaces.FirstOrDefault(w => string.Equals(path, ConfigurationPath(w), comparison))
            ?? throw new ArgumentException("Configuration file does not belong to an authorized project.");
    }

    private async Task<object> ProjectConfigurationAsync(
        object result, CodexWorkspace route, CodexWorkspaceSession backend, JsonElement p, CancellationToken ct)
    {
        var source = await backend.ReadConfigurationMetadataAsync(ct);
        var layer = source.Get("layers").Rows().LastOrDefault(l => l.Get("name").Text("type") == "workspace");
        var version = layer.Required("version");
        var node = JsonSerializer.SerializeToNode(result)!.AsObject();
        var name = new { type = "project", dotCodexFolder = Path.GetDirectoryName(ConfigurationPath(route))! };
        var origins = new JsonObject();
        foreach (var key in new[] { "model", "model_provider", "model_reasoning_effort", "service_tier", "approval_policy", "default_permissions", "sandbox_mode" })
            origins[key] = JsonSerializer.SerializeToNode(new { name, version });
        node["origins"] = origins;
        if (p.Flag("includeLayers"))
            node["layers"] = new JsonArray(JsonSerializer.SerializeToNode(new { name, version, config = node["config"]!.DeepClone() }));
        return node;
    }

    private static object UnboundConfiguration(JsonElement p)
    {
        var config = new Dictionary<string, object?>
        {
            ["model"] = null, ["model_provider"] = null, ["model_reasoning_effort"] = null,
            ["model_reasoning_summary"] = null, ["model_verbosity"] = null,
            ["service_tier"] = null, ["approval_policy"] = "on-request", ["approvals_reviewer"] = "user",
            ["sandbox_mode"] = "danger-full-access", ["sandbox_workspace_write"] = null,
            ["default_permissions"] = CodexPermissionProfile.Id,
            ["permissions"] = null, ["forced_chatgpt_workspace_id"] = null,
            ["forced_login_method"] = null, ["web_search"] = null,
            ["tools"] = null, ["instructions"] = null, ["developer_instructions"] = null,
            ["compact_prompt"] = null, ["personality"] = null,
            ["history"] = null, ["project_doc_max_bytes"] = null,
            ["project_root_markers"] = null, ["notify"] = null, ["profile"] = null,
            ["profiles"] = null, ["model_providers"] = null, ["features"] = null,
            ["mcp_servers"] = null, ["plugins"] = null, ["projects"] = null
        };
        var result = new Dictionary<string, object?> { ["config"] = config, ["origins"] = new { } };
        if (p.Flag("includeLayers")) result["layers"] = Array.Empty<object>();
        return result;
    }

    private object ProjectConfigurationWrite(object result, CodexWorkspace route)
    {
        var node = JsonSerializer.SerializeToNode(result)!.AsObject();
        node["filePath"] = ConfigurationPath(route);
        if (node["overriddenMetadata"]?["overridingLayer"] is JsonObject layer)
            layer["name"] = JsonSerializer.SerializeToNode(new
            {
                type = "project", dotCodexFolder = Path.GetDirectoryName(ConfigurationPath(route))!
            });
        return node;
    }
}
