using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using DotCraft.Visualizations;
using Microsoft.Extensions.AI;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class VisualizationRequestHandler : IAppServerDomainHandler, IDisposable
{
    private readonly ISessionService sessions;
    private readonly AppServerConnection connection;
    private readonly VisualizationAssetStore? store;
    private readonly VisualizationRuntimeRegistry? runtimeRegistry;
    private readonly AppServerThreadSubscribers? subscribers;
    private const int MaxWidgetStateBytes = 16 * 1024;
    private const int MaxContextBytes = 32 * 1024;

    // Escapes backticks, so context cannot close the fenced block that carries it.
    private static readonly JsonSerializerOptions ContextJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true
    };

    private readonly VisualizationViewRegistry _views = new();

    public VisualizationRequestHandler(
        ISessionService sessions,
        AppServerConnection connection,
        VisualizationAssetStore? store,
        VisualizationRuntimeRegistry? runtimeRegistry,
        AppServerThreadSubscribers? subscribers)
    {
        this.sessions = sessions;
        this.connection = connection;
        this.store = store;
        this.runtimeRegistry = runtimeRegistry;
        this.subscribers = subscribers;
        _ = DisposeWhenConnectionClosesAsync();
    }

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Protocol.AppServer.AppServerRpc.VisualizationViewOpen, OpenAsync);
        table.Map(Protocol.AppServer.AppServerRpc.VisualizationViewMessage, MessageAsync);
        table.Map(Protocol.AppServer.AppServerRpc.VisualizationViewWidgetStateUpdate, UpdateWidgetStateAsync);
        table.Map(Protocol.AppServer.AppServerRpc.VisualizationViewClose, CloseAsync);
    }

    private async Task<AppServerTypedResult<Contract.VisualizationViewOpenResult>> OpenAsync(
        AppServerTypedRequest<Contract.VisualizationViewOpenParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = request.Params;
        var threadId = Require(parameters.ThreadId, "threadId");
        var turnId = Require(parameters.TurnId, "turnId");
        var itemId = Require(parameters.ItemId, "itemId");
        var path = Require(parameters.Path, "path");
        if (runtimeRegistry?.IsBoundTo(threadId, connection) != true)
            throw VisualizationViewErrors.Create("not_available", "Visualizations are not available for this thread.");
        if (!VisualizationContentReferenceParser.IsValidPath(path))
            throw VisualizationViewErrors.Create("unsafe_path", "The visualization path is invalid.");
        SessionThread thread;
        try { thread = await sessions.GetThreadAsync(threadId, cancellationToken); }
        catch (KeyNotFoundException) { throw VisualizationViewErrors.Create("not_found", "The visualization thread is unavailable."); }
        var turn = thread.Turns.FirstOrDefault(candidate => candidate.Id == turnId)
            ?? throw VisualizationViewErrors.Create("not_found", "The visualization turn is unavailable.");
        var item = turn.Items.FirstOrDefault(candidate => candidate.Id == itemId)
            ?? throw VisualizationViewErrors.Create("not_found", "The visualization item is unavailable.");
        string fragment;
        try { fragment = await store!.ReadReferencedFragmentAsync(thread, turn, item, path, cancellationToken); }
        catch (VisualizationException ex) { throw VisualizationViewErrors.Create(ex.Code, ex.Message); }
        var state = _views.Add(new VisualizationViewState
        {
            Handle = $"vis_{Guid.NewGuid():N}",
            ThreadId = thread.Id,
            TurnId = turn.Id,
            ItemId = item.Id,
            Path = path
        });
        var widgetState = sessions.GetVisualizationWidgetState(thread.Id, path);
        return AppServerTypedResult<Contract.VisualizationViewOpenResult>.FromResult(new Contract.VisualizationViewOpenResult
        {
            ViewHandle = state.Handle,
            Fragment = fragment,
            MimeType = "text/html",
            WidgetState = widgetState is null ? null : new Contract.VisualizationWidgetState
            {
                ModelContent = ParseJson(widgetState.ModelContentJson),
                PrivateContent = ParseJson(widgetState.PrivateContentJson)
            }
        });
    }

    private async Task<AppServerTypedResult<Contract.VisualizationViewMessageResult>> MessageAsync(
        AppServerTypedRequest<Contract.VisualizationViewMessageParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable();
        var parameters = request.Params;
        var state = GetBoundView(parameters.ViewHandle);
        var prompt = Require(parameters.Prompt, "prompt");
        var context = RawJson(parameters.Context);
        if (context != null && Encoding.UTF8.GetByteCount(context) > MaxContextBytes)
            throw VisualizationViewErrors.Create("context_too_large", "The visualization context exceeds 32 KiB.");
        var text = context is null
            ? prompt
            : $"""
              {prompt}

              Current visualization context (JSON):
              ```json
              {JsonSerializer.Serialize(parameters.Context.Value, ContextJsonOptions)}
              ```
              """;
        using var trigger = TurnTriggerScope.Set(new TurnTriggerInfo { Kind = "visualization", Label = Path.GetFileName(state.Path), RefId = state.ItemId });
        var queued = await sessions.EnqueueTurnInputAsync(
            state.ThreadId,
            [new TextContent(text)],
            ct: cancellationToken);
        await sessions.TryStartNextQueuedTurnAsync(state.ThreadId, cancellationToken);
        return AppServerTypedResult<Contract.VisualizationViewMessageResult>.FromResult(
            new Contract.VisualizationViewMessageResult { QueuedInputId = queued.Id });
    }

    private Task<AppServerTypedResult<Protocol.RpcEmpty>> UpdateWidgetStateAsync(
        AppServerTypedRequest<Contract.VisualizationViewWidgetStateUpdateParams> request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        EnsureAvailable();
        var parameters = request.Params;
        var state = GetBoundView(parameters.ViewHandle);
        var modelContent = RawJson(parameters.ModelContent);
        var privateContent = RawJson(parameters.PrivateContent);
        var snapshot = $"{{\"modelContent\":{modelContent ?? "null"},\"privateContent\":{privateContent ?? "null"}}}";
        if (Encoding.UTF8.GetByteCount(snapshot) > MaxWidgetStateBytes)
            throw VisualizationViewErrors.Create("state_too_large", "The visualization widget state exceeds 16 KiB.");
        sessions.SetVisualizationWidgetState(state.ThreadId, state.Path, new VisualizationWidgetState(modelContent, privateContent));
        subscribers?.NotifyOthers(
            Contract.AppServerRpc.VisualizationWidgetStateUpdated,
            state.ThreadId,
            connection,
            static candidate => candidate.SupportsVisualizations,
            new Contract.VisualizationWidgetStateUpdatedNotification
            {
                ThreadId = state.ThreadId,
                Path = Path.GetFullPath(state.Path),
                WidgetState = modelContent is null && privateContent is null
                    ? null
                    : new Contract.VisualizationWidgetState
                    {
                        ModelContent = ParseJson(modelContent),
                        PrivateContent = ParseJson(privateContent)
                    }
            });
        return Task.FromResult(AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new()));
    }

    private Task<AppServerTypedResult<Contract.VisualizationViewCloseResult>> CloseAsync(
        AppServerTypedRequest<Contract.VisualizationViewCloseParams> request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var viewHandle = Require(request.Params.ViewHandle, "viewHandle");
        return Task.FromResult(AppServerTypedResult<Contract.VisualizationViewCloseResult>.FromResult(
            new Contract.VisualizationViewCloseResult { Closed = _views.Close(viewHandle) }));
    }

    private VisualizationViewState GetBoundView(Protocol.Optional<string> viewHandle)
    {
        var state = _views.Get(Require(viewHandle, "viewHandle"));
        if (runtimeRegistry?.IsBoundTo(state.ThreadId, connection) == true)
            return state;
        _views.Close(state.Handle);
        throw VisualizationViewErrors.Create("stale_view", "The visualization view is no longer available.");
    }

    private static string? RawJson(Protocol.Optional<JsonElement?> value) =>
        value.IsSet && value.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element
            ? element.GetRawText()
            : null;

    private static JsonElement? ParseJson(string? json)
    {
        if (json is null)
            return null;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Require(Protocol.Optional<string> value, string field)
    {
        var result = value.IsSet ? value.Value : null;
        if (string.IsNullOrWhiteSpace(result))
            throw AppServerErrors.InvalidParams($"'{field}' is required.");
        return result;
    }

    private void EnsureAvailable()
    {
        if (!connection.SupportsVisualizations || store is null || runtimeRegistry is null)
            throw AppServerErrors.MethodNotFound(Protocol.AppServer.AppServerMethodNames.VisualizationViewOpen);
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
