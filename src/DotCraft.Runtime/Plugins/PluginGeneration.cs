using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DotCraft.Contributions;
using DotCraft.Plugins;

namespace DotCraft.Runtime;

internal sealed record PluginGenerationHost(
    IServiceProvider Services,
    IContributionRegistry Contributions,
    PluginCallGateRegistry CallGates,
    IReadOnlySet<string> HostSharedAssemblies,
    bool ToolsOnly = false,
    bool ModelsOnly = false);

/// <summary>The poller's reference to the load context must be weak so it can observe collection.</summary>
internal sealed record PluginGenerationRemnant(
    WeakReference? LoadContext,
    string PluginId,
    string GenerationId,
    string ShadowCopyPath,
    IReadOnlyList<string> CleanupErrors);

/// <summary>Retrying with the same activation inputs cannot resolve a deterministic failure.</summary>
internal sealed record PluginActivationAttempt(
    PluginGeneration? Generation,
    string? Error,
    bool DeterministicFailure,
    PluginRuntimeBlocker? FailureBlocker,
    PluginGenerationRemnant? Remnant,
    bool CommitRejected = false);

internal sealed class PluginActivationCommitGate
{
    private const int Open = 0;
    private const int Committed = 1;
    private const int Cancelled = 2;
    private int _state;

    public bool TryCancel() =>
        Interlocked.CompareExchange(ref _state, Cancelled, Open) == Open;

    public bool TryCommit(Func<bool> canCommit, Action commit)
    {
        if (Volatile.Read(ref _state) != Open)
            return false;

        // External trust checks can throw, so validate before committing while rollback is still possible.
        if (!canCommit())
        {
            TryCancel();
            return false;
        }

        if (Interlocked.CompareExchange(ref _state, Committed, Open) != Open)
            return false;

        commit();
        return true;
    }
}

internal sealed class PluginGeneration
{
    private readonly object _gate = new();
    private readonly PluginCallGate _calls;
    private readonly PluginCallGateRegistry _callGates;
    private PluginLoadContext? _loadContext;
    private IDotCraftPlugin? _entry;
    private PluginLifetime? _lifetime;
    private PluginContributionRegistrar? _registrar;
    private PluginServiceExportRegistry? _exports;
    private ProviderApiSet _reachableApis;
    private Task<PluginGenerationRemnant>? _cleanupTask;

    private PluginGeneration(
        string pluginId,
        string generationId,
        string shadowRoot,
        PluginCallGateRegistry callGates,
        PluginCallGate calls,
        PluginLoadContext loadContext,
        IDotCraftPlugin entry,
        PluginLifetime lifetime,
        PluginContributionRegistrar registrar,
        PluginServiceExportRegistry exports,
        ProviderApiSet reachableApis)
    {
        PluginId = pluginId;
        GenerationId = generationId;
        ShadowRoot = shadowRoot;
        _callGates = callGates;
        _calls = calls;
        _loadContext = loadContext;
        _entry = entry;
        _lifetime = lifetime;
        _registrar = registrar;
        _exports = exports;
        _reachableApis = reachableApis;
    }

    public string PluginId { get; }

    public string GenerationId { get; }

    public string ShadowRoot { get; }

