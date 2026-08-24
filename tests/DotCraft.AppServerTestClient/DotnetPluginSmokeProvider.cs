using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;

namespace DotCraft.AppServerTestClient;

internal static class DotnetPluginSmokeProvider
{
    public static bool TryResolve(
        DotnetPluginSmokeCliOptions options,
        out DotnetPluginSmokeProviderSelection selection,
        out string? errorCode)
    {
        var probeRoot = Path.Combine(options.WorkRoot, "provider-probe");
        var configPath = Path.Combine(probeRoot, ".craft", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, "{}");
        try
        {
            var config = AppConfig.LoadWithGlobalFallback(configPath);
            var providerId = options.ProviderId ?? config.ProviderId?.Trim();
            if (string.IsNullOrWhiteSpace(providerId))
                return Fail("provider_not_selected", out selection, out errorCode);
            if (!config.Providers.TryGetValue(providerId, out var provider))
                return Fail("provider_not_configured", out selection, out errorCode);

            string protocol;
            try
            {
                protocol = ModelProviderProtocols.Normalize(provider.Protocol);
            }
            catch (ArgumentException)
            {
                return Fail("provider_protocol_unsupported", out selection, out errorCode);
            }

            var authMethod = ModelProviderAuthMethods.Normalize(provider.AuthMethod);
            var model = ResolveModel(config, providerId, options.Model, authMethod);
            if (string.IsNullOrWhiteSpace(model))
                return Fail("model_not_selected", out selection, out errorCode);
            if (string.Equals(authMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.Ordinal))
            {
                var userDataPath = string.IsNullOrWhiteSpace(config.GlobalConfigPath)
                    ? null
                    : Path.GetDirectoryName(config.GlobalConfigPath);
                if (new OpenAITokenStore(userDataPath).Load() is null)
                    return Fail("chatgpt_oauth_not_logged_in", out selection, out errorCode);
            }
            else if (string.IsNullOrWhiteSpace(provider.ApiKey))
            {
                return Fail("provider_api_key_missing", out selection, out errorCode);
            }

            selection = new DotnetPluginSmokeProviderSelection(protocol, providerId, model.Trim());
            errorCode = null;
            return true;
        }
        finally
        {
            try { Directory.Delete(probeRoot, recursive: true); } catch { }
        }
    }

    internal static string? ResolveModel(
        AppConfig config,
        string providerId,
        string? explicitModel,
        string authMethod)
    {
        if (!string.IsNullOrWhiteSpace(explicitModel))
            return explicitModel.Trim();
        if (config.ProviderPreferences.TryGetValue(providerId, out var preference)
            && !string.IsNullOrWhiteSpace(preference.Model))
        {
            return preference.Model.Trim();
        }
        return string.Equals(authMethod, ModelProviderAuthMethods.ChatGptOAuth, StringComparison.Ordinal)
            ? ModelProviderDefaults.DefaultChatGptCodexModel
            : null;
    }

    private static bool Fail(
        string error,
        out DotnetPluginSmokeProviderSelection selection,
        out string? errorCode)
    {
        selection = null!;
        errorCode = error;
        return false;
    }
}
