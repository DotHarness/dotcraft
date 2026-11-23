using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Configuration;

public static class ConfigurationSubsystems
{
    public const string ThreadAgents = "threadAgents";
    public const string Lsp = "lsp";
    public const string Dreams = "dreams";
    public const string Skills = "skills";

    public static IReadOnlyList<string> Keys { get; } = [ThreadAgents, Lsp, Dreams, Skills];

    public static void Register(
        ConfigurationService configuration,
        IAppConfigMonitor monitor,
        IThreadAgentRefreshService? threadAgents,
        SkillsLoader? skills,
        LspServerManager? lsp,
        DreamsService? dreams,
        IContextPageManager? contextPages)
    {
        configuration.RegisterSubsystemHandler(ThreadAgents, (_, _) =>
        {
            threadAgents?.InvalidateThreadAgents();
            return Task.CompletedTask;
        });
        configuration.RegisterSubsystemHandler(Lsp, (_, ct) => lsp?.InitializeAsync(ct) ?? Task.CompletedTask);
        configuration.RegisterSubsystemHandler(Dreams, (_, ct) =>
            dreams == null ? Task.CompletedTask
            : monitor.Current is { Dreams.Enabled: true, Memory.Enabled: true } ? dreams.StartAsync(ct)
            : dreams.StopAsync(ct));
        configuration.RegisterSubsystemHandler(Skills, (_, _) =>
        {
            skills?.SetDisabledSkills(monitor.Current.Skills.DisabledSkills);
            contextPages?.MarkDirty(ContextPageKeys.SkillsWildcard());
            threadAgents?.InvalidateThreadAgents();
            return Task.CompletedTask;
        });

        configuration.RegisterValidator("Dreams.Interval", candidate =>
            candidate.Dreams.Interval > TimeSpan.Zero ? null : "'Dreams.Interval' must be a positive duration.");
        configuration.RegisterValidator("Memory.Types", candidate => MemoryPolicyRules.ValidateTypes(candidate.Memory.Types));
        configuration.RegisterValidator("Memory.SaveGuidance", candidate =>
            MemoryPolicyRules.ValidateGuidance("Memory.SaveGuidance", candidate.Memory.SaveGuidance));
        configuration.RegisterValidator("Memory.ExtractionGuidance", candidate =>
            MemoryPolicyRules.ValidateGuidance("Memory.ExtractionGuidance", candidate.Memory.ExtractionGuidance));
        configuration.RegisterValidator("Memory.ConsolidationGuidance", candidate =>
            MemoryPolicyRules.ValidateGuidance("Memory.ConsolidationGuidance", candidate.Memory.ConsolidationGuidance));
        configuration.RegisterValidator("ProviderPreferences", candidate =>
            ValidateProviderPreferences(monitor.Current, candidate));
        configuration.RegisterValidator(ProviderConfiguration.KeyPath, (previous, candidate) =>
            ProviderConfiguration.ValidateProviders(previous, candidate)
            ?? ProviderConfiguration.ValidateSelection(previous, candidate, monitor.Current));
        configuration.RegisterValidator("SubAgent.ProviderPreferences", (previous, candidate) =>
            ValidateSubAgentProviderPreferences(monitor.Current, previous, candidate));
        configuration.RegisterValidator("SubAgent.Models", (previous, candidate) =>
            SubAgentModelChoiceRules.Validate(
                previous.SubAgent.Models,
                candidate.SubAgent.Models,
                providerId => candidate.Providers.ContainsKey(providerId) || monitor.Current.Providers.ContainsKey(providerId)));
        configuration.RegisterValidator("ProviderId", (previous, candidate) =>
            ProviderConfiguration.ValidateSelection(previous, candidate, monitor.Current));
    }

    private static string? ValidateProviderPreferences(AppConfig runtime, AppConfig candidate)
    {
        foreach (var (providerId, preference) in candidate.ProviderPreferences)
        {
            if (ModelPreferenceRules.ValueEquals(preference, ModelPreferenceRules.Find(runtime.ProviderPreferences, providerId)))
                continue;

            var model = preference?.Model?.Trim();
            if (string.IsNullOrEmpty(model) || string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
                return $"'ProviderPreferences.{providerId}.Model' is required.";
            if (ModelPreferenceRules.ValidateReasoning(runtime, providerId, model, preference!.Reasoning ?? new()) is { } error)
                return error;
        }

        return null;
    }

    private static string? ValidateSubAgentProviderPreferences(AppConfig runtime, AppConfig previous, AppConfig candidate)
    {
        foreach (var (providerId, preference) in candidate.SubAgent.ProviderPreferences)
        {
            var targetProviderId = string.IsNullOrWhiteSpace(preference?.ProviderId)
                ? providerId
                : preference.ProviderId.Trim();
            if (previous.SubAgent.ProviderPreferences.TryGetValue(providerId, out var prior)
                && prior != null
                && preference != null
                && string.Equals(prior.ProviderId ?? providerId, targetProviderId, StringComparison.OrdinalIgnoreCase)
                && ModelPreferenceRules.ValueEquals(prior.ToModelPreference(), preference.ToModelPreference()))
            {
                continue;
            }

            var model = preference?.Model?.Trim();
            if (string.IsNullOrEmpty(model) || string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
                return $"'SubAgent.ProviderPreferences.{providerId}.Model' is required.";
            if (!string.IsNullOrWhiteSpace(preference!.ProviderId)
                && !candidate.Providers.ContainsKey(targetProviderId)
                && !runtime.Providers.ContainsKey(targetProviderId))
            {
                return $"Model provider '{targetProviderId}' is not configured.";
            }
            if (ModelPreferenceRules.ValidateReasoning(runtime, targetProviderId, model, preference.Reasoning ?? new()) is { } error)
                return error;
        }

        return null;
    }
}
