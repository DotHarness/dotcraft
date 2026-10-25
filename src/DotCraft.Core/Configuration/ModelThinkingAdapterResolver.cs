namespace DotCraft.Configuration;

public static class ModelThinkingAdapterResolver
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

    private static ModelThinkingAdapterCatalog.ReasoningCapabilityData? ResolveReasoningCapability(
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

    /// <summary>Backend models are described only by what their backend reports.</summary>
    public static ModelThinkingAdapterCatalog.ReasoningCapabilityData? ResolveReasoningCapability(
        AppConfig config,
        EffectiveModelRuntime runtime) =>
        runtime.BackendId is null
            ? ResolveReasoningCapability(config, runtime.Protocol, runtime.EndPoint, runtime.Model)
            : null;

    public static ModelThinkingAdapterCatalog.ReasoningCapabilityData? ResolveReasoningCapability(
        AppConfig config,
        string? providerId,
        string? protocol,
        string? endpoint,
        ModelCatalogEntry model)
    {
        if (model.Reasoning is { } reported)
            return FromReported(reported);
        return ModelProviderResolver.IsBackendProvider(config, providerId)
            ? null
            : ResolveReasoningCapability(config, protocol, endpoint, model.Id);
    }

    private static ModelThinkingAdapterCatalog.ReasoningCapabilityData FromReported(ModelReasoningSupport reported)
    {
        var capability = new ModelThinkingAdapterCatalog.ReasoningCapabilityData
        {
            SupportsDisable = reported.SupportsDisable,
            DefaultEffort = reported.DefaultEffort
        };
        capability.SupportedEfforts.AddRange(reported.SupportedEfforts.Select(static effort => new ModelThinkingAdapterCatalog.ReasoningEffortOptionData(effort)));
        capability.NormalizeDefaults();
        return capability;
    }

    private static string? GlobalPath(AppConfig config) =>
        ModelThinkingAdapterCatalog.CatalogPathForConfig(config.GlobalConfigPath);

    private static string? WorkspacePath(AppConfig config) =>
        ModelThinkingAdapterCatalog.CatalogPathForConfig(config.WorkspaceConfigPath);
}
