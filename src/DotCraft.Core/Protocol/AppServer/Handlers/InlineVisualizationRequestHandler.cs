using DotCraft.Protocol.InlineVisualizations;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

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
        table.Map(AppServerMethods.InlineVisualizationViewOpen, OpenAsync);
        table.Map(AppServerMethods.InlineVisualizationViewMessage, MessageAsync);
        table.Map(AppServerMethods.InlineVisualizationViewClose, CloseAsync);
    }

    private async Task<object?> OpenAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = AppServerParams.Get<InlineVisualizationViewOpenParams>(message);
        if (runtimeRegistry?.IsBoundTo(parameters.ThreadId, connection) != true)
            throw InlineVisualizationViewErrors.Create("not_available", "Inline visualizations are not available for this thread.");
        if (!InlineVisualizationDirectiveParser.IsValidFileName(parameters.File))
            throw InlineVisualizationViewErrors.Create("unsafe_path", "The visualization file name is invalid.");
        var thread = await sessions.GetThreadAsync(parameters.ThreadId, cancellationToken)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization thread is unavailable.");
        var turn = thread.Turns.FirstOrDefault(candidate => candidate.Id == parameters.TurnId)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization turn is unavailable.");
        var item = turn.Items.FirstOrDefault(candidate => candidate.Id == parameters.ItemId)
            ?? throw InlineVisualizationViewErrors.Create("not_found", "The visualization item is unavailable.");
        string fragment;
        try { fragment = await store!.ReadReferencedFragmentAsync(thread, turn, item, parameters.File, cancellationToken); }
        catch (InlineVisualizationException ex) { throw InlineVisualizationViewErrors.Create(ex.Code, ex.Message); }
        var state = _views.Add(new InlineVisualizationViewState
        {
            Handle = $"vis_{Guid.NewGuid():N}",
            ThreadId = thread.Id,
            TurnId = turn.Id,
            ItemId = item.Id,
            File = parameters.File
        });
        return new InlineVisualizationViewOpenResult
        {
            ViewHandle = state.Handle,
            Fragment = fragment,
            MimeType = "text/html"
        };
    }

    private async Task<object?> MessageAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = AppServerParams.Get<InlineVisualizationViewMessageParams>(message);
        var state = _views.Get(parameters.ViewHandle);
        if (runtimeRegistry?.IsBoundTo(state.ThreadId, connection) != true)
        {
            _views.Close(state.Handle);
            throw InlineVisualizationViewErrors.Create("stale_view", "The visualization view is no longer available.");
        }
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo { Kind = "visualization", Label = state.File, RefId = state.ItemId });
        var queued = await sessions.EnqueueTurnInputAsync(state.ThreadId, [new TextContent(parameters.Prompt)], ct: cancellationToken);
        await sessions.TryStartNextQueuedTurnAsync(state.ThreadId, cancellationToken);
        return new InlineVisualizationViewMessageResult { QueuedInputId = queued.Id };
    }

    private Task<object?> CloseAsync(AppServerIncomingMessage message, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var parameters = AppServerParams.Get<InlineVisualizationViewCloseParams>(message);
        return Task.FromResult<object?>(new InlineVisualizationViewCloseResult { Closed = _views.Close(parameters.ViewHandle) });
    }

    private void EnsureAvailable()
    {
        if (!connection.SupportsInlineVisualizations || store is null || runtimeRegistry is null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.InlineVisualizationViewOpen);
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
