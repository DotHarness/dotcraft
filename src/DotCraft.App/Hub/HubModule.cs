using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Hub;

/// <summary>
/// Hub module for the workspace-independent local coordinator shell.
/// </summary>
[DotCraftModule("hub", Priority = 300, Description = "Hub: local workspace-independent coordinator shell", CanBePrimaryHost = true)]
public sealed partial class HubModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => false;
}

/// <summary>
/// Host factory for Hub mode.
/// </summary>
[HostFactory("hub")]
public sealed class HubHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<HubHost>(
            serviceProvider,
            context.Config.GetSection<HubConfig>("Hub"),
            HubPaths.ForCurrentUser());
    }
}
