using DotCraft.Configuration;

namespace DotCraft.Protocol.AppServer;

internal static class AppServerRuntimeRequestValidator
{
    public static void ValidateReasoningForRuntime(
        AppConfig config,
        string? providerId,
        string? model,
        AppConfig.ReasoningConfig reasoning)
    {
        EffectiveModelRuntime runtime;
        try
        {
            runtime = ModelProviderResolver.ResolveMain(config, providerId, model);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (ModelProviderConfigurationException)
        {
            return;
        }

        var capability = ModelThinkingAdapterCatalog.ResolveReasoningCapability(
            config,
            runtime.Protocol,
            runtime.EndPoint,
            runtime.Model);
        if (capability == null)
            return;

        if (!reasoning.Enabled)
        {
            if (!capability.SupportsDisable)
            {
                throw AppServerErrors.InvalidParams(
                    $"Model '{runtime.Model}' does not support disabling reasoning.");
            }

            return;
        }

        if (capability.SupportedEfforts.All(option => option.Effort != reasoning.Effort))
        {
            throw AppServerErrors.InvalidParams(
                $"Model '{runtime.Model}' does not support reasoning effort '{reasoning.Effort}'.");
        }
    }
}