    public static async Task<PluginActivationAttempt> CreateAsync(
        PluginAcceptedSnapshot snapshot,
        string generationId,
        string shadowRoot,
        string dataRoot,
        string workspaceRoot,
        JsonElement settings,
        PluginGenerationHost host,
        IReadOnlyDictionary<string, PluginGeneration> directProviders,
        PluginActivationCommitGate commitGate,
        Func<string, string, PluginRuntimeBlocker?> finalCommitBlocker,
        CancellationToken cancellationToken)
    {
        PluginLoadContext? loadContext = null;
        PluginLifetime? lifetime = null;
        PluginCallGate? calls = null;
        PluginContributionRegistrar? registrar = null;
        PluginServiceExportRegistry? exports = null;
        IDotCraftPlugin? entry = null;
        var deterministicFailure = true;
        try
        {
            var entryPath = Resolve(shadowRoot, snapshot.Manifest.Dotnet!.EntryAssembly);
            var providerApis = BuildProviderApiMap(directProviders);
            RejectOwnedApiConflicts(snapshot, shadowRoot, providerApis);
            loadContext = new PluginLoadContext(
                snapshot.Manifest.Id,
                shadowRoot,
                entryPath,
                providerApis.Assemblies,
                host.HostSharedAssemblies);
            var exportedApis = LoadExportedApis(snapshot, shadowRoot, loadContext);
            var reachableApis = AddOwnedApis(providerApis, snapshot.Manifest.Id, exportedApis);
            var assembly = loadContext.LoadFromAssemblyPath(entryPath);
            var entryType = assembly.GetType(snapshot.Manifest.Dotnet.EntryType, throwOnError: true, ignoreCase: false)!;
            deterministicFailure = false;
            entry = Activator.CreateInstance(entryType) as IDotCraftPlugin
                ?? throw new InvalidOperationException("Plugin entry type does not implement IDotCraftPlugin.");
            lifetime = new PluginLifetime();
            lifetime.SetEntryInstance(entry);
            calls = new PluginCallGate();
            registrar = new PluginContributionRegistrar(
                host.Contributions,
                ContributionOrigin.Plugin(snapshot.Manifest.Id, generationId),
                calls,
                entry,
                host.ToolsOnly,
                lifetime.Stopping,
                host.ModelsOnly);
            exports = new PluginServiceExportRegistry(
                snapshot.Manifest.Id,
                new HashSet<Assembly>(exportedApis.Values, ReferenceEqualityComparer.Instance));
            var dependencies = new PluginDependencyResolver(snapshot.Manifest.Id, directProviders);
            var context = new PluginActivationContext(
                new PluginIdentity(snapshot.Manifest.Id, snapshot.Manifest.Version!, generationId),
                shadowRoot,
                dataRoot,
                workspaceRoot,
                settings,
                new PluginVisibleServiceProvider(host.Services),
                registrar,
                lifetime,
                exports,
                dependencies);
            await entry.ActivateAsync(context, cancellationToken).ConfigureAwait(false);
            exports.ValidateOwnership(entry, lifetime);

            lifetime.Seal();
            exports.Seal();
            dependencies.Seal();
            cancellationToken.ThrowIfCancellationRequested();
            PluginRuntimeBlocker? commitBlocker = null;
            if (!commitGate.TryCommit(
                () => (commitBlocker = finalCommitBlocker(
                    snapshot.Manifest.Id,
                    snapshot.DotnetFingerprint)) == null,
                () =>
                {
                    host.CallGates.Publish(snapshot.Manifest.Id, generationId, calls);
                    registrar.Commit();
                }))
            {
                if (commitBlocker != null)
                    throw new PluginActivationCommitRejectedException(commitBlocker);
                throw new OperationCanceledException(cancellationToken);
            }
            var generation = new PluginGeneration(
                snapshot.Manifest.Id,
                generationId,
                shadowRoot,
                host.CallGates,
                calls,
                loadContext,
                entry,
                lifetime,
                registrar,
                exports,
                reachableApis);
            return new PluginActivationAttempt(generation, null, false, null, null);
        }
        catch (Exception exception)
        {
            if (exception is PluginServiceBindingException)
                deterministicFailure = true;
            var commitRejected = exception as PluginActivationCommitRejectedException;
            var error = commitRejected?.Blocker.Message ?? CopyExceptionMessage(exception);
            var blocker = commitRejected?.Blocker
                          ?? PluginActivationBlockers.Describe(
                              exception,
                              snapshot,
                              deterministicFailure,
                              error);
            registrar?.Revoke();
            var callDrain = calls?.CloseAsync() ?? Task.CompletedTask;
            host.CallGates.Revoke(snapshot.Manifest.Id, generationId);
            try
            {
                lifetime?.SignalStopping();
            }
            catch
            {
            }
            await callDrain.ConfigureAwait(false);
            var cleanupErrors = registrar == null
                ? []
                : await registrar.DisposeTargetsAsync().ConfigureAwait(false);
            exports?.Clear();
            await DisposeEntryAndLifetimeAsync(entry, lifetime).ConfigureAwait(false);
            var released = loadContext == null ? null : ReleaseLoadContext(loadContext);
            return new PluginActivationAttempt(
                null,
                error,
                deterministicFailure,
                blocker,
                new PluginGenerationRemnant(
                    released,
                    snapshot.Manifest.Id,
                    generationId,
                    shadowRoot,
                    cleanupErrors),
                CommitRejected: commitRejected != null);
        }
    }

