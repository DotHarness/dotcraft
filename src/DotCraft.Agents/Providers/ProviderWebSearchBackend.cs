namespace DotCraft.Configuration;

public static class ProviderWebSearchBackend
{
    public const string DeepSeek = "deepseek";
    public static Uri DeepSeekSearchEndpoint { get; } = new("https://api.deepseek.com/v1/tools/search");

    public static string? Resolve(string endpoint, string? authMethod)
    {
        if ((!string.IsNullOrWhiteSpace(authMethod) && !string.Equals(authMethod.Trim(), ModelProviderAuthMethods.ApiKey, StringComparison.OrdinalIgnoreCase))
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !uri.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return null;

        return uri.AbsolutePath.TrimEnd('/') is "" or "/v1" or "/beta" or "/anthropic" or "/anthropic/v1"
            ? DeepSeek
            : null;
    }
}
