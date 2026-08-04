using DotCraft.Protocol.InlineVisualizations;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class InlineVisualizationRequestHandler : IAppServerDomainHandler, IDisposable
{
    private readonly ISessionService sessions;
    private readonly AppServerConnection connection;
    private readonly InlineVisualizationAssetStore? store;
    private readonly InlineVisualizationRuntimeRegistry? runtimeRegistry;
    private readonly InlineVisualizationViewRegistry _views = new();

    public InlineVisualizationRequestHandler(
        ISessionService sessions,
        AppServerConnection connection,
        InlineVisualizationAssetStore? store,
        InlineVisualizationRuntimeRegistry? runtimeRegistry)
    {
        this.sessions = sessions;
        this.connection = connection;
        this.store = store;
        this.runtimeRegistry = runtimeRegistry;
        _ = DisposeWhenConnectionClosesAsync();
    }

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.InlineVisualizationViewOpen, OpenAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.InlineVisualizationViewMessage, MessageAsync);
        table.Map(global::DotCraft.Protocol.AppServer.AppServerRpc.InlineVisualizationViewClose, CloseAsync);
    }

    private async Task<AppServerTypedResult<Contract.InlineVisualizationViewOpenResult>> OpenAsync(
        AppServerTypedRequest<Contract.InlineVisualizationViewOpenParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = request.Params;
        var threadId = Require(parameters.ThreadId, "threadId");
        var turnId = Require(parameters.TurnId, "turnId");
        var itemId = Require(parameters.ItemId, "itemId");
        var file = Require(parameters.File, "file");
        if (runtimeRegistry?.IsBoundTo(threadId, connection) != true)
            throw InlineVisualizationViewErrors.Create("not_available", "Inline visualizations are not available for this thread.");
        if (!InlineVisualizationDirectiveParser.IsValidFileName(file))
            throw InlineVisualizationViewErrors.Create("unsafe_path", "The visualization file name is invalid.");
        var thread = await sessions.GetThreadAsync(threadId, cancellationToken)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization thread is unavailable.");
        var turn = thread.Turns.FirstOrDefault(candidate => candidate.Id == turnId)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization turn is unavailable.");
        var item = turn.Items.FirstOrDefault(candidate => candidate.Id == itemId)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization item is unavailable.");
        string fragment;
        try { fragment = await store!.ReadReferencedFragmentAsync(thread, turn, item, file, cancellationToken); }
        catch (InlineVisualizationException ex) { throw InlineVisualizationViewErrors.Create(ex.Code, ex.Message); }
        var state = _views.Add(new InlineVisualizationViewState
        {
            Handle = $"vis_{Guid.NewGuid():N}",
            ThreadId = thread.Id,
            TurnId = turn.Id,
            ItemId = item.Id,
            File = file
        });
        return AppServerTypedResult<Contract.InlineVisualizationViewOpenResult>.FromResult(new Contract.InlineVisualizationViewOpenResult
        {
            ViewHandle = state.Handle,
            Fragment = fragment,
            MimeType = "text/html"
        });
    }

    private async Task<AppServerTypedResult<Contract.InlineVisualizationViewMessageResult>> MessageAsync(
        AppServerTypedRequest<Contract.InlineVisualizationViewMessageParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = request.Params;
        var state = _views.Get(Require(parameters.ViewHandle, "viewHandle"));
        if (runtimeRegistry?.IsBoundTo(state.ThreadId, connection) != true)
        {
            _views.Close(state.Handle);
            throw InlineVisualizationViewErrors.Create("stale_view", "The visualization view is no longer available.");
        }
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo { Kind = "visualization", Label = state.File, RefId = state.ItemId });
        var queued = await sessions.EnqueueTurnInputAsync(
            state.ThreadId,
            [new TextContent(Require(parameters.Prompt, "prompt"))],
            ct: cancellationToken);
        await sessions.TryStartNextQueuedTurnAsync(state.ThreadId, cancellationToken);
        return AppServerTypedResult<Contract.InlineVisualizationViewMessageResult>.FromResult(
            new Contract.InlineVisualizationViewMessageResult { QueuedInputId = queued.Id });
    }

    private Task<AppServerTypedResult<Contract.InlineVisualizationViewCloseResult>> CloseAsync(
        AppServerTypedRequest<Contract.InlineVisualizationViewCloseParams> request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var viewHandle = Require(request.Params.ViewHandle, "viewHandle");
        return Task.FromResult(AppServerTypedResult<Contract.InlineVisualizationViewCloseResult>.FromResult(
            new Contract.InlineVisualizationViewCloseResult { Closed = _views.Close(viewHandle) }));
    }

    private static string Require(DotCraft.Protocol.Optional<string> value, string field)
    {
        var result = value.IsSet ? value.Value : null;
        if (string.IsNullOrWhiteSpace(result))
            throw AppServerErrors.InvalidParams($"'{field}' is required.");
        return result;
    }

    private void EnsureAvailable()
    {
        if (!connection.SupportsInlineVisualizations || store is null || runtimeRegistry is null)
            throw AppServerErrors.MethodNotFound(DotCraft.Protocol.AppServer.AppServerMethodNames.InlineVisualizationViewOpen);
    }

    private async Task DisposeWhenConnectionClosesAsync()
    {
        await connection.Closed.ConfigureAwait(false);
        Dispose();
    }

    public void Dispose()
    {
        _views.Dispose();
    }
}
