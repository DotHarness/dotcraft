using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Agents;

namespace DotCraft.Auth.OpenAI;

public static class OpenAIAuthBindingPersistence
{
    /// <summary>
    /// Marks the provider with <paramref name="providerId"/> as using ChatGPT OAuth and records the
    /// account id / plan tier returned by login. Creates the provider entry if absent.
    /// </summary>
    /// <param name="globalConfigPath">Full path selected by the host.</param>
    /// <param name="defaultModel">Model to remember for this provider when none is configured.</param>
    public static void BindProviderToOAuth(
        string providerId,
        ProviderAuthenticationStatus status,
        string globalConfigPath,
        string defaultModel = ModelProviderDefaults.DefaultChatGptCodexModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);
        AtomicConfigDocument.Update(Path.GetFullPath(globalConfigPath), root =>
            Apply(root, BindEdits(root, providerId, status, defaultModel)));
    }

    /// <summary>Reverts the provider to API-key auth and clears account metadata.</summary>
    public static void UnbindProvider(string providerId, string globalConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);
        AtomicConfigDocument.Update(Path.GetFullPath(globalConfigPath), root =>
            Apply(root, UnbindEdits(root, providerId)));
    }

    public static IReadOnlyList<ConfigEdit> BindEdits(
        JsonObject userLayer,
        string providerId,
        ProviderAuthenticationStatus status,
        string defaultModel = ModelProviderDefaults.DefaultChatGptCodexModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(status);

        var providers = AtomicConfigDocument.Value(userLayer, ProviderConfiguration.KeyPath) as JsonObject;
        var existingKey = providers == null ? null : AtomicConfigDocument.Key(providers, providerId);
        var id = existingKey ?? providerId;
        var edits = new List<ConfigEdit>();
        if (existingKey == null || providers![existingKey] is not JsonObject)
        {
            edits.Add(new(ProviderConfiguration.ProviderKeyPath(id), new JsonObject { ["DisplayName"] = "OpenAI (ChatGPT)" }));
        }

        edits.Add(new(ProviderConfiguration.ProviderKeyPath(id, "AuthMethod"), ModelProviderAuthMethods.ChatGptOAuth));
        edits.Add(new(ProviderConfiguration.ProviderKeyPath(id, "Protocol"), ModelProviderProtocols.OpenAIResponses));
        edits.Add(new(ProviderConfiguration.ProviderKeyPath(id, "ChatGptAccountId"),
            string.IsNullOrEmpty(status.AccountId) ? null : status.AccountId));
        edits.Add(new(ProviderConfiguration.ProviderKeyPath(id, "ChatGptPlanType"),
            string.IsNullOrEmpty(status.PlanType) ? null : status.PlanType));

        if (string.IsNullOrWhiteSpace(GetStringValue(userLayer, "ProviderId")))
            edits.Add(new("ProviderId", id));
        var providerPreferences = AtomicConfigDocument.Value(userLayer, "ProviderPreferences") as JsonObject;
        var preferenceKey = providerPreferences == null ? null : AtomicConfigDocument.Key(providerPreferences, id);
        var preference = preferenceKey == null ? null : providerPreferences![preferenceKey] as JsonObject;
        if (string.IsNullOrWhiteSpace(preference == null ? null : GetStringValue(preference, "Model")))
        {
            edits.Add(new(
                ConfigJsonPath.Join("ProviderPreferences", preferenceKey ?? id),
                JsonSerializer.SerializeToNode(ModelPreferenceRules.CreateManual(defaultModel), AppConfig.SerializerOptions)));
        }

        return edits;
    }

    public static IReadOnlyList<ConfigEdit> UnbindEdits(JsonObject userLayer, string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (AtomicConfigDocument.Value(userLayer, ProviderConfiguration.KeyPath) is not JsonObject providers
            || AtomicConfigDocument.Key(providers, providerId) is not { } id
            || providers[id] is not JsonObject)
        {
            return [];
        }

        return
        [
            new(ProviderConfiguration.ProviderKeyPath(id, "AuthMethod"), ModelProviderAuthMethods.ApiKey),
            new(ProviderConfiguration.ProviderKeyPath(id, "ChatGptAccountId"), null),
            new(ProviderConfiguration.ProviderKeyPath(id, "ChatGptPlanType"), null)
        ];
    }

    private static void Apply(JsonObject root, IEnumerable<ConfigEdit> edits)
    {
        foreach (var edit in edits)
        {
            var path = ConfigJsonPath.Split(edit.KeyPath);
            if (edit.Value == null)
                ConfigJsonPath.Remove(root, path);
            else
                ConfigJsonPath.Set(root, path, edit.Value.DeepClone());
        }
    }

    private static string? GetStringValue(JsonObject node, string key) =>
        AtomicConfigDocument.Value(node, key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
