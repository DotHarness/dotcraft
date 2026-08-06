namespace DotCraft.Configuration;

internal static class ModelThinkingAdapterResolver
{
    public static bool ShouldApplyDeepThinking(AppConfig config, string? endpoint, string? model) =>
        ModelThinkingAdapterCatalog.ShouldApplyDeepThinking(
            endpoint,
            model,
            GlobalPath(config),
            WorkspacePath(config));

    public static ModelThinkingAdapterCatalog.AnthropicThinkingAdapterData? ResolveAnthropicThinkingAdapter(
        AppConfig config,
        string? endpoint,
        string? model) =>
        ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
            endpoint,
            model,
            GlobalPath(config),
            WorkspacePath(config));

    public static ModelThinkingAdapterCatalog.AnthropicMessageContentAdapterData? ResolveAnthropicMessageContentAdapter(
        AppConfig config,
        string? endpoint,
        string? model) =>
        ModelThinkingAdapterCatalog.ResolveAnthropicMessageContentAdapter(
            endpoint,
            model,
            GlobalPath(config),
            WorkspacePath(config));

    public static ModelThinkingAdapterCatalog.ReasoningCapabilityData? ResolveReasoningCapability(
        AppConfig config,
        string? protocol,
        string? endpoint,
        string? model) =>
        ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            protocol,
            endpoint,
            model,
            GlobalPath(config),
            WorkspacePath(config));

    private static string? GlobalPath(AppConfig config) =>
        ModelThinkingAdapterCatalog.CatalogPathForConfig(config.GlobalConfigPath);

    private static string? WorkspacePath(AppConfig config) =>
        ModelThinkingAdapterCatalog.CatalogPathForConfig(config.WorkspaceConfigPath);
}
