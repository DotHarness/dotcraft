using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;

namespace DotCraft.Auth.OpenAI;

/// <summary>
/// Helpers that mutate the global <c>~/.craft/config.json</c> when the user binds or unbinds a
/// provider to/from ChatGPT subscription auth. Shared between the CLI and AppServer JSON-RPC handler.
/// </summary>
public static class OpenAIAuthBindingPersistence
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultGlobalConfigPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".craft",
        "config.json");

    /// <summary>
    /// Marks the provider with <paramref name="providerId"/> as using ChatGPT OAuth and records the
    /// account id / plan tier returned by login. Creates the provider entry if absent.
    /// </summary>
    /// <param name="globalConfigPath">Full path; defaults to <see cref="DefaultGlobalConfigPath"/>.</param>
    /// <param name="defaultModel">Model to remember for this provider when none is configured.</param>
    public static void BindProviderToOAuth(
        string providerId,
        OpenAIAuthStatus status,
        string? globalConfigPath = null,
        string defaultModel = ModelProviderDefaults.DefaultChatGptCodexModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(status);

        var path = globalConfigPath ?? DefaultGlobalConfigPath();
        var root = LoadOrCreate(path);
        var providers = GetOrCreateObject(root, "Providers");
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

        // Make this provider the default if no provider is configured yet.
        if (string.IsNullOrWhiteSpace(GetStringValue(root, "ProviderId")))
            root["ProviderId"] = canonicalKey;
        var providerPreferences = GetOrCreateObject(root, "ProviderPreferences");
        var preferenceMatch = providerPreferences
            .FirstOrDefault(p => string.Equals(p.Key, canonicalKey, StringComparison.OrdinalIgnoreCase));
        var preference = preferenceMatch.Value as JsonObject;
        var existingModel = preference == null ? null : GetStringValue(preference, "Model");
        if (string.IsNullOrWhiteSpace(existingModel))
        {
            providerPreferences[preferenceMatch.Key ?? canonicalKey] = JsonSerializer.SerializeToNode(
                ModelPreferenceRules.CreateManual(defaultModel),
                AppConfig.SerializerOptions);
        }

        Save(path, root);
    }

    /// <summary>Reverts the provider to API-key auth and clears account metadata.</summary>
    public static void UnbindProvider(string providerId, string? globalConfigPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        var path = globalConfigPath ?? DefaultGlobalConfigPath();
        if (!File.Exists(path))
            return;

        var root = LoadOrCreate(path);
        if (root["Providers"] is not JsonObject providers)
            return;

        var matched = providers.FirstOrDefault(p => string.Equals(p.Key, providerId, StringComparison.OrdinalIgnoreCase));
        if (matched.Value is not JsonObject providerNode)
            return;

        providerNode["AuthMethod"] = ModelProviderAuthMethods.ApiKey;
        providerNode.Remove("ChatGptAccountId");
        providerNode.Remove("ChatGptPlanType");
        Save(path, root);
    }

    private static JsonObject LoadOrCreate(string path) =>
        File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? new JsonObject()
            : new JsonObject();

    private static void Save(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, root.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string key)
    {
        var matched = parent.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (matched.Value is JsonObject existing)
            return existing;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    private static (string CanonicalKey, JsonObject Node) GetOrCreateProvider(JsonObject providers, string providerId)
    {
        var matched = providers.FirstOrDefault(p => string.Equals(p.Key, providerId, StringComparison.OrdinalIgnoreCase));
        if (matched.Value is JsonObject existing)
            return (matched.Key, existing);

        var node = new JsonObject
        {
            ["DisplayName"] = "OpenAI (ChatGPT)",
            ["Protocol"] = ModelProviderProtocols.OpenAIResponses
        };
        providers[providerId] = node;
        return (providerId, node);
    }

    private static string? GetStringValue(JsonObject node, string key)
    {
        var matched = node.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (matched.Value is null)
            return null;
        return matched.Value is JsonValue value && value.TryGetValue<string>(out var str) ? str : null;
    }

}
