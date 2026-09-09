using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Tools;
using DotCraft.Commands.Core;
using Microsoft.Extensions.DependencyInjection;
namespace DotCraft.Automations;
/// <summary>Unified workspace automation scheduling and management.</summary>
[DotCraftModule("automations", Priority = 55, Description = "Scheduled agent work")]
public sealed partial class AutomationsModule : ModuleBase, ISessionChannelModule, IToolSourceModule
{
    public override bool IsEnabled(AppConfig config) => config.GetSection<AutomationsConfig>("Automations").Enabled;
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var cfg = config.GetSection<AutomationsConfig>("Automations");
        return cfg.Enabled && cfg.MaxConcurrentTasks < 1 ? ["Automations: MaxConcurrentTasks must be at least 1."] : [];
    }
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.AddSingleton(context.Config.GetSection<AutomationsConfig>("Automations"));
        services.AddSingleton<AutomationService>();
        services.AddSingleton<AutomationToolSource>();
        services.AddSingleton<ICommandHandler, AutomationCommandHandler>();
    }
    public IEnumerable<IToolSource> GetToolSources(IServiceProvider services) => [services.GetRequiredService<AutomationToolSource>()];
    public IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [new("automations", "system")];
}
