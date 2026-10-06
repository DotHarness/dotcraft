using DotCraft.Context;
using DotCraft.Dreams;
using DotCraft.Lsp;
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
        configuration.RegisterValidator("ProviderPreferences", candidate =>
            ValidateProviderPreferences(monitor.Current, candidate));
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
}
