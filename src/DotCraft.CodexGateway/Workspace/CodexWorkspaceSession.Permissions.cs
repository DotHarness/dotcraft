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

    private async Task<string?> RequestedApprovalPolicyAsync(string policy, JsonElement configuration, CancellationToken ct)
    {
        if (policy is not ("never" or "on-request"))
            throw new ArgumentException("Only never and on-request approval policies map to DotCraft.");
        var current = await EffectiveApprovalPolicyAsync(configuration, ct);
        if (ApprovalPolicy(current) == policy) return null;
        if (policy == "never")
            throw new ArgumentException("The source approval policy requires prompting; never cannot disable source approval authority.");
        return "prompt";
    }

    private static void ValidateSandboxPolicy(JsonElement sandbox)
    {
        if (sandbox.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        if (sandbox.Text("type") != "dangerFullAccess")
            throw new ArgumentException("Only the dangerFullAccess sandbox policy is available.");
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
