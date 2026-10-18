using DotCraft.Channels;
using DotCraft.GenerativeUi;
using DotCraft.Visualizations;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;
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
    VisualizationRuntimeRegistry? visualizationRuntimeRegistry,
    GenerativeUiRuntimeRegistry? generativeUiRuntimeRegistry)
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

    /// <summary>
    /// Binds the connection affordances that only need a thread id, so a thread created next
    /// builds its agent once with the resulting tool surface already in place.
    /// </summary>
    public void BindThreadRuntimeInputs(
        string threadId,
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? dynamicTools,
        IReadOnlyDictionary<string, RuntimeAdditionalContextValue>? additionalContext)
    {
        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(threadId, transport, connection);
        if (wireNodeReplProxy != null && connection.HasNodeRepl && (connection.HasBrowserUse || connection.HasComputerUse))
            wireNodeReplProxy.BindThread(threadId, transport, connection);
        if (dynamicTools != null)
            wireDynamicToolProxy?.BindThread(threadId, transport, connection, dynamicTools);
        wireRuntimeAdditionalContextProvider?.BindThread(threadId, transport, connection, additionalContext);
    }

    public void UnbindThreadRuntimeInputs(string threadId)
    {
        wireAcpExtensionProxy?.UnbindThread(threadId);
        wireNodeReplProxy?.UnbindThread(threadId);
        wireDynamicToolProxy?.UnbindThread(threadId);
        wireRuntimeAdditionalContextProvider?.BindThread(threadId, transport, connection, EmptyAdditionalContext);
    }

    /// <summary>Binds what needs the created thread. Never changes the tool surface.</summary>
    public void BindThreadAssets(SessionThread thread)
    {
        visualizationRuntimeRegistry?.BindThread(thread, transport, connection);
        generativeUiRuntimeRegistry?.BindThread(thread.Id, connection);
    }

    public ChannelChatDescriptor? ValidateChat(Contract.ChannelChat? chat)
    {
        if (chat == null)
            return null;
        if (!connection.IsChannelAdapter)
            throw AppServerErrors.InvalidParams("chat is accepted only from a channel adapter connection.");
        if (chat.Kind is not (ChannelChatKinds.Direct or ChannelChatKinds.Group or ChannelChatKinds.Topic))
            throw AppServerErrors.InvalidParams("chat.kind must be 'direct', 'group', or 'topic'.");
        if (chat.Membership is not (ChannelChatMemberships.Shared or ChannelChatMemberships.Sender))
            throw AppServerErrors.InvalidParams("chat.membership must be 'shared' or 'sender'.");
        var label = string.IsNullOrWhiteSpace(chat.Label) ? null : chat.Label.Trim();
        return new ChannelChatDescriptor(chat.Kind, label, chat.Membership);
    }

    public void BindChat(string threadId, ChannelChatDescriptor? chat)
    {
        if (chat != null)
            connection.BindThreadChat(threadId, chat);
    }

    public void ForkAdditionalContext(string parentThreadId, string childThreadId)
        => wireRuntimeAdditionalContextProvider?.TryForkThreadBinding(parentThreadId, childThreadId, connection);

    private static readonly Dictionary<string, RuntimeAdditionalContextValue> EmptyAdditionalContext = [];

    public async Task BindThreadRuntimeAsync(
        SessionThread thread,
        IReadOnlyList<RuntimeDynamicToolDeclarationSpec>? dynamicTools,
        IReadOnlyDictionary<string, RuntimeAdditionalContextValue>? additionalContext,
        CancellationToken ct)
    {
        if (wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);

        // Only a binding that changes the tool surface needs a rebuilt agent: client-bound context
        // reaches the model as a thread context item reconciled on each Turn.
        var shouldRefreshAgent = false;
        visualizationRuntimeRegistry?.BindThread(thread, transport, connection);
        generativeUiRuntimeRegistry?.BindThread(thread.Id, connection);
        if (wireNodeReplProxy != null && connection.HasNodeRepl && (connection.HasBrowserUse || connection.HasComputerUse))
        {
            wireNodeReplProxy.BindThread(thread.Id, transport, connection);
            shouldRefreshAgent = true;
        }

        if (dynamicTools != null && wireDynamicToolProxy != null)
        {
            wireDynamicToolProxy.BindThread(thread.Id, transport, connection, dynamicTools);
            shouldRefreshAgent = true;
        }

        wireRuntimeAdditionalContextProvider?.BindThread(thread.Id, transport, connection, additionalContext);

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
