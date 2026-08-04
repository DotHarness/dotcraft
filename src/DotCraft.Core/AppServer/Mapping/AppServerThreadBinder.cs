using DotCraft.Context;
using DotCraft.InlineVisualizations;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

namespace DotCraft.AppServer;

/// <summary>
/// Binds per-connection runtime affordances to a newly created or resumed thread.
/// </summary>
internal sealed class AppServerThreadBinder(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    WireAcpExtensionProxy? wireAcpExtensionProxy,
    WireNodeReplProxy? wireNodeReplProxy,
    WireDynamicToolProxy? wireDynamicToolProxy,
    WireRuntimeAdditionalContextProvider? wireRuntimeAdditionalContextProvider,
    InlineVisualizationRuntimeRegistry? inlineVisualizationRuntimeRegistry,
    IContextPageManager? contextPageManager)
{
    public void ValidateRuntimeInputs(
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? dynamicTools,
        IReadOnlyDictionary<string, RuntimeAdditionalContextValue>? additionalContext)
    {
        if (!WireDynamicToolProxy.TryValidateSpecs(dynamicTools, out var dynamicToolError))
            throw AppServerErrors.InvalidParams(dynamicToolError);
        if (dynamicTools != null && wireDynamicToolProxy == null)
            throw AppServerErrors.InvalidParams("dynamicTools is not supported by this AppServer host.");
        if (!WireRuntimeAdditionalContextProvider.TryValidateAdditionalContext(additionalContext, out var additionalContextError))
            throw AppServerErrors.InvalidParams(additionalContextError);
        if (additionalContext != null && wireRuntimeAdditionalContextProvider == null)
            throw AppServerErrors.InvalidParams("additionalContext is not supported by this AppServer host.");
    }

    public async Task BindThreadRuntimeAsync(
        SessionThread thread,
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? dynamicTools,
        IReadOnlyDictionary<string, RuntimeAdditionalContextValue>? additionalContext,
        CancellationToken ct)
    {
        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);

        var shouldRefreshAgent = false;
        if (inlineVisualizationRuntimeRegistry?.BindThread(thread, transport, connection) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.InlineVisualization());
            shouldRefreshAgent = true;
        }
        if (wireNodeReplProxy != null && connection.HasNodeRepl && connection.HasBrowserUse)
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (dynamicTools != null && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, dynamicTools);
            shouldRefreshAgent = true;
        }

        if (wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, additionalContext) == true)
        {
            contextPageManager?.ReleaseStablePage(thread.Id, ContextPageKeys.RuntimeAdditionalContext());
            shouldRefreshAgent = true;
        }

        if (shouldRefreshAgent && sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(thread.Id, ct);
    }

    public async Task BindNodeReplThreadAndRefreshAgentAsync(string threadId, CancellationToken ct)
    {
        if (wireNodeReplProxy == null)
            return;

        wireNodeReplProxy.BindThread(threadId, transport, connection);
        if (sessionService is IThreadAgentRefreshService refreshService)
            await refreshService.RefreshThreadAgentAsync(threadId, ct);
    }
}
