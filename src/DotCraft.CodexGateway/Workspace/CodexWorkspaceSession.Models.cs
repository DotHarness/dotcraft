using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private sealed record ModelBinding(string Provider, string Model, JsonElement Catalog);
    private readonly Dictionary<string, ModelBinding> _models = new(StringComparer.Ordinal);
    private string? _defaultBinding;
    private string? _defaultEffort;
    private string? _defaultPolicy;
    private string? _defaultSpeed;

    private async Task LoadModelsAsync(CancellationToken ct)
    {
        var configuration = (await RequestAsync("config/read", new { }, ct)).Get("config");
        var providerValue = Config(configuration, "ProviderId");
        var provider = providerValue.ValueKind == JsonValueKind.String ? providerValue.GetString() : null;
        var preferences = Config(configuration, "ProviderPreferences");
        var preference = preferences.Get(provider ?? "");
        var model = Config(preference, "model").ValueKind == JsonValueKind.String ? Config(preference, "model").GetString() : null;
        _defaultBinding = provider is not null && model is not null ? WorkspaceProjector.Binding(provider, model) : null;
        _defaultEffort = Config(Config(preference, "reasoning"), "effort").ValueKind == JsonValueKind.String
            ? Config(Config(preference, "reasoning"), "effort").GetString() : null;
        if (Config(Config(preference, "reasoning"), "enabled").ValueKind == JsonValueKind.False) _defaultEffort = "none";
        _defaultPolicy = ApprovalPolicy(Config(Config(configuration, "Permissions"), "DefaultApprovalPolicy").ValueKind == JsonValueKind.String
            ? Config(Config(configuration, "Permissions"), "DefaultApprovalPolicy").GetString() : null);
        _defaultSpeed = Config(preference, "speed").ValueKind == JsonValueKind.String ? Config(preference, "speed").GetString() : null;
        var providers = await RequestAsync("provider/list", new { }, ct);
        _models.Clear();
        foreach (var entry in providers.Get("providers").Rows())
        {
            var id = entry.Required("id");
            var catalog = await RequestAsync("model/list", new { providerId = id }, ct);
            if (catalog.Get("success").ValueKind == JsonValueKind.False) continue;
            foreach (var item in catalog.Get("models").Rows())
            {
                var name = item.Required("id");
                _models.Add(WorkspaceProjector.Binding(id, name), new(id, name, item.Clone()));
            }
        }
    }

    private static JsonElement Config(JsonElement config, string name) =>
        config.ValueKind == JsonValueKind.Object
            ? config.EnumerateObject().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value
            : default;

    private async Task<object> ModelsAsync(JsonElement p, CancellationToken ct)
    {
        await LoadModelsAsync(ct);
        var modelScope = "models:" + (p.Text("modelProvider") ?? "");
        var cursor = DecodeCursor(p.Text("cursor"), modelScope, null);
        var offset = cursor?.Offset ?? 0;
        var limit = p.Limit(100);
        var bindings = _models.Where(m => p.Text("modelProvider") is not { } provider || m.Value.Provider == provider)
            .OrderBy(m => m.Key, StringComparer.Ordinal).ToArray();
        var data = bindings.Skip(offset).Take(limit).Select(pair =>
        {
            var m = pair.Value.Catalog;
            var reasoning = m.Get("reasoning");
            var effort = reasoning.Flag("defaultEnabled") || reasoning.Get("supportsDisable").ValueKind == JsonValueKind.False
                ? reasoning.Text("defaultEffort") ?? "medium" : "none";
            return new
            {
                id = pair.Key, model = pair.Key,
                displayName = (m.Text("displayName") ?? pair.Value.Model) + " · " + pair.Value.Provider,
                description = "DotCraft provider: " + pair.Value.Provider,
                upgrade = (object?)null, upgradeInfo = (object?)null, availabilityNux = (object?)null,
                hidden = false, modelSpecialty = (object?)null,
                supportedReasoningEfforts = reasoning.Get("supportedEfforts").Rows()
                    .Select(e => new { reasoningEffort = CodexEffort(e.Required("effort")), description = e.Text("description") ?? e.Required("effort") })
                    .Concat(reasoning.Flag("supportsDisable") ? new[] { new { reasoningEffort = "none", description = "Disabled" } } : [])
                    .DistinctBy(e => e.reasoningEffort).ToArray(),
                defaultReasoningEffort = CodexEffort(effort),
                inputModalities = m.Get("inputModalities").Rows().Select(v => v.GetString())
                    .Where(v => v is "text" or "image").ToArray(),
                supportsPersonality = false, multiAgentVersion = (object?)null,
                additionalSpeedTiers = m.Get("speed").Get("supportedModes").Rows().Any(v => v.GetString() == "fast")
                    ? new[] { "fast" } : Array.Empty<string>(), serviceTiers = Array.Empty<object>(),
                defaultServiceTier = (string?)null, availableAccessPrograms = (object?)null,
                isDefault = pair.Key == _defaultBinding
            };
        }).ToArray();
        return new
        {
            data,
            nextCursor = offset + data.Length < bindings.Length
                ? EncodeCursor(new(modelScope, null, null, "asc", null, offset + data.Length)) : null
        };
    }

    private async Task<object> ConfigurationAsync(JsonElement p, CancellationToken ct)
    {
        ValidateWorkspace(p);
        await LoadModelsAsync(ct);
        var config = new Dictionary<string, object?>
        {
            ["model"] = _defaultBinding,
            ["model_provider"] = _defaultBinding is { } id && _models.TryGetValue(id, out var binding) ? binding.Provider : null,
            ["model_reasoning_effort"] = _defaultEffort is { } effort ? CodexEffort(effort) : null,
            ["model_reasoning_summary"] = null, ["model_verbosity"] = null,
            ["service_tier"] = _defaultSpeed == "fast" ? "fast" : null, ["approval_policy"] = _defaultPolicy, ["approvals_reviewer"] = "user",
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
}
