using DotCraft.Plugins;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>The generation half of the runtime manager: one activation transaction and one teardown.</summary>
internal sealed partial class DotNetPluginRuntimeManager
{
    private async Task ActivateNodeAsync(PluginRuntimeNode node, CancellationToken _)
    {
        if (node.Generation != null || node.PendingActivation != null || node.PendingTeardown != null)
            return;
        if (!node.Enabled)
        {
            node.State = PluginDotnetRuntimeState.Stopped;
            PublishSnapshot();
            return;
        }

        // The last gate before any loader work: nothing below runs for a bundle whose preflight
        // failed or whose bytes the user has not confirmed.
        var admissionBlockers = PreflightBlockers(node.Snapshot).Concat(TrustBlockers(node)).ToArray();
        if (admissionBlockers.Length > 0)
        {
            node.State = PluginDotnetRuntimeState.Blocked;
            node.Blockers = admissionBlockers;
            PublishSnapshot();
            return;
        }

        node.PendingRemnant = null;
        node.State = PluginDotnetRuntimeState.Activating;
        node.Blockers = [];
        var generationId = $"g{Interlocked.Increment(ref _generationSequence):x}-{Guid.NewGuid():N}";
        node.GenerationId = generationId;
        PublishSnapshot();

        string shadowRoot;
        try
        {
            shadowRoot = _bundleStore.CreateGenerationCopy(node.Snapshot, generationId);
        }
        catch (Exception)
        {
            node.GenerationId = null;
            node.State = PluginDotnetRuntimeState.Blocked;
            node.Blockers =
            [
                PluginActivationBlockers.Create(
                    PluginDotnetDiagnosticCodes.EntryAssemblyInvalid,
                    "The plugin generation copy could not be prepared.",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["assemblyPath"] = node.Snapshot.Manifest.Dotnet!.EntryAssembly,
                        ["reason"] = "shadowCopyFailed"
                    })
            ];
            ReportFailedActivation(node, generationId);
            PublishSnapshot();
            return;
        }

        using var activationCts = new CancellationTokenSource();
        var commitGate = new PluginActivationCommitGate();
        var dataRoot = _paths.UserData.ResolveOrNull("plugins", node.Snapshot.Manifest.Id)
                       ?? _paths.Data.Resolve("plugin-data", node.Snapshot.Manifest.Id);
        Directory.CreateDirectory(dataRoot);
        var activation = Task.Run(() => PluginGeneration.CreateAsync(
            node.Snapshot,
            generationId,
            shadowRoot,
            dataRoot,
            node.WorkspaceRoot,
            new PluginGenerationHost(_services, _contributions, CallGates, _config),
            GetDirectProviderGenerations(node),
            commitGate,
            TrustCommitBlocker,
            activationCts.Token));
        node.PendingActivation = activation;
        PluginActivationAttempt attempt;
        try
        {
            attempt = await activation.WaitAsync(_options.ActivationTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Commit and timeout are one linearized race. If commit already won, the activation is
            // accepted; otherwise the callback can finish only into rollback.
            if (!commitGate.TryCancel())
            {
                attempt = await activation.ConfigureAwait(false);
                node.PendingActivation = null;
                await CompleteActivationAttemptAsync(node, attempt).ConfigureAwait(false);
                return;
            }

            activationCts.Cancel();
            node.State = PluginDotnetRuntimeState.Faulted;
            node.Blockers =
            [
                PluginActivationBlockers.Create(
                    PluginActivationBlockers.ActivationTimeout,
                    "Plugin activation did not complete before the deadline.",
                    new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["phase"] = "activation",
                        ["generationId"] = generationId
                    })
            ];

            var teardown = AbandonedActivationAsync(
                activation,
                node.Snapshot.Manifest.Id,
                generationId,
                shadowRoot);
            node.PendingTeardown = teardown;
            node.PendingTeardownCompletedState = PluginDotnetRuntimeState.Faulted;
            ObservePendingTeardown(node, teardown);
            ReportFailedActivation(node, generationId);
            PublishSnapshot();
            return;
        }

