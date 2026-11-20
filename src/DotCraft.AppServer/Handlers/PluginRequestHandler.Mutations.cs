using DotCraft.Plugins;
using System.Runtime.ExceptionServices;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private void MapSnapshotRead<TParams, TResult>(
        AppServerMethodTable table,
        DotCraft.Protocol.RpcRequest<TParams, TResult> descriptor,
        Func<AppServerTypedRequest<TParams>, CancellationToken, Task<AppServerTypedResult<TResult>>> read)
        where TParams : class
        where TResult : class =>
        table.Map(descriptor, (request, ct) => managementState.RunSnapshotReadAsync(
            token => read(request, token),
            ct));

    private void MapMutation<TParams, TResult>(
        AppServerMethodTable table,
        DotCraft.Protocol.RpcRequest<TParams, TResult> descriptor,
        Func<AppServerTypedRequest<TParams>, CancellationToken, Task<AppServerTypedResult<TResult>>> mutation)
        where TParams : class
        where TResult : class =>
        table.Map(descriptor, (request, ct) => managementState.RunMutationAsync(
            token => mutation(request, token),
            ct));

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> HandlePluginSetTrustedAsync(
        AppServerTypedRequest<Contract.PluginSetTrustedParams> request,
        CancellationToken ct)
    {
        var pluginId = RequireLifecyclePluginId(request.Params.Id);
        if (dotnetRuntime == null)
            throw AppServerErrors.InvalidParams("The code plugin runtime is unavailable.");

        var commitToken = EnterMutationCommit(ct);
        var mutation = request.Params.Trusted
            ? await dotnetRuntime.TrustAsync(pluginId, commitToken).ConfigureAwait(false)
            : await dotnetRuntime.RevokeTrustAsync(pluginId, commitToken).ConfigureAwait(false);
        if (mutation.Outcome == PluginRuntimeMutationOutcome.Applied)
            AdvancePluginSnapshotRevision();
        var result = BuildOperationResult(
            RefreshPluginRuntime(),
            pluginId,
            MapOutcome(mutation.Outcome),
            mutation.AffectedPluginIds,
            mutation.Diagnostics);
        return await WriteWithSnapshotInvalidationAsync(
                request.Message,
                result,
                mutation.AffectedPluginIds,
                ct,
                notify: mutation.Outcome == PluginRuntimeMutationOutcome.Applied)
            .ConfigureAwait(false);
    }

    // A committed mutation must finish even if the initiating client disconnects.
    private static CancellationToken EnterMutationCommit(CancellationToken requestCancellationToken)
    {
        requestCancellationToken.ThrowIfCancellationRequested();
        return CancellationToken.None;
    }

    private Task<PluginDiscoveryResult> FinalizePluginContributionRefreshAsync(
        string source,
        CancellationToken ct) =>
        managedPluginRefresher.FinalizePluginContributionRefreshAsync(source, ct);

    private async Task<AppServerTypedResult<Contract.PluginOperationResult>> WriteWithSnapshotInvalidationAsync(
        AppServerIncomingMessage message,
        Contract.PluginOperationResult result,
        IEnumerable<string> pluginIds,
        CancellationToken ct,
        bool notify = true)
    {
        var notification = notify
            ? new Contract.PluginSnapshotUpdatedNotification
            {
                SnapshotRevision = CurrentPluginSnapshotRevision,
                PluginIds = pluginIds.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray()
            }
            : null;
        var responseBarrier = notification == null ? null : QueuePluginSnapshotUpdated(notification);
        managementState.CompleteCurrentMutation();

        var responseFailure = await TryWriteMutationResponseAsync(message, result, ct).ConfigureAwait(false);
        responseBarrier?.TrySetResult();
        if (notify)
        {
            try
            {
                if (responseBarrier == null && responseFailure == null)
                    await NotifyInitiatingPluginSnapshotUpdatedAsync(notification!, ct).ConfigureAwait(false);
            }
            catch when (responseFailure != null)
            {
            }
        }

        responseFailure?.Throw();
        return AppServerTypedResult<Contract.PluginOperationResult>.Written;
    }

    private async Task<ExceptionDispatchInfo?> TryWriteMutationResponseAsync<TResult>(
        AppServerIncomingMessage message,
        TResult result,
        CancellationToken ct)
        where TResult : class
    {
        try
        {
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(message.Id, result), ct)
                .ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return ExceptionDispatchInfo.Capture(exception);
        }
    }
}
