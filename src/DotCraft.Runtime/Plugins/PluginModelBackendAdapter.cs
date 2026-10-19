using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.Extensions.AI;
using System.Collections.Concurrent;

namespace DotCraft.Runtime;

internal sealed class PluginModelBackendSourceAdapter : IModelBackendSource
{
    public PluginModelBackendSourceAdapter(IModelBackendSource source, PluginInvocation invocation)
    {
        Backend = invocation.Invoke(() => new BackendAdapter(source.Backend, invocation));
    }

    public IModelBackend Backend { get; }

    private sealed class BackendAdapter : IModelBackend
    {
        private readonly PluginTarget<IModelBackend> _target;
        private readonly PluginInvocation _invocation;

        public BackendAdapter(IModelBackend target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target);
            _invocation = invocation;
            var descriptor = target.Descriptor;
            if (descriptor.Protocols.Count == 0 || !descriptor.Protocols.Contains(descriptor.DefaultProtocol)
                || descriptor.Protocols.Any(string.IsNullOrWhiteSpace)
                || descriptor.AuthenticationMethods.Any(method => string.IsNullOrWhiteSpace(method.Id)))
                throw new InvalidOperationException("The backend descriptor is invalid.");
            Descriptor = descriptor with
            {
                Protocols = descriptor.Protocols.ToArray(),
                AuthenticationMethods = descriptor.AuthenticationMethods.ToArray()
            };
            PluginObjectGraphGuard.EnsureHostOwnedGraph(Descriptor, "backend descriptor");
        }

        public ModelBackendDescriptor Descriptor { get; }
        public CancellationToken Stopping => _invocation.Stopping;

        public IModelProvider CreateProvider(ModelBackendContext context) =>
            _invocation.Invoke(() => new ProviderAdapter(_target.Value.CreateProvider(context), _invocation));
    }

    private sealed class ProviderAdapter : IModelProvider
    {
        private readonly PluginTarget<IModelProvider> _target;
        private readonly PluginInvocation _invocation;
        private readonly ConcurrentDictionary<Type, Lazy<object?>> _capabilities = new();

        public ProviderAdapter(IModelProvider target, PluginInvocation invocation)
        {
            _target = invocation.Capture(target, ownsTarget: true);
            _invocation = invocation;
            Protocols = invocation.Snapshot(() => target.Protocols);
        }

        public IReadOnlyCollection<string> Protocols { get; }

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime) =>
            _invocation.Invoke(() => new PluginChatClient(_target.Value.CreateChatClient(runtime), _invocation));

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceKey is not null) return null;
            if (serviceType == typeof(IModelProvider)) return this;
            if (!PluginProviderCapabilities.Supports(serviceType)) return null;
            return _capabilities.GetOrAdd(serviceType, type => new Lazy<object?>(() => _invocation.Invoke(() =>
            {
                var capability = _target.Value.GetService(type);
                return capability is null ? null : PluginProviderCapabilities.Adapt(capability, type, _invocation);
            }))).Value;
        }
    }
}
