using DotCraft.Channels;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Templates;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Automations;

/// <summary>
/// Automation task orchestration module.
/// </summary>
[DotCraftModule("automations", Priority = 55, Description = "Local automation task orchestrator")]
public sealed partial class AutomationsModule : ModuleBase, ISessionChannelModule
{
    public override bool IsEnabled(AppConfig config) =>
        config.GetSection<AutomationsConfig>("Automations").Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        var a = config.GetSection<AutomationsConfig>("Automations");
        if (a.Enabled && a.MaxConcurrentTasks < 1)
            errors.Add("Automations: MaxConcurrentTasks must be at least 1.");
        if (a.Enabled && a.WorktreeRetentionEnabled && a.WorktreeRetentionIdlePeriod < TimeSpan.FromDays(14))
            errors.Add("Automations: WorktreeRetentionIdlePeriod must be at least 14 days.");
        return errors;
    }

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var cfg = context.Config.GetSection<AutomationsConfig>("Automations");
        services.AddSingleton(cfg);
        services.AddSingleton<LocalTaskFileStore>();
        services.AddSingleton<UserTemplateFileStore>();
        services.AddSingleton<LocalWorkflowLoader>();
        services.AddSingleton<LocalAutomationSource>();
        services.AddSingleton<AutomationOrchestrator>();
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() =>
        [new("automations", "system")];
}
