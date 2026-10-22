using System.Text;
using System.Text.Json;
using DotCraft.GenerativeUi;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed class GenerativeUiRequestHandler(
    ISessionService sessions,
    AppServerConnection connection,
    GenerativeUiRuntimeRegistry? runtimeRegistry,
    AppServerThreadSubscribers? subscribers) : IAppServerDomainHandler
{
    private const int MaxStateBytes = 16 * 1024;

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.GenerativeUiStateRead, ReadAsync);
        table.Map(Contract.AppServerRpc.GenerativeUiStateUpdate, UpdateAsync);
    }

    private async Task<AppServerTypedResult<Contract.GenerativeUiStateReadResult>> ReadAsync(
        AppServerTypedRequest<Contract.GenerativeUiStateReadParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable(Contract.AppServerMethodNames.GenerativeUiStateRead);
        var threadId = Require(request.Params.ThreadId, "threadId");
        _ = await GetThreadAsync(threadId, cancellationToken);
        var states = sessions.GetGenerativeUiStates(threadId)
            .Select(state => new Contract.GenerativeUiBlockState
            {
                TurnId = state.TurnId,
                ItemId = state.ItemId,
                BlockIndex = state.BlockIndex,
                State = ParseJson(state.StateJson)
            })
            .ToArray();
        return AppServerTypedResult<Contract.GenerativeUiStateReadResult>.FromResult(
            new Contract.GenerativeUiStateReadResult { States = states });
    }

    private async Task<AppServerTypedResult<Protocol.RpcEmpty>> UpdateAsync(
        AppServerTypedRequest<Contract.GenerativeUiStateUpdateParams> request,
        CancellationToken cancellationToken)
    {
        EnsureAvailable(Contract.AppServerMethodNames.GenerativeUiStateUpdate);
        var parameters = request.Params;
        var threadId = Require(parameters.ThreadId, "threadId");
        var turnId = Require(parameters.TurnId, "turnId");
        var itemId = Require(parameters.ItemId, "itemId");
        if (parameters.BlockIndex < 0)
            throw Error("invalid_block", "The Generative UI block index is invalid.");
        var stateJson = parameters.State.IsSet
            && parameters.State.Value is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } state
                ? state.GetRawText()
                : null;
        if (stateJson != null && Encoding.UTF8.GetByteCount(stateJson) > MaxStateBytes)
            throw Error("state_too_large", "The Generative UI state exceeds 16 KiB.");

        var thread = await GetThreadAsync(threadId, cancellationToken);
        var item = await sessions.ReadThreadItemAsync(thread.Id, turnId, itemId, cancellationToken);
        if (item is not { Type: ItemType.AgentMessage, Status: ItemStatus.Completed })
            throw Error("not_found", "The Generative UI message is unavailable.");

        sessions.SetGenerativeUiState(thread.Id, turnId, itemId, parameters.BlockIndex, stateJson);
        subscribers?.NotifyOthers(
            Contract.AppServerRpc.GenerativeUiStateUpdated,
            thread.Id,
            connection,
            static candidate => candidate.SupportsGenerativeUi,
            new Contract.GenerativeUiStateUpdatedNotification
            {
                ThreadId = thread.Id,
                TurnId = turnId,
                ItemId = itemId,
                BlockIndex = parameters.BlockIndex,
                State = stateJson is null ? null : ParseJson(stateJson)
            });
        return AppServerTypedResult<Protocol.RpcEmpty>.FromResult(new());
    }

    private async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        try
        {
            return await sessions.GetThreadAsync(threadId, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            throw Error("not_found", "The thread is unavailable.");
        }
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw AppServerErrors.InvalidParams($"'{field}' is required.") : value;

    private static AppServerException Error(string code, string fallbackText) =>
        new(AppServerErrors.InvalidParamsCode, fallbackText, new AppServerErrorData
        {
            Code = code,
            MessageKey = $"errors.generativeUi.{code}",
            FallbackText = fallbackText
        });

    private void EnsureAvailable(string method)
    {
        if (!connection.SupportsGenerativeUi || runtimeRegistry is null)
            throw AppServerErrors.MethodNotFound(method);
    }
}
