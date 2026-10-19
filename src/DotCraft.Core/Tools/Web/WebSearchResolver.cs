using DotCraft.Configuration;

namespace DotCraft.Tools.Web;

internal sealed record WebSearchTarget(
    string Provider,
    string? ApiKey = null,
    EffectiveModelRuntime? Runtime = null,
    string? Error = null);

internal static class WebSearchResolver
{
    public static WebSearchTarget Resolve(
        AppConfig config,
        EffectiveModelRuntime? current = null,
        Func<string, string?>? environment = null)
    {
        var web = config.Tools.Web;
        var selected = web.SearchProvider.Trim();
        var automatic = selected.Equals(WebSearchProvider.Auto, StringComparison.OrdinalIgnoreCase);
        if (selected.Equals(WebSearchProvider.Exa, StringComparison.OrdinalIgnoreCase))
            return new(WebSearchProvider.Exa);
        if (selected.Equals(WebSearchProvider.Bing, StringComparison.OrdinalIgnoreCase))
            return new(WebSearchProvider.Bing);
        if (!automatic && !selected.Equals(WebSearchProvider.DeepSeek, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unsupported Tools.Web.SearchProvider '{web.SearchProvider}'. Supported values: Auto, Exa, Bing, DeepSeek.");
        if (automatic && (current?.WebSearchBackend != ProviderWebSearchBackend.DeepSeek || !IsAvailable(config, current)))
            return new(WebSearchProvider.Exa);

        return ResolveDeepSeek(config, current, environment ?? Environment.GetEnvironmentVariable);
    }

    private static WebSearchTarget ResolveDeepSeek(AppConfig config, EffectiveModelRuntime? current, Func<string, string?> environment)
    {
        var web = config.Tools.Web;
        if (web.DeepSeekCredentialSource.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            var key = string.IsNullOrWhiteSpace(web.DeepSeekApiKey) ? environment("DEEPSEEK_API_KEY") : web.DeepSeekApiKey;
            return string.IsNullOrWhiteSpace(key)
                ? Failed("Set Tools.Web.DeepSeekApiKey or DEEPSEEK_API_KEY, or reuse an existing DeepSeek provider.")
                : new(WebSearchProvider.DeepSeek, key.Trim());
        }
        if (!web.DeepSeekCredentialSource.Equals("Provider", StringComparison.OrdinalIgnoreCase))
            return Failed("Unsupported Tools.Web.DeepSeekCredentialSource. Choose Provider or ApiKey.");

        if (!string.IsNullOrWhiteSpace(web.DeepSeekProvider))
        {
            try
            {
                return FromRuntime(config, ModelProviderResolver.ResolveProvider(config, web.DeepSeekProvider.Trim()));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Failed($"DeepSeek search provider '{web.DeepSeekProvider}' is not configured. Select another provider.");
            }
        }
        if (current?.WebSearchBackend == ProviderWebSearchBackend.DeepSeek && IsAvailable(config, current))
            return FromRuntime(config, current);

        var candidates = config.Providers.Keys
            .Select(id => TryResolveProvider(config, id))
            .OfType<EffectiveModelRuntime>()
            .Where(runtime => runtime.WebSearchBackend == ProviderWebSearchBackend.DeepSeek && IsAvailable(config, runtime))
            .Take(2).ToArray();
        return candidates.Length switch
        {
            1 => FromRuntime(config, candidates[0]),
            > 1 => Failed("Multiple DeepSeek providers are available. Select Tools.Web.DeepSeekProvider in Web search settings."),
            _ => Failed("No authenticated DeepSeek search provider is available. Configure a provider, upgrade the remote model service, or use an independent search key.")
        };
    }

    private static WebSearchTarget FromRuntime(AppConfig config, EffectiveModelRuntime runtime)
    {
        if (runtime.WebSearchBackend != ProviderWebSearchBackend.DeepSeek)
            return Failed($"Provider '{runtime.ProviderId}' does not support official DeepSeek search. Select another provider or upgrade the remote model service.");
        if (!IsAvailable(config, runtime))
            return Failed($"DeepSeek provider '{runtime.ProviderId}' is not authenticated. Configure its API key.");
        return new(WebSearchProvider.DeepSeek, runtime.IsRemote ? null : runtime.ApiKey, runtime);
    }

    private static bool IsAvailable(AppConfig config, EffectiveModelRuntime runtime) =>
        runtime.IsRemote
            ? config.Providers.GetValueOrDefault(runtime.ProviderId)?.RemoteAuthentication?.IsAuthenticated == true
            : !string.IsNullOrWhiteSpace(runtime.ApiKey);

    private static EffectiveModelRuntime? TryResolveProvider(AppConfig config, string id)
    {
        try
        {
            return ModelProviderResolver.ResolveProvider(config, id);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static WebSearchTarget Failed(string message) => new(WebSearchProvider.DeepSeek, Error: message);
}
