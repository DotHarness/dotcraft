using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Acp;

/// <summary>
/// ACP module for Agent Client Protocol interaction with code editors/IDEs.
/// </summary>
[DotCraftModule("acp", Priority = 200, Description = "ACP module for Agent Client Protocol (stdio) interaction with code editors", CanBePrimaryHost = true)]
public sealed partial class AcpModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<AcpConfig>("Acp").Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [new("acp", "builtin")];
}

/// <summary>
/// Host factory for ACP mode.
/// </summary>
[HostFactory("acp")]
public sealed class AcpHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<AcpHost>(serviceProvider);
    }
}
