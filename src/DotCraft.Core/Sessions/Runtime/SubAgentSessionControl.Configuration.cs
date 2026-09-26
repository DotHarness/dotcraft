using DotCraft.Configuration;
using DotCraft.Tools;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static ThreadConfiguration ApplyRoleToChildConfiguration(
        ThreadConfiguration? parentConfiguration,
        SubAgentRoleConfig role,
        ModelPreference? nativeSubAgentPreference,
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
        child.DeveloperInstructions = null;
        if (!string.IsNullOrWhiteSpace(role.Mode))
            child.Mode = role.Mode.Trim();
        if (runtimeConfig != null)
        {
            var providerId = child.ProviderId ?? runtimeConfig.ProviderId;
            var parentPreference = new ModelPreference
            {
                Model = child.Model ?? string.Empty,
                Reasoning = ThreadConfigurationCloner.CloneNullableReasoningConfig(child.Reasoning) ?? new AppConfig.ReasoningConfig(),
                Speed = child.Speed ?? InferenceSpeed.Standard,
                ContextWindow = new ModelPreferenceContextWindow
                {
                    Mode = child.ContextWindow?.Mode ?? ContextWindowMode.Default
                }
            };
            var preference = nativeSubAgentPreference == null
                ? parentPreference
                : ModelPreferenceRules.Clone(nativeSubAgentPreference);
            if ((!isNativeRuntime || !inheritsFullHistory) && !string.IsNullOrWhiteSpace(role.Model))
                preference.Model = role.Model.Trim();
            if (invocationModelOverride != null)
            {
                if (!string.IsNullOrWhiteSpace(invocationModelOverride.Model))
                    preference.Model = invocationModelOverride.Model.Trim();
                if (invocationModelOverride.Effort is { } effort)
                {
                    preference.Reasoning.Enabled = true;
                    preference.Reasoning.Effort = effort;
                }
            }
            preference = ModelPreferenceRules.Normalize(runtimeConfig, providerId, preference);
            child.Model = preference.Model;
            child.Reasoning = ThreadConfigurationCloner.CloneNullableReasoningConfig(preference.Reasoning);
            child.Speed = preference.Speed;
            child.ContextWindow = new ThreadContextWindowConfig { Mode = preference.ContextWindow.Mode };
        }

        child.RoleInstructions = NormalizeOptional(role.Instructions);
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
