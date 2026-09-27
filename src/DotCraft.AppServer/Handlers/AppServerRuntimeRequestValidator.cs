using DotCraft.Configuration;
using DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.AppServer;

internal static class AppServerRuntimeRequestValidator
{
    public static void NormalizeCompleteModelConfiguration(
        AppConfig appConfig,
        ThreadConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.ProviderId)
            || string.IsNullOrWhiteSpace(config.Model)
            || config.Reasoning == null
            || !config.Speed.HasValue)
        {
            return;
        }

        var normalized = ModelPreferenceRules.Normalize(
            appConfig,
            config.ProviderId,
            new ModelPreference
            {
                Model = config.Model,
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = config.Reasoning.Enabled,
                    Effort = config.Reasoning.Effort,
                    Output = config.Reasoning.Output
                },
                Speed = config.Speed.Value,
            });

        config.Model = normalized.Model;
        config.Reasoning = normalized.Reasoning;
        config.Speed = normalized.Speed;

    }

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

        var capability = ModelThinkingAdapterResolver.ResolveReasoningCapability(
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

        if (capability.SupportedEfforts.All(option => option.Effort != reasoning.Effort.ToProviderEffort()))
        {
            throw AppServerErrors.InvalidParams(
                $"Model '{runtime.Model}' does not support reasoning effort '{reasoning.Effort}'.");
        }
    }

}