    public void StartWork(Action<string> onFailure) => _lifetime!.StartWork(onFailure);

    public object GetRequiredExport(Type contract)
    {
        try
        {
            return _exports!.GetRequired(contract);
        }
        catch (InvalidOperationException exception)
        {
            throw PluginServiceBindingException.Missing(
                PluginId,
                contract.FullName ?? contract.Name,
                exception.Message);
        }
    }

    /// <summary>Closes routing synchronously before returning the task that drains callers and releases resources.</summary>
    public Task<PluginGenerationRemnant> BeginCleanup()
    {
        lock (_gate)
        {
            return _cleanupTask ??= CleanupCoreAsync();
        }
    }

    private async Task<PluginGenerationRemnant> CleanupCoreAsync()
    {
        var errors = new List<string>();
        var lifetime = _lifetime;
        var entry = _entry;

        var registrar = _registrar;
        _registrar = null;
        if (registrar != null)
        {
            try
            {
                registrar.Revoke();
            }
            catch (Exception exception)
            {
                errors.Add(CopyExceptionMessage(exception));
            }
        }

        // Close the gate before removing its lookup entry so callers holding it cannot enter.
        var callDrain = _calls.CloseAsync();
        _callGates.Revoke(PluginId, GenerationId);
        if (lifetime != null)
        {
            try
            {
                lifetime.SignalStopping();
            }
            catch (Exception exception)
            {
                errors.Add(CopyExceptionMessage(exception));
            }
        }
        await callDrain.ConfigureAwait(false);

        if (lifetime != null)
        {
            try
            {
                await lifetime.DrainAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(CopyExceptionMessage(exception));
            }
        }

        if (registrar != null)
            errors.AddRange(await registrar.DisposeTargetsAsync().ConfigureAwait(false));

        _exports?.Clear();

        if (entry != null)
        {
            try
            {
                if (entry is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (entry is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception exception)
            {
                errors.Add(CopyExceptionMessage(exception));
            }
        }

        if (lifetime != null)
            errors.AddRange(await lifetime.DisposeResourcesAsync(CancellationToken.None).ConfigureAwait(false));

        _entry = null;
        _lifetime = null;
        _exports = null;
        _reachableApis = ProviderApiSet.Empty;
        lifetime?.Dispose();
        var loadContext = _loadContext!;
        _loadContext = null;
        return new PluginGenerationRemnant(
            ReleaseLoadContext(loadContext),
            PluginId,
            GenerationId,
            ShadowRoot,
            errors);
    }

    private static async Task DisposeEntryAndLifetimeAsync(
        IDotCraftPlugin? entry,
        PluginLifetime? lifetime)
    {
        if (entry != null)
        {
            try
            {
                if (entry is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else if (entry is IDisposable disposable)
                    disposable.Dispose();
            }
            catch
            {
            }
        }

        if (lifetime != null)
        {
            try
            {
                await lifetime.DisposeResourcesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
            lifetime.Dispose();
        }
    }

    // Inlining can keep the load context alive in the caller's frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReleaseLoadContext(PluginLoadContext loadContext)
    {
        var weakReference = new WeakReference(loadContext, trackResurrection: false);
        loadContext.Unload();
        return weakReference;
    }

    internal static string CopyExceptionMessage(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null })
            exception = exception.InnerException;
        return exception is PluginServiceBindingException or PluginContributionException
            ? exception.Message
            : "Plugin operation failed.";
    }

    private static string Resolve(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath[2..].Replace('/', Path.DirectorySeparatorChar)));

    private static ProviderApiSet BuildProviderApiMap(
        IReadOnlyDictionary<string, PluginGeneration> directProviders)
    {
        var result = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in directProviders.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var api in provider.Value._reachableApis.Assemblies.OrderBy(
                         static pair => pair.Key,
                         StringComparer.Ordinal))
            {
                var owner = provider.Value._reachableApis.Owners[api.Key];
                if (!result.TryAdd(api.Key, api.Value))
                {
                    if (string.Equals(owners[api.Key], owner, StringComparison.Ordinal))
                        continue;

                    var providerIds = new[] { owners[api.Key], owner }
                        .OrderBy(static id => id, StringComparer.Ordinal)
                        .ToArray();
                    throw PluginServiceBindingException.ApiConflict(api.Key, providerIds);
                }
                owners.Add(api.Key, owner);
            }
        }
        return new ProviderApiSet(result, owners);
    }

    private static ProviderApiSet AddOwnedApis(
        ProviderApiSet providerApis,
        string pluginId,
        IReadOnlyDictionary<string, Assembly> exportedApis)
    {
        var assemblies = new Dictionary<string, Assembly>(
            providerApis.Assemblies,
            StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(
            providerApis.Owners,
            StringComparer.OrdinalIgnoreCase);
        foreach (var api in exportedApis)
        {
            assemblies.Add(api.Key, api.Value);
            owners.Add(api.Key, pluginId);
        }
        return new ProviderApiSet(assemblies, owners);
    }

    private static void RejectOwnedApiConflicts(
        PluginAcceptedSnapshot snapshot,
        string shadowRoot,
        ProviderApiSet providerApis)
    {
        foreach (var relativePath in snapshot.Manifest.Dotnet!.ExportedApiAssemblies)
        {
            var identity = AssemblyName.GetAssemblyName(Resolve(shadowRoot, relativePath));
            if (identity.Name != null && providerApis.Assemblies.ContainsKey(identity.Name))
            {
                throw PluginServiceBindingException.ApiConflict(
                    identity.Name,
                    new[] { providerApis.Owners[identity.Name], snapshot.Manifest.Id }
                        .OrderBy(static id => id, StringComparer.Ordinal)
                        .ToArray());
            }
        }
    }

    private static IReadOnlyDictionary<string, Assembly> LoadExportedApis(
        PluginAcceptedSnapshot snapshot,
        string shadowRoot,
        PluginLoadContext loadContext)
    {
        var result = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in snapshot.Manifest.Dotnet!.ExportedApiAssemblies)
        {
            var assembly = loadContext.LoadFromAssemblyPath(Resolve(shadowRoot, relativePath));
            var name = assembly.GetName().Name!;
            if (!result.TryAdd(name, assembly))
                throw new InvalidOperationException($"Duplicate exported API assembly simple name '{name}'.");
        }
        return result;
    }

    private sealed record ProviderApiSet(
        IReadOnlyDictionary<string, Assembly> Assemblies,
        IReadOnlyDictionary<string, string> Owners)
    {
        public static ProviderApiSet Empty { get; } = new(
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class PluginActivationCommitRejectedException(PluginRuntimeBlocker blocker)
        : InvalidOperationException(blocker.Message)
    {
        public PluginRuntimeBlocker Blocker { get; } = blocker;
    }
}
