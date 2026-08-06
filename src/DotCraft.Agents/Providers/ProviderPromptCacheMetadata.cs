using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public static class ProviderPromptCacheMetadata
{
    public const string PromptCacheKey = "dotcraft.prompt_cache_key";

    public static string? ResolveKey(ChatOptions? options, string? preferred = null)
    {
        if (options?.AdditionalProperties != null
            && options.AdditionalProperties.TryGetValue(PromptCacheKey, out var raw)
            && raw is string configured
            && !string.IsNullOrWhiteSpace(configured))
            return configured.Trim();
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred.Trim();
        return ProviderRequestContextScope.Current?.CurrentIdentity.RootThreadId;
    }

    public static void ApplyKey(ChatOptions options, string promptCacheKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(promptCacheKey))
            return;
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[PromptCacheKey] = promptCacheKey.Trim();
    }
}
