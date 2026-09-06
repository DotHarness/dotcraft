using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed class RemoteToolHostClientFactory(IRemoteToolHostDirectory directory, AppConfig config) :
    IRemoteToolHostClientFactory,
    IAsyncDisposable
{
    private readonly object _gate = new();
    private RemoteToolHostClient? _client;

    public IRemoteToolHostClient Create(IApprovalService approvalService)
    {
        lock (_gate)
            return _client ??= new RemoteToolHostClient(directory, approvalService, config);
    }

    public async ValueTask DisposeAsync()
    {
        RemoteToolHostClient? client;
        lock (_gate)
        {
            client = _client;
            _client = null;
        }
        if (client is not null)
            await client.DisposeAsync().ConfigureAwait(false);
    }
}