        node.PendingActivation = null;
        await CompleteActivationAttemptAsync(node, attempt).ConfigureAwait(false);
    }

    private async Task CompleteActivationAttemptAsync(
        PluginRuntimeNode node,
        PluginActivationAttempt attempt)
    {
        if (attempt.Generation != null)
        {
            node.Generation = attempt.Generation;
            node.State = PluginDotnetRuntimeState.Active;
            node.Blockers = [];
            // Describe before publishing so the projection carries the generation's Tools without
            // waiting for a thread to plan.
            await DescribeToolsAsync(CancellationToken.None).ConfigureAwait(false);
            PublishSnapshot();
            var generation = attempt.Generation;
            generation.StartWork(message => _ = HandleWorkFailureAsync(
                node.Snapshot.Manifest.Id,
                generation.GenerationId,
                message));
            return;
        }

        if (attempt.Remnant != null)
            ReclaimOrTrack(node, attempt.Remnant);

        var generationId = node.GenerationId;
        node.GenerationId = null;
        node.State = attempt.DeterministicFailure || attempt.CommitRejected
            ? PluginDotnetRuntimeState.Blocked
            : PluginDotnetRuntimeState.Faulted;
        node.Blockers = attempt.FailureBlocker != null
            ? [attempt.FailureBlocker]
            : [Blocker(
                PluginActivationBlockers.ActivationFailed,
                attempt.Error ?? "Plugin activation failed.",
                ("phase", "activation"))];
        ReportFailedActivation(node, generationId);
        PublishSnapshot();
    }

    /// <returns><see langword="true"/> once functional teardown has completed.</returns>
    private async Task<bool> StopNodeAsync(
        PluginRuntimeNode node,
        PluginDotnetRuntimeState completedState,
        CancellationToken _)
    {
        if (node.PendingTeardown != null)
        {
            node.PendingTeardownCompletedState = completedState;
            node.RetryAfterPendingTeardown = false;
            node.State = PluginDotnetRuntimeState.Deactivating;
            node.Blockers = [];
            PublishSnapshot();
            return await AwaitPendingTeardownAsync(node, node.PendingTeardown).ConfigureAwait(false);
        }

        if (node.Generation == null)
        {
            if (node.State != PluginDotnetRuntimeState.Reclaiming)
            {
                node.State = completedState;
                node.GenerationId = null;
                node.Blockers = [];
            }

            PublishSnapshot();
            return true;
        }

        var generation = node.Generation;
        node.State = PluginDotnetRuntimeState.Deactivating;
        node.Blockers = [];
        PublishSnapshot();

        // Revoke before drain: BeginCleanup revokes every contribution handle before its first await,
        // so routing stops when this call is made, not when it completes.
        var teardown = generation.BeginCleanup();
        node.PendingTeardown = teardown;
        node.PendingTeardownCompletedState = completedState;
        return await AwaitPendingTeardownAsync(node, teardown).ConfigureAwait(false);
    }

    private async Task<bool> AwaitPendingTeardownAsync(
        PluginRuntimeNode node,
        Task<PluginGenerationRemnant> teardown)
    {
        try
        {
            var remnant = await teardown.WaitAsync(_options.CleanupTimeout).ConfigureAwait(false);
            CompletePendingTeardown(node, teardown, remnant);
            PublishSnapshot();
            return true;
        }
        catch (TimeoutException)
        {
            var generationId = node.GenerationId ?? node.Snapshot.Manifest.Id;
            if (!node.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == PluginActivationBlockers.DrainTimeout
                    && diagnostic.Parameters.TryGetValue("generationId", out var observed)
                    && string.Equals(observed.GetString(), generationId, StringComparison.Ordinal)))
            {
                AddDiagnostic(
                    node,
                    PluginActivationBlockers.DrainTimeout,
                    "Plugin cleanup did not complete before the deadline.",
                    "drain",
                    generationId);
            }

            ObservePendingTeardown(node, teardown);
            PublishSnapshot();
            return false;
        }
    }

    private void CompletePendingTeardown(
        PluginRuntimeNode node,
        Task<PluginGenerationRemnant> teardown,
        PluginGenerationRemnant remnant)
    {
        if (!ReferenceEquals(node.PendingTeardown, teardown))
            return;

        var completedState = node.PendingTeardownCompletedState;
        node.PendingActivation = null;
        node.PendingTeardown = null;
        node.Generation = null;
        node.GenerationId = null;
        node.Blockers = completedState == PluginDotnetRuntimeState.Faulted ? node.Blockers : [];
        if (ReclaimOrTrack(node, remnant))
        {
            node.PendingRemnant = null;
            node.State = completedState;
        }
        else if (completedState == PluginDotnetRuntimeState.Faulted)
        {
            node.PendingRemnant = null;
            node.State = completedState;
        }
        else
        {
            node.PendingRemnant = remnant;
            node.ReclaimCompletedState = completedState;
            node.State = PluginDotnetRuntimeState.Reclaiming;
        }
    }

    private void ObservePendingTeardown(
        PluginRuntimeNode node,
        Task<PluginGenerationRemnant> teardown) =>
        _ = CompletePendingTeardownAsync(node, teardown);

    private async Task CompletePendingTeardownAsync(
        PluginRuntimeNode node,
        Task<PluginGenerationRemnant> teardown)
    {
        PluginGenerationRemnant remnant;
        try
        {
            remnant = await teardown.ConfigureAwait(false);
        }
        catch (Exception)
        {
            _logger?.LogError(
                "Plugin {PluginId} generation {GenerationId} teardown failed unexpectedly.",
                node.Snapshot.Manifest.Id,
                node.GenerationId);
            return;
        }

        if (_stopping)
            return;
        await _mutationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopping)
                return;
            if (!ReferenceEquals(node.PendingTeardown, teardown))
                return;

            var retry = node.RetryAfterPendingTeardown && node.Enabled;
            node.RetryAfterPendingTeardown = false;
            CompletePendingTeardown(node, teardown, remnant);
            await ResumePendingStopClosuresAsync().ConfigureAwait(false);
            if (retry && !_disposed)
            {
                await ReplanAllAsync(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            node.Snapshot.Manifest.Id
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>Records a remnant's teardown errors and either releases or watches it.</summary>
    /// <returns><see langword="true"/> when nothing is left to reclaim.</returns>
    private bool ReclaimOrTrack(PluginRuntimeNode node, PluginGenerationRemnant remnant)
    {
        foreach (var error in remnant.CleanupErrors)
        {
            AddDiagnostic(
                node,
                PluginActivationBlockers.CleanupFailed,
                error,
                "cleanup",
                remnant.GenerationId);
        }

        if (remnant.LoadContext is { IsAlive: true })
        {
            _reclaim.Track(remnant);
            return false;
        }

        ReleaseGenerationCopy(remnant.ShadowCopyPath);
        return true;
    }

    /// <summary>Deletes a collected generation's shadow copy, or hands it on to be retried.</summary>
    private void ReleaseGenerationCopy(string shadowCopyPath)
    {
        if (!_bundleStore.DeleteGeneration(shadowCopyPath))
            _reclaim.TrackDeletion(shadowCopyPath);
    }

    private async Task ReclaimAsync(PluginGenerationRemnant remnant, CancellationToken cancellationToken)
    {
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SettleReclaimed(remnant);
            PublishSnapshot();
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>Releases a collected generation and settles the node still waiting on it.</summary>
    private void SettleReclaimed(PluginGenerationRemnant remnant)
    {
        ReleaseGenerationCopy(remnant.ShadowCopyPath);
        if (!_nodes.TryGetValue(remnant.PluginId, out var node)
            || node.State != PluginDotnetRuntimeState.Reclaiming
            || (node.PendingRemnant != null && !ReferenceEquals(node.PendingRemnant, remnant)))
        {
            return;
        }

        node.PendingRemnant = null;
        node.GenerationId = null;
        node.Blockers = [];
        node.State = node.ReclaimCompletedState;
    }

    /// <summary>Tears down whatever an abandoned activation attempt ends up owning.</summary>
    private static async Task<PluginGenerationRemnant> AbandonedActivationAsync(
        Task<PluginActivationAttempt> activation,
        string pluginId,
        string generationId,
        string shadowRoot)
    {
        PluginActivationAttempt attempt;
        try
        {
            attempt = await activation.ConfigureAwait(false);
        }
        catch
        {
            return new PluginGenerationRemnant(null, pluginId, generationId, shadowRoot, []);
        }

        if (attempt.Generation != null)
            return await attempt.Generation.BeginCleanup().ConfigureAwait(false);
        return attempt.Remnant
               ?? new PluginGenerationRemnant(null, pluginId, generationId, shadowRoot, []);
    }
}
