using DotCraft.Plugins;
using Microsoft.Extensions.Logging;

namespace DotCraft.Runtime;

/// <summary>The trust half of the runtime manager: the activation gate and the grant and revoke operations.</summary>
/// <remarks>A grant binds the fingerprint the runtime accepted, never one the caller supplies.</remarks>
internal sealed partial class DotNetPluginRuntimeManager
{
    /// <inheritdoc />
    public async Task<PluginRuntimeMutationResult> TrustAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_nodes.TryGetValue(canonicalId, out var node))
            {
                return NotApplied(
                    canonicalId,
                    "PluginRuntimeNotDeclared",
                    "The selected plugin has no accepted .NET plugin runtime snapshot.");
            }

            bool granted;
            try
            {
                granted = _trust.Grant(canonicalId, node.Snapshot.Fingerprint);
            }
            catch (Exception exception)
                when (exception is PluginTrustPersistenceException or IOException or UnauthorizedAccessException)
            {
                // A grant that cannot be persisted must not activate anything.
                _logger?.LogWarning("Could not persist trust for .NET plugin {PluginId}.", canonicalId);
                return NotApplied(
                    canonicalId,
                    "PluginTrustNotPersisted",
                    "The trust grant could not be written to the machine-local trust authority.");
            }

            var affected = ReplanIdsFor(canonicalId);
            await ReplanAllAsync(affected, cancellationToken).ConfigureAwait(false);
            PublishSnapshot();
            return Result(
                granted ? PluginRuntimeMutationOutcome.Applied : PluginRuntimeMutationOutcome.NoChange,
                canonicalId,
                affected,
                []);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PluginRuntimeMutationResult> RevokeTrustAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var canonicalId = PluginIds.Canonicalize(pluginId);
        await _mutationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_nodes.TryGetValue(canonicalId, out var node))
            {
                return NotApplied(
                    canonicalId,
                    "PluginRuntimeNotDeclared",
                    "The selected plugin has no accepted .NET plugin runtime snapshot.");
            }

            var affected = ReplanIdsFor(canonicalId);
            try
            {
                if (!_trust.Revoke(canonicalId, node.Snapshot.Fingerprint))
                    return Result(PluginRuntimeMutationOutcome.NoChange, canonicalId, affected, []);
            }
            catch (Exception exception)
                when (exception is PluginTrustPersistenceException or IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning("Could not revoke trust for .NET plugin {PluginId}.", canonicalId);
                return NotApplied(
                    canonicalId,
                    "PluginTrustNotPersisted",
                    "The trust revocation could not be written to the machine-local trust authority.");
            }

            // A development build has an independent process-local qualification for these exact bytes.
            if (!HasAuthoringExecutionQualification(canonicalId, node.Snapshot.Fingerprint))
                await StopClosureAsync(canonicalId, node, cancellationToken).ConfigureAwait(false);
            await ReplanAllAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken)
                .ConfigureAwait(false);
            PublishSnapshot();
            return Result(PluginRuntimeMutationOutcome.Applied, canonicalId, affected, []);
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    /// <summary>Resolves one node's trust status against its accepted bundle fingerprint.</summary>
    private PluginDotnetTrustStatus TrustStatusOf(PluginRuntimeNode node) =>
        _trust.ResolveStatus(node.Snapshot.Manifest.Id, node.Snapshot.Fingerprint);

    /// <summary>Returns the blocker that keeps an untrusted or modified bundle out of the process.</summary>
    private PluginRuntimeBlocker[] TrustBlockers(PluginRuntimeNode node)
    {
        var blocker = TrustCommitBlocker(
            node.Snapshot.Manifest.Id,
            node.Snapshot.Fingerprint);
        return blocker == null ? [] : [blocker];
    }

    /// <summary>Re-reads the machine authority immediately before an activation is published.</summary>
    private PluginRuntimeBlocker? TrustCommitBlocker(string pluginId, string fingerprint)
    {
        if (HasAuthoringExecutionQualification(pluginId, fingerprint))
            return null;

        var status = _trust.ResolveStatus(pluginId, fingerprint);
        if (status == PluginDotnetTrustStatus.Trusted)
            return null;

        var fingerprintPrefix = Prefix(fingerprint);
        if (status == PluginDotnetTrustStatus.Modified)
        {
            return Blocker(
                PluginDotnetDiagnosticCodes.TrustModified,
                $"Plugin '{pluginId}' changed since it was trusted and must be confirmed again.",
                ("pluginId", pluginId),
                ("trustStatus", StatusName(status)),
                ("fingerprintPrefix", fingerprintPrefix),
                ("trustedFingerprintPrefix", Prefix(_trust.TrustedFingerprint(pluginId))));
        }

        return Blocker(
            PluginDotnetDiagnosticCodes.Untrusted,
            $"Plugin '{pluginId}' runs .NET code in the DotCraft process and is not trusted yet.",
            ("pluginId", pluginId),
            ("trustStatus", StatusName(status)),
            ("fingerprintPrefix", fingerprintPrefix));
    }

    /// <summary>Returns the wire-stable spelling of a trust status.</summary>
    private static string StatusName(PluginDotnetTrustStatus status) =>
        status.ToString().ToLowerInvariant();

    /// <summary>Shortens a fingerprint to the prefix a client displays.</summary>
    private static string? Prefix(string? fingerprint) =>
        string.IsNullOrEmpty(fingerprint)
            ? null
            : fingerprint[..Math.Min(12, fingerprint.Length)];
}
