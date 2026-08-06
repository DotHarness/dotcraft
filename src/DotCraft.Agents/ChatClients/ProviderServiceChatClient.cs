using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>Attaches provider capabilities to an MEAI client without vendor type checks.</summary>
public sealed class ProviderServiceChatClient(
    IChatClient innerClient,
    IReadOnlyDictionary<Type, object> services) : DelegatingChatClient(innerClient)
{
    public override object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey == null && services.TryGetValue(serviceType, out var service))
            return service;
        return base.GetService(serviceType, serviceKey);
    }
}
