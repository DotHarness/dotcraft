using DotCraft.Configuration;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static ThreadConfiguration ApplyRoleToChildConfiguration(
        ThreadConfiguration? parentConfiguration,
        SubAgentRoleConfig role,
        SubAgentModelPreference? subAgentPreference,
        SubAgentInvocationModelOverride? invocationModelOverride,
        AppConfig? runtimeConfig,
        bool inheritsFullHistory)
    {
        var child = parentConfiguration == null
            ? new ThreadConfiguration()
            : ThreadConfigurationCloner.Clone(parentConfiguration);
        child.AgentBuilderTargetId = null;
        child.AgentBuilderTargetSource = null;
        if (!inheritsFullHistory)
        {
            child.RoleInstructions = null;
            child.DeveloperInstructions = null;
        }
        if (!string.IsNullOrWhiteSpace(role.Mode))
            child.Mode = role.Mode.Trim();
        if (runtimeConfig != null)
        {
            var parentProviderId = child.ProviderId ?? runtimeConfig.ProviderId;
            var providerId = parentProviderId;
            var preference = new ModelPreference
            {
                Model = child.Model ?? string.Empty,
                Reasoning = ThreadConfigurationCloner.CloneNullableReasoningConfig(child.Reasoning) ?? new AppConfig.ReasoningConfig(),
                Speed = child.Speed ?? InferenceSpeed.Standard,
            };
            if (subAgentPreference != null)
            {
                providerId = subAgentPreference.ProviderId ?? parentProviderId;
                preference = subAgentPreference.ToModelPreference();
            }
            if (!inheritsFullHistory && !string.IsNullOrWhiteSpace(role.Model))
                SelectModel(runtimeConfig, ref providerId, preference, role.ProviderId ?? parentProviderId, role.Model);
            if (invocationModelOverride != null)
            {
                if (!string.IsNullOrWhiteSpace(invocationModelOverride.Model))
                    SelectModel(runtimeConfig, ref providerId, preference, invocationModelOverride.ProviderId ?? providerId, invocationModelOverride.Model);
                if (invocationModelOverride.Effort is { } effort)
                {
                    preference.Reasoning.Enabled = true;
                    preference.Reasoning.Effort = effort;
                }
            }
            preference = ModelPreferenceRules.Normalize(runtimeConfig, providerId, preference);
            child.ProviderId = providerId;
            child.Model = preference.Model;
            child.Reasoning = ThreadConfigurationCloner.CloneNullableReasoningConfig(preference.Reasoning);
            child.Speed = preference.Speed;
        }

        child.SubAgentRoleInstructions = NormalizeOptional(role.Instructions);
        child.OverrideBasePrompt = role.OverrideBasePrompt;
        return child;
    }

    private static void SelectModel(
        AppConfig config,
        ref string providerId,
        ModelPreference preference,
        string targetProviderId,
        string model)
    {
        if (string.Equals(targetProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            preference.Model = model.Trim();
            return;
        }
        var defaults = ModelPreferenceRules.CreateDefault(config, targetProviderId, model);
        providerId = targetProviderId;
        preference.Model = defaults.Model;
        preference.Reasoning = defaults.Reasoning;
        preference.Speed = defaults.Speed;
    }
}
