using DotCraft.Agents;
using DotCraft.AppBinding;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Logging;
using DotCraft.Modules;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Composes the workspace-scoped AppServer request handler used by every external-channel
/// transport. Keeping this composition in one place prevents transport-specific capability drift.
/// </summary>
internal sealed class ExternalChannelRequestHandlerFactory(
    ISessionService sessionService,
    string serverVersion,
    ModuleRegistry moduleRegistry,
    string hostWorkspacePath,
    ChatClientRegistry chatClientRegistry,
    ModelProviderRegistry modelProviderRegistry,
    SessionStreamDebugLogger? streamDebugLogger,
    IAppConfigMonitor? appConfigMonitor,
    IReadOnlyList<IAppServerProtocolExtension> protocolExtensions,
    AppBindingService? appBindingService,
    IReadOnlyList<IThreadOriginPresentationProvider> originPresentationProviders,
    ILoggerFactory? loggerFactory,
    WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider = null,
    DotCraft.Contributions.IContributionView? contributions = null,
    DotCraft.Commands.Core.CommandRegistry? commandRegistry = null)
{
    private readonly string _workspaceCraftPath = Path.Combine(hostWorkspacePath, ".craft");

    public AppServerRequestHandler Create(
        AppServerConnection connection,
        IAppServerTransport transport)
    {
        return new AppServerRequestHandler(
            sessionService,
            connection,
            transport,
            new ModuleRegistryChannelListContributor(moduleRegistry, appConfigMonitor?.Current),
            new AppServerConnectionServices
            {
                ServerVersion = serverVersion,
                CommandRegistry = commandRegistry,
                WorkspaceCraftPath = _workspaceCraftPath,
                HostWorkspacePath = hostWorkspacePath,
                StreamDebugLogger = streamDebugLogger,
                ConfigSchema = ConfigSchemaRegistrations.GetConfigSchema(),
                AppConfigMonitor = appConfigMonitor,
                ChatClientRegistry = chatClientRegistry,
                ModelProviderRegistry = modelProviderRegistry,
                ProtocolExtensions = protocolExtensions,
                AppBindingService = appBindingService,
                ThreadOriginPresentationProviders = originPresentationProviders,
                LoggerFactory = loggerFactory,
                WireRuntimeAdditionalContextProvider = wireRuntimeAdditionalContextProvider,
                Contributions = contributions,
            });
    }
}
