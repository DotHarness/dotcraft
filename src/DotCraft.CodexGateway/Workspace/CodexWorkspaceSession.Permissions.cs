using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private static string ApprovalPolicy(string? policy) => policy switch
    {
        "autoApprove" or "deny" => "never",
        "prompt" or "default" or null => "on-request",
        _ => throw new ArgumentException("Unsupported source approval policy.")
    };

    private async Task<string> EffectiveApprovalPolicyAsync(JsonElement configuration, CancellationToken ct)
    {
        var policy = configuration.Text("approvalPolicy");
        if (policy is null or "default")
        {
            var config = (await RequestAsync("config/read", new { }, ct)).Get("config");
            var value = Config(Config(config, "Permissions"), "DefaultApprovalPolicy");
            policy = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        return policy is null or "default" ? "prompt" : policy;
    }

    private static string RequestedApprovalPolicy(string policy) => policy switch
    {
        "never" => "autoApprove",
        "on-request" => "prompt",
        _ => throw new ArgumentException("Only never and on-request approval policies map to DotCraft.")
    };

    private static void ApplyPermissionOverrides(JsonElement p, Dictionary<string, JsonElement> values)
    {
        var sandbox = p.Get("sandbox").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        var sandboxPolicy = p.Get("sandboxPolicy").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        var permissions = p.Get("permissions").ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);
        if (permissions && (sandbox || sandboxPolicy))
            throw new ArgumentException("Permissions cannot be combined with sandbox or sandboxPolicy.");
        if (sandbox || sandboxPolicy || permissions)
            foreach (var key in new[] { "sandbox", "sandbox_mode", "permissions", "default_permissions" })
                values.Remove(key);
    }

    private async Task<object> UnchangedConfigurationAsync(JsonElement p, CancellationToken ct)
    {
        var source = await ReadConfigurationMetadataAsync(ct);
        var layer = source.Get("layers").Rows().Last(l => l.Get("name").Text("type") == "workspace");
        var version = layer.Required("version");
        if (p.Text("expectedVersion") is { } expected && expected != version)
            throw new ArgumentException("The workspace configuration changed since the expected version.");
        return new { status = "ok", version, filePath = layer.Get("name").Required("file"), overriddenMetadata = (object?)null };
    }
}
