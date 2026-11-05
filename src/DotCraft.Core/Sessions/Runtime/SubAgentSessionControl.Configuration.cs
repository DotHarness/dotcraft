using DotCraft.Configuration;
using DotCraft.Tools;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static ThreadConfiguration ApplyRoleToChildConfiguration(
        ThreadConfiguration? parentConfiguration,
        SubAgentRoleConfig role,
        SubAgentModelPreference? nativeSubAgentPreference,
        SubAgentInvocationModelOverride? invocationModelOverride,
        AppConfig? runtimeConfig,
        bool isNativeRuntime,
        bool inheritsFullHistory,
        int childDepth,
        int maxDepth)
    {
        var child = parentConfiguration == null
            ? new ThreadConfiguration()
            : ThreadConfigurationCloner.Clone(parentConfiguration);
        child.AgentBuilderTargetId = null;
        child.AgentBuilderTargetSource = null;
        if (!isNativeRuntime || !inheritsFullHistory)
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
            if (nativeSubAgentPreference != null)
            {
                providerId = nativeSubAgentPreference.ProviderId ?? parentProviderId;
                preference = nativeSubAgentPreference.ToModelPreference();
            }
            if ((!isNativeRuntime || !inheritsFullHistory) && !string.IsNullOrWhiteSpace(role.Model))
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
        if (!isNativeRuntime)
        {
            child.ToolAllowList = MergeAllowLists(parentConfiguration?.ToolAllowList, role.ToolAllowList);
            child.ToolDenyList = MergeDenyLists(parentConfiguration?.ToolDenyList, role.ToolDenyList);
            if (role.OverrideBasePrompt && !string.IsNullOrWhiteSpace(role.Instructions))
                child.AgentInstructions = role.Instructions;
            ApplyAgentControlPolicy(child, role, childDepth, maxDepth);
        }
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

    private static void ApplyAgentControlPolicy(
        ThreadConfiguration child,
        SubAgentRoleConfig role,
        int childDepth,
        int maxDepth)
    {
        var requestedAccess = role.AgentControlToolAccess;
        var requestedAllowed = role.AllowedAgentControlTools
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (requestedAccess == AgentControlToolAccess.Full)
            requestedAllowed = AgentControlToolPolicy.AllToolNames.ToHashSet(StringComparer.Ordinal);

        if (childDepth >= maxDepth)
            requestedAllowed.Remove(nameof(AgentTools.SpawnAgent));

        if (requestedAccess == AgentControlToolAccess.Disabled || requestedAllowed.Count == 0)
        {
            child.AgentControlToolAccess = AgentControlToolAccess.Disabled;
            child.AllowedAgentControlTools = null;
            return;
        }

        child.AgentControlToolAccess = requestedAccess == AgentControlToolAccess.AllowList || childDepth >= maxDepth
            ? AgentControlToolAccess.AllowList
            : AgentControlToolAccess.Full;
        child.AllowedAgentControlTools = child.AgentControlToolAccess == AgentControlToolAccess.AllowList
            ? requestedAllowed.ToArray()
            : null;
    }

    private static string[]? MergeAllowLists(string[]? parent, IReadOnlyList<string> role)
    {
        var parentSet = parent?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);
        var roleSet = role.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);
        if (parentSet is not { Count: > 0 })
            return roleSet.Count == 0 ? null : roleSet.ToArray();
        if (roleSet.Count == 0)
            return parentSet.ToArray();

        parentSet.IntersectWith(roleSet);
        return parentSet.Count == 0 ? [] : parentSet.ToArray();
    }

    private static string[]? MergeDenyLists(string[]? parent, IReadOnlyList<string> role)
    {
        var deny = parent?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in role.Where(v => !string.IsNullOrWhiteSpace(v)))
            deny.Add(item);
        return deny.Count == 0 ? null : deny.ToArray();
    }
}
