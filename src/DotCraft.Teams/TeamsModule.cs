using DotCraft.Abstractions;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Teams;

/// <summary>
/// First-party managed App Binding runtime for DotCraft Teams.
/// </summary>
[DotCraftModule("teams", Priority = 54, Description = "DotCraft Teams runtime")]
public sealed partial class TeamsModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => true;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.TryAddSingleton<TeamsService>();
        services.AddSingleton<IManagedAppBindingRuntime>(sp => sp.GetRequiredService<TeamsService>());
        services.AddSingleton<IThreadRuntimeSignalObserver>(sp => sp.GetRequiredService<TeamsService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAppServerProtocolExtension, TeamsProtocolExtension>());
    }

    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() =>
        [new("teams", "system")];
}
