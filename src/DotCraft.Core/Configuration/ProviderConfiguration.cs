using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

public static class ProviderConfiguration
{
    public const string KeyPath = "Providers";

    internal const string RemoteManagedMessage =
        "'Providers' is managed by the model service; change providers in its configuration.";

    public static string ProviderKeyPath(string providerId, string? field = null) =>
        field == null ? ConfigJsonPath.Join(KeyPath, providerId) : ConfigJsonPath.Join(KeyPath, providerId, field);

    internal static bool IsProviderKeyPath(string keyPath) =>
        keyPath == KeyPath || keyPath.StartsWith(KeyPath + ".", StringComparison.Ordinal);

    internal static string? ValidateProviders(AppConfig previous, AppConfig candidate)
    {
        foreach (var (id, provider) in candidate.Providers)
        {
            if (Find(previous.Providers, id) is { } before && JsonNode.DeepEquals(Serialize(before), Serialize(provider)))
                continue;
            if (ValidateEntry(id, provider) is { } error)
                return error;
        }

        return null;
    }

    internal static string? ValidateSelection(AppConfig previous, AppConfig candidate, AppConfig runtime)
    {
        var providerId = candidate.ProviderId?.Trim();
        var remote = runtime.ModelService != null;
        if (string.IsNullOrEmpty(providerId) || Find(remote ? runtime.Providers : candidate.Providers, providerId) != null)
            return null;

        var unchanged = string.Equals(previous.ProviderId?.Trim(), providerId, StringComparison.OrdinalIgnoreCase);
        if (unchanged && (remote || Find(previous.Providers, providerId) == null))
            return null;
        return remote
            ? "'ProviderId' must name a provider managed by the model service."
            : "'ProviderId' must name a defined provider.";
    }

    private static string? ValidateEntry(string id, AppConfig.ModelProviderConfig? provider)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(['/', '\\', ':']) >= 0)
            return "'Providers' ids must be non-empty and must not contain '/', '\\', or ':'.";
        if (provider == null)
            return $"'{ProviderKeyPath(id)}' must be an object.";

        if (!string.IsNullOrWhiteSpace(provider.BackendId))
        {
            if (provider.BackendSettings is null)
                return $"'{ProviderKeyPath(id, "BackendSettings")}' must be an object.";
            if (provider.BackendId.Any(char.IsWhiteSpace))
                return $"'{ProviderKeyPath(id, "BackendId")}' must not contain whitespace.";
            if (provider.NetworkTimeoutSeconds < 1 || provider.MaxOutputTokens < 1 || provider.StreamIdleTimeoutMs < 1
                || provider.StreamMaxRetries is < 0 or > ModelProviderDefaults.MaxStreamMaxRetries)
                return $"'{ProviderKeyPath(id)}' has invalid request limits.";
            return null;
        }

        string protocol;
        try
        {
            protocol = ModelProviderProtocols.Normalize(provider.Protocol);
        }
        catch (ArgumentException)
        {
            return $"'{ProviderKeyPath(id, "Protocol")}' must be one of: {ModelProviderProtocols.OpenAIChatCompletions}, {ModelProviderProtocols.OpenAIResponses}, {ModelProviderProtocols.Anthropic}.";
        }

        if (!string.IsNullOrWhiteSpace(provider.EndPoint) && !Uri.TryCreate(provider.EndPoint.Trim(), UriKind.Absolute, out _))
            return $"'{ProviderKeyPath(id, "EndPoint")}' must be an absolute URI.";
        if (provider.NetworkTimeoutSeconds < 1)
            return $"'{ProviderKeyPath(id, "NetworkTimeoutSeconds")}' must be at least 1.";
        if (provider.MaxOutputTokens < 1)
            return $"'{ProviderKeyPath(id, "MaxOutputTokens")}' must be at least 1.";
        if (provider.StreamIdleTimeoutMs < 1)
            return $"'{ProviderKeyPath(id, "StreamIdleTimeoutMs")}' must be at least 1.";
        if (provider.StreamMaxRetries is < 0 or > ModelProviderDefaults.MaxStreamMaxRetries)
            return $"'{ProviderKeyPath(id, "StreamMaxRetries")}' must be from 0 to {ModelProviderDefaults.MaxStreamMaxRetries}.";

        var authMethod = provider.AuthMethod?.Trim();
        if (string.IsNullOrEmpty(authMethod))
            return null;
        if (!authMethod.Equals(ModelProviderAuthMethods.ApiKey, StringComparison.OrdinalIgnoreCase)
            && !authMethod.Equals(ModelProviderAuthMethods.ChatGptOAuth, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{ProviderKeyPath(id, "AuthMethod")}' must be one of: {ModelProviderAuthMethods.ApiKey}, {ModelProviderAuthMethods.ChatGptOAuth}.";
        }

        return ModelProviderAuthMethods.IsChatGptOAuth(authMethod) && !ModelProviderProtocols.IsOpenAIProtocol(protocol)
            ? $"'{ProviderKeyPath(id, "AuthMethod")}' {ModelProviderAuthMethods.ChatGptOAuth} requires an OpenAI protocol."
            : null;
    }

    private static AppConfig.ModelProviderConfig? Find(Dictionary<string, AppConfig.ModelProviderConfig> providers, string id) =>
        providers.FirstOrDefault(pair => string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase)).Value;

    private static JsonNode? Serialize(AppConfig.ModelProviderConfig? provider) =>
        JsonSerializer.SerializeToNode(provider, AppConfig.SerializerOptions);
}
