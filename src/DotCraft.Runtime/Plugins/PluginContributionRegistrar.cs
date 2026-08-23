using DotCraft.Contributions;

namespace DotCraft.Runtime;

/// <summary>Stages plugin registrations and keeps raw plugin ownership outside the kernel registry.</summary>
internal sealed class PluginContributionRegistrar : IContributionRegistrar
{
    private readonly object _gate = new();
    private readonly IContributionRegistry _registry;
    private readonly IContributionRegistrar _inner;
    private readonly PluginInvocation _invocation;
    private readonly List<Registration> _registrations = [];
    private object? _generationOwnedEntry;
    private int _preparing;
    private RegistrarState _state;

    public PluginContributionRegistrar(
        IContributionRegistry registry,
        ContributionOrigin origin,
        PluginCallGate callGate,
        object generationOwnedEntry)
    {
        _registry = registry;
        _inner = registry.CreateRegistrar(origin);
        _invocation = new PluginInvocation(origin.Name!, origin.Generation!, callGate);
        _generationOwnedEntry = generationOwnedEntry ?? throw new ArgumentNullException(nameof(generationOwnedEntry));
    }

    public ContributionOrigin Origin => _inner.Origin;

    public IContributionHandle Add<TContract>(
        TContract contribution,
        ContributionOptions? options = null)
        where TContract : class, IContributionContract
    {
        ArgumentNullException.ThrowIfNull(contribution);
        Registration<TContract> registration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state >= RegistrarState.Revoked, this);
            if (_state != RegistrarState.Staging)
            {
                throw new InvalidOperationException(
                    "Plugin contributions can be registered only during activation.");
            }
            var resolvedOptions = options ?? ContributionOptions.Default;
            registration = new Registration<TContract>(
                contribution,
                resolvedOptions,
                resolvedOptions.OwnsContribution && !ReferenceEquals(contribution, _generationOwnedEntry));
            _registrations.Add(registration);
            _preparing++;
        }

        try
        {
            registration.Prepare(_invocation);
        }
        finally
        {
            lock (_gate)
                _preparing--;
        }

        lock (_gate)
        {
            if (_state != RegistrarState.Staging)
            {
                throw new InvalidOperationException(
                    "Plugin activation ended before the contribution was prepared.");
            }
        }
        return registration;
    }

    /// <summary>Publishes every successfully prepared activation registration as one change batch.</summary>
    public void Commit()
    {
        Registration[] registrations;
        lock (_gate)
        {
            if (_state != RegistrarState.Staging)
                throw new InvalidOperationException("Plugin contributions can be committed only once.");
            if (_preparing != 0)
            {
                throw new InvalidOperationException(
                    "Plugin contribution registration must finish before activation returns.");
            }
            _state = RegistrarState.Sealed;
            _generationOwnedEntry = null;
            registrations = [.. _registrations];
        }

        try
        {
            using var batch = _registry.BeginBatch();
            foreach (var registration in registrations)
                registration.Publish(_inner);
        }
        catch
        {
            Revoke();
            throw;
        }
    }

    /// <summary>Removes every route without disposing a raw plugin target.</summary>
    public void Revoke()
    {
        Registration[] registrations;
        lock (_gate)
        {
            if (_state >= RegistrarState.Revoked)
                return;
            _state = RegistrarState.Revoked;
            _generationOwnedEntry = null;
            registrations = [.. _registrations];
        }

        using var batch = _registry.BeginBatch();
        for (var index = registrations.Length - 1; index >= 0; index--)
            registrations[index].Revoke();
        _inner.Dispose();
    }

    /// <summary>Disposes raw plugin targets after every admitted callback and tracked worker has stopped.</summary>
    public async Task<IReadOnlyList<string>> DisposeTargetsAsync()
    {
        Registration[] registrations;
        lock (_gate)
        {
            if (_state == RegistrarState.Disposed)
                return [];
            _state = RegistrarState.Disposed;
            _generationOwnedEntry = null;
            registrations = [.. _registrations];
            _registrations.Clear();
        }

        var errors = new List<string>(await _invocation.DisposeCapturedTargetsAsync().ConfigureAwait(false));
        for (var index = registrations.Length - 1; index >= 0; index--)
        {
            try
            {
                await registrations[index].DisposeTargetAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors.Add(PluginGeneration.CopyExceptionMessage(exception));
            }
        }
        return errors;
    }

    public void Dispose() => Revoke();

    private enum RegistrarState
    {
        Staging,
        Sealed,
        Revoked,
        Disposed
    }

    private abstract class Registration : IContributionHandle
    {
        private readonly object _gate = new();
        private IContributionHandle? _published;
        private bool _prepared;
        private bool _revoked;
        private bool _targetDisposed;

        public ContributionId Id
        {
            get
            {
                lock (_gate)
                    return _published?.Id ?? default;
            }
        }

        protected abstract bool OwnsTarget { get; }

        protected abstract IContributionHandle PublishCore(IContributionRegistrar registrar);

        protected abstract ValueTask DisposeTargetCoreAsync();

        protected abstract void PrepareCore(PluginInvocation invocation);

        public void Prepare(PluginInvocation invocation)
        {
            PrepareCore(invocation);
            lock (_gate)
                _prepared = true;
        }

        public void Publish(IContributionRegistrar registrar)
        {
            IContributionHandle handle;
            lock (_gate)
            {
                if (_revoked || !_prepared || _published != null)
                    return;
            }

            handle = PublishCore(registrar);
            lock (_gate)
            {
                if (_revoked)
                {
                    handle.Dispose();
                    return;
                }
                _published = handle;
            }
        }

        public void Revoke()
        {
            IContributionHandle? handle;
            lock (_gate)
            {
                if (_revoked)
                    return;
                _revoked = true;
                handle = _published;
                _published = null;
            }
            handle?.Dispose();
        }

        public async ValueTask DisposeTargetAsync()
        {
            lock (_gate)
            {
                if (_targetDisposed || !OwnsTarget)
                    return;
                _targetDisposed = true;
            }
            await DisposeTargetCoreAsync().ConfigureAwait(false);
        }

        void IDisposable.Dispose() => Revoke();
    }

    private sealed class Registration<TContract>(
        TContract target,
        ContributionOptions options,
        bool ownsTarget) : Registration
        where TContract : class, IContributionContract
    {
        private TContract? _adapted;

        protected override bool OwnsTarget => ownsTarget;

        protected override void PrepareCore(PluginInvocation invocation) =>
            _adapted = PluginContributionAdapters.Adapt(target, invocation);

        protected override IContributionHandle PublishCore(IContributionRegistrar registrar) =>
            registrar.Add(_adapted!, options with { OwnsContribution = false });

        protected override async ValueTask DisposeTargetCoreAsync()
        {
            if (target is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (target is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
