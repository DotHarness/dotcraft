using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Agents;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Helpers that mutate a host-selected config file when the user binds or unbinds a provider.
/// </summary>
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
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(status);

        ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);
        AtomicConfigDocument.Update(Path.GetFullPath(globalConfigPath), root =>
        {
            var providers = AtomicConfigDocument.Object(root, "Providers");
            var (canonicalKey, providerNode) = GetOrCreateProvider(providers, providerId);

            providerNode["AuthMethod"] = ModelProviderAuthMethods.ChatGptOAuth;
            providerNode["Protocol"] = ModelProviderProtocols.OpenAIResponses;
            if (!string.IsNullOrEmpty(status.AccountId))
                providerNode["ChatGptAccountId"] = status.AccountId;
            else
                providerNode.Remove("ChatGptAccountId");
            if (!string.IsNullOrEmpty(status.PlanType))
                providerNode["ChatGptPlanType"] = status.PlanType;
            else
                providerNode.Remove("ChatGptPlanType");

            if (string.IsNullOrWhiteSpace(GetStringValue(root, "ProviderId")))
                root["ProviderId"] = canonicalKey;
            var providerPreferences = AtomicConfigDocument.Object(root, "ProviderPreferences");
            var preferenceKey = AtomicConfigDocument.Key(providerPreferences, canonicalKey);
            var preference = preferenceKey == null ? null : providerPreferences[preferenceKey] as JsonObject;
            var existingModel = preference == null ? null : GetStringValue(preference, "Model");
            if (string.IsNullOrWhiteSpace(existingModel))
            {
                providerPreferences[preferenceKey ?? canonicalKey] = JsonSerializer.SerializeToNode(
                    ModelPreferenceRules.CreateManual(defaultModel),
                    AppConfig.SerializerOptions);
            }
        });
    }

    /// <summary>Reverts the provider to API-key auth and clears account metadata.</summary>
    public static void UnbindProvider(string providerId, string globalConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        ArgumentException.ThrowIfNullOrWhiteSpace(globalConfigPath);
        AtomicConfigDocument.Update(Path.GetFullPath(globalConfigPath), root =>
        {
            if (AtomicConfigDocument.Value(root, "Providers") is not JsonObject providers
                || AtomicConfigDocument.Value(providers, providerId) is not JsonObject providerNode)
            {
                return;
            }

            providerNode["AuthMethod"] = ModelProviderAuthMethods.ApiKey;
            providerNode.Remove("ChatGptAccountId");
            providerNode.Remove("ChatGptPlanType");
        });
    }

    private static (string CanonicalKey, JsonObject Node) GetOrCreateProvider(JsonObject providers, string providerId)
    {
        if (AtomicConfigDocument.Key(providers, providerId) is { } key && providers[key] is JsonObject existing)
            return (key, existing);

        var node = new JsonObject
        {
            ["DisplayName"] = "OpenAI (ChatGPT)",
            ["Protocol"] = ModelProviderProtocols.OpenAIResponses
        };
        providers[providerId] = node;
        return (providerId, node);
    }

    private static string? GetStringValue(JsonObject node, string key) =>
        AtomicConfigDocument.Value(node, key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
