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
        if (ModelPreferenceRules.ValidateReasoning(config, providerId, model, reasoning) is { } error)
            throw AppServerErrors.InvalidParams(error);
    }

}
