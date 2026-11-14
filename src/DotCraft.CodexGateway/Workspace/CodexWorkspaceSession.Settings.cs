using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

public sealed partial class CodexWorkspaceSession
{
    private static string SourceEffort(string effort) => effort switch
    {
        "xhigh" => "extraHigh", _ => effort
    };

    internal static string CodexEffort(string effort) => effort switch
    {
        "extraHigh" => "xhigh", _ => effort
    };

    private async Task<Dictionary<string, object?>> ExecutionSettingsAsync(JsonElement p, CancellationToken ct, bool allowPolicy)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var entry in p.Get("config").ValueKind == JsonValueKind.Object
                     ? p.Get("config").EnumerateObject() : Enumerable.Empty<JsonProperty>())
            values[entry.Name] = entry.Value.Clone();
        foreach (var name in new[] { "model", "modelProvider", "effort", "serviceTier", "summary", "approvalPolicy", "sandbox", "permissions" })
            if (p.Get(name).ValueKind != JsonValueKind.Undefined) values[name] = p.Get(name).Clone();
        var collaboration = p.Get("collaborationMode");
        if (collaboration.ValueKind == JsonValueKind.Object)
        {
            values.Remove("model");
            values.Remove("effort");
            values["model"] = collaboration.Get("settings").Get("model");
            values["effort"] = collaboration.Get("settings").Get("reasoning_effort");
            if (collaboration.Get("settings").Text("developer_instructions") is not null)
                throw new ArgumentException("Custom collaboration developer instructions cannot be atomically replaced by this source.");
        }
        var configuration = default(JsonElement);
        if (p.Text("threadId") is not null &&
            values.Any(entry => (entry.Key is "approvalPolicy" or "approval_policy") &&
                entry.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)))
            configuration = (await RequestAsync("thread/read", new { threadId = ThreadId(p) }, ct)).Get("thread").Get("configuration");
        var settings = await ResolveSettingsAsync(values, ct, configuration);
        if (settings.GetValueOrDefault("speed") as string == "fast") await ValidateFastModeAsync(settings, p, ct);
        if (collaboration.ValueKind == JsonValueKind.Object)
        {
            settings["mode"] = collaboration.Required("mode") switch
            {
                "default" => "agent", "plan" => "plan",
                _ => throw new ArgumentException("Only default and plan collaboration modes are available.")
            };
            if (values["effort"].ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                var model = values["model"].GetString();
                if (model is null || !_models.TryGetValue(model, out var binding))
                    throw new ArgumentException("A collaboration mode requires a configured model.");
                settings["reasoning"] = new
                {
                    enabled = binding.Catalog.Get("reasoning").Flag("defaultEnabled") ||
                        binding.Catalog.Get("reasoning").Get("supportsDisable").ValueKind == JsonValueKind.False,
                    effort = SourceEffort(binding.Catalog.Get("reasoning").Text("defaultEffort") ?? "medium")
                };
            }
        }
        if (p.Text("approvalsReviewer") is { } reviewer && reviewer != "user")
            throw new ArgumentException("Only the user approval reviewer is supported.");
        ValidateSandboxPolicy(p.Get("sandboxPolicy"));
        p.Reject("permissionProfile");
        if (settings.ContainsKey("approvalPolicy") && !allowPolicy)
            throw new ArgumentException("Turn-level approval policy changes are not supported by atomic source admission.");
        return settings;
    }

    private async Task<Dictionary<string, object?>> ResolveSettingsAsync(
        Dictionary<string, JsonElement> values, CancellationToken ct, JsonElement configuration = default)
    {
        var settings = new Dictionary<string, object?>();
        string? model = null;
        string? provider = null;
        foreach (var (key, value) in values)
        {
            var name = key switch
            {
                "model_reasoning_effort" => "effort", "model_provider" => "modelProvider",
                "service_tier" => "serviceTier", "model_reasoning_summary" => "summary",
                "approval_policy" => "approvalPolicy", "sandbox_mode" => "sandbox",
                "default_permissions" => "permissions", "approvals_reviewer" => "approvalsReviewer", _ => key
            };
            if (name == "serviceTier" && value.ValueKind == JsonValueKind.Null)
            {
                settings["speed"] = "standard";
                continue;
            }
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            switch (name)
            {
                case "model": model = value.GetString(); break;
                case "modelProvider": provider = value.GetString(); break;
                case "effort":
                    var effort = value.GetString()!;
                    settings["reasoning"] = new { enabled = effort != "none", effort = SourceEffort(effort) };
                    break;
                case "serviceTier":
                    settings["speed"] = value.GetString() switch
                    {
                        "fast" => "fast", "default" => "standard",
                        _ => throw new ArgumentException("Only default and fast service tiers have a source equivalent.")
                    };
                    break;
                case "approvalPolicy":
                    if (await RequestedApprovalPolicyAsync(value.GetString()!, configuration, ct) is { } policy)
                        settings["approvalPolicy"] = policy;
                    break;
                case "sandbox":
                    CodexPermissionProfile.ValidateSandbox(value.GetString());
                    break;
                case "permissions":
                    CodexPermissionProfile.ValidateSelection(value.GetString());
                    break;
                case "approvalsReviewer":
                    if (value.GetString() != "user")
                        throw new ArgumentException("Only the user approval reviewer is supported.");
                    break;
                case "summary":
                    if (value.GetString() != "none")
                        throw new ArgumentException("Only summary none has a source equivalent.");
                    break;
                default: throw new ArgumentException("Unsupported configuration key: " + key);
            }
        }
        if (model is not null)
        {
            if (_models.Count == 0) await LoadModelsAsync(ct);
            if (!_models.TryGetValue(model, out var binding))
                throw new ArgumentException("Select a model ID returned by model/list for the selected workspace.");
            if (provider is not null && provider != binding.Provider)
                throw new ArgumentException("modelProvider conflicts with the selected model binding.");
            settings["providerId"] = binding.Provider;
            settings["model"] = binding.Model;
        }
        else if (provider is not null)
            throw new ArgumentException("Changing modelProvider requires a model binding.");
        return settings;
    }

    private async Task ValidateFastModeAsync(Dictionary<string, object?> settings, JsonElement p, CancellationToken ct)
    {
        if (_models.Count == 0) await LoadModelsAsync(ct);
        var provider = settings.GetValueOrDefault("providerId") as string;
        var model = settings.GetValueOrDefault("model") as string;
        if (model is null && p.Text("threadId") is not null)
        {
            var config = (await RequestAsync("thread/read", new { threadId = ThreadId(p) }, ct)).Get("thread").Get("configuration");
            provider = config.Text("providerId");
            model = config.Text("model");
        }
        var id = provider is not null && model is not null ? WorkspaceProjector.Binding(provider, model) : _defaultBinding;
        if (id is null || !_models.TryGetValue(id, out var binding) ||
            !binding.Catalog.Get("speed").Get("supportedModes").Rows().Any(mode => mode.GetString() == "fast"))
            throw new ArgumentException("The selected workspace model does not support fast service.");
    }

    private async Task<object> ModesAsync(CancellationToken ct)
    {
        await LoadModelsAsync(ct);
        var effort = _defaultEffort is { } value ? CodexEffort(value) : null;
        return new { data = new[]
        {
            new { name = "Agent", mode = "default", model = _defaultBinding, reasoning_effort = effort },
            new { name = "Plan", mode = "plan", model = _defaultBinding, reasoning_effort = effort }
        } };
    }

    private async Task<object> WriteConfigurationAsync(string method, JsonElement p, CancellationToken ct)
    {
        ValidateWorkspace(p);
        p.Reject("filePath");
        var edits = method == "config/value/write" ? new[] { p } : p.Get("edits").Rows().ToArray();
        var values = new Dictionary<string, JsonElement>();
        foreach (var edit in edits)
        {
            if (edit.Required("mergeStrategy") is not ("replace" or "upsert"))
                throw new ArgumentException("Unsupported configuration merge strategy.");
            values[edit.Required("keyPath")] = edit.Get("value").Clone();
        }
        await LoadModelsAsync(ct);
        var source = await ResolveSettingsAsync(values, ct);
        if (values.GetValueOrDefault("service_tier").ValueKind == JsonValueKind.Null) source.Remove("speed");
        if (source.GetValueOrDefault("speed") as string == "fast") await ValidateFastModeAsync(source, p, ct);
        var provider = source.GetValueOrDefault("providerId") as string ??
            (_defaultBinding is not null && _models.TryGetValue(_defaultBinding, out var current) ? current.Provider : null);
        if (edits.Length == 0) throw new ArgumentException("At least one configuration edit is required.");
        var mapped = new List<object>();
        foreach (var (key, value) in source)
        {
            var sourceKey = key switch
            {
                "providerId" => "ProviderId",
                "model" or "reasoning" or "speed" when provider is not null =>
                    "ProviderPreferences." + provider + "." + key,
                "approvalPolicy" => "Permissions.DefaultApprovalPolicy",
                _ => throw new ArgumentException("A configured provider is required for model preferences.")
            };
            var sourceValue = key == "approvalPolicy" && (string?)value == "prompt" ? "default" : value;
            mapped.Add(new { keyPath = sourceKey, value = sourceValue, mergeStrategy = "upsert" });
        }
        foreach (var (key, value) in values.Where(entry => entry.Value.ValueKind == JsonValueKind.Null))
        {
            if (key is "default_permissions" or "sandbox_mode" or "model_reasoning_summary" or "approvals_reviewer") continue;
            var sourceKey = key switch
            {
                "model" => "ProviderPreferences." + provider + ".model",
                "model_reasoning_effort" => "ProviderPreferences." + provider + ".reasoning.effort",
                "model_provider" => "ProviderId",
                "approval_policy" => "Permissions.DefaultApprovalPolicy",
                "service_tier" => "ProviderPreferences." + provider + ".speed",
                _ => throw new ArgumentException("Unsupported configuration key: " + key)
            };
            mapped.Add(new { keyPath = sourceKey, value = (object?)null, mergeStrategy = "replace" });
        }
        if (mapped.Count == 0) return await UnchangedConfigurationAsync(p, ct);
        var result = await RequestAsync("config/batchWrite", new
        {
            edits = mapped, expectedVersion = p.Text("expectedVersion")
        }, ct);
        _models.Clear();
        return result;
    }
}
