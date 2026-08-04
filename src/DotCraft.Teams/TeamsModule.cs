using DotCraft.Channels;
using DotCraft.Context;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DotCraft.AppServer;
using DotCraft.Sessions;

namespace DotCraft.Teams;

/// <summary>
/// First-party native runtime for DotCraft Teams.
/// </summary>
[DotCraftModule("teams", Priority = 54, Description = "DotCraft Teams runtime")]
public sealed partial class TeamsModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => true;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.TryAddSingleton<TeamsService>();
        services.TryAddSingleton<TeamsToolSource>();
        services.AddSingleton<ISessionServiceConsumer>(sp => sp.GetRequiredService<TeamsService>());
        services.AddSingleton<IThreadRuntimeSignalObserver>(sp => sp.GetRequiredService<TeamsService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadSystemPromptContextProvider, TeamsThreadSystemPromptContextProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IThreadOriginPresentationProvider, TeamsThreadOriginPresentationProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, TeamsProtocolExtension>());
    }

    public override IEnumerable<IToolSource> GetToolSources(IServiceProvider services) =>
        [services.GetRequiredService<TeamsToolSource>()];

    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() =>
        [new("teams", "system")];
}
