using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

internal sealed class AppBindingAttachmentRegistry
{
    private readonly ConcurrentDictionary<string, ActiveAppBindingAttachment> _activeAttachments = new(StringComparer.Ordinal);

    public void Set(string bindingId, IAppServerTransport transport, AppServerConnection connection) =>
        _activeAttachments[bindingId] = new ActiveAppBindingAttachment(transport, connection);

    public void Remove(string bindingId) =>
        _activeAttachments.TryRemove(bindingId, out _);

    public bool TryGetLive(
        string bindingId,
        [NotNullWhen(true)] out ActiveAppBindingAttachment? attachment)
    {
        if (_activeAttachments.TryGetValue(bindingId, out attachment))
        {
            if (!attachment.Connection.IsClosed)
                return true;

            _activeAttachments.TryRemove(bindingId, out _);
        }

        attachment = null;
        return false;
    }
}

internal sealed record ActiveAppBindingAttachment(
    IAppServerTransport Transport,
    AppServerConnection Connection);
