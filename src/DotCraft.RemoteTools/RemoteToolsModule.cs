using DotCraft.Configuration;
using DotCraft.Modules;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.RemoteTools;

/// <summary>Registers the Agent-side Remote Tool Host client without owning an Agent or Session kernel.</summary>
[DotCraftModule("remote-tools", Priority = 45, Description = "Remote Tool Host client and provider-free execution host")]
public sealed partial class RemoteToolsModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => true;

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.TryAddSingleton(_ => new RemoteToolHostStorage(context.Paths.UserData.RootPath));
        services.TryAddSingleton<IRemoteToolHostClientFactory, RemoteToolHostClientFactory>();
    }
}
