using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Plugins;
using System.Runtime.ExceptionServices;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
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
        var mutation = Read(request.Params.Trusted)
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

    // Once a serialized mutation starts changing persistent or live state it must converge even
    // when the initiating client disconnects. Request cancellation still applies while queueing,
    // validating, and reading the pre-mutation snapshot.
    private static CancellationToken EnterMutationCommit(CancellationToken requestCancellationToken)
    {
        requestCancellationToken.ThrowIfCancellationRequested();
        return CancellationToken.None;
    }

    private async Task<PluginDiscoveryResult> FinalizePluginContributionRefreshAsync(
        string source,
        CancellationToken ct)
    {
        var discovery = RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct);
        await ReconnectEffectiveLspRuntimeAsync(ct);
        RefreshHooksAfterPluginChange();
        appConfigMonitor?.NotifyChanged(
            source,
            [ConfigChangeRegions.Plugins, ConfigChangeRegions.Skills, ConfigChangeRegions.Mcp, ConfigChangeRegions.Lsp, ConfigChangeRegions.Hooks]);
        AppServerContextInvalidation.MarkSkills(contextPageManager);
        return discovery;
    }

    private async Task<PluginDiagnostic?> QuiesceRootBackedContributionsAsync(
        string pluginId,
        CancellationToken ct)
    {
        var current = appConfigMonitor?.Current ?? new AppConfig();
        var originalDisabled = current.Plugins.DisabledPlugins.ToList();
        var temporarilyDisabled = PluginsConfigPersistence.NormalizeDisabledPluginIds(originalDisabled).ToList();
        if (!temporarilyDisabled.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            temporarilyDisabled.Add(pluginId);

        PluginDiagnostic? failure = null;
        current.Plugins.DisabledPlugins = temporarilyDisabled;
        try
        {
            await RestoreRootBackedContributionsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = PluginDiagnostic.Error(
                "PluginContributionQuiesceFailed",
                "The plugin's root-backed contributions could not be stopped.",
                pluginId,
                parameters: new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["phase"] = System.Text.Json.JsonSerializer.SerializeToElement("quiesce"),
                    ["failureType"] = System.Text.Json.JsonSerializer.SerializeToElement(exception.GetType().Name)
                });
        }
        finally
        {
            current.Plugins.DisabledPlugins = originalDisabled;
        }

        if (failure == null)
            return null;

        try
        {
            await RestoreRootBackedContributionsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The stable quiesce diagnostic remains authoritative; recovery is best effort.
        }
        return failure;
    }

    private async Task RestoreRootBackedContributionsAsync(CancellationToken ct)
    {
        RefreshPluginRuntime();
        await ReconnectEffectiveMcpRuntimeAsync(await GetWorkspaceMcpServersAsync(ct), ct).ConfigureAwait(false);
        await ReconnectEffectiveLspRuntimeAsync(ct).ConfigureAwait(false);
        RefreshHooksAfterPluginChange();
        AppServerContextInvalidation.MarkSkills(contextPageManager);
    }

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
                // Preserve the response failure after a best-effort direct notification. A host
                // broadcast does not use the failed initiating transport and will have completed.
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
