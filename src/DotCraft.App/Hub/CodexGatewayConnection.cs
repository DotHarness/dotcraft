using DotCraft.CodexGateway;

namespace DotCraft.Hub;

internal interface ICodexGatewayConnection : IAsyncDisposable
{
    Task RunAsync(CancellationToken ct);
    Task<RemoteControlPairing> PairAsync(CancellationToken ct);
    Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken ct);
    Task<IReadOnlyList<RemoteControlClient>> ListClientsAsync(CancellationToken ct);
    Task RevokeClientAsync(string clientId, CancellationToken ct);
}

internal sealed class CodexGatewayConnection(RemoteControlHost host) : ICodexGatewayConnection
{
    public Task RunAsync(CancellationToken ct) => host.RunAsync(ct);
    public Task<RemoteControlPairing> PairAsync(CancellationToken ct) => host.PairAsync(ct);
    public Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken ct)
        => host.IsPairingClaimedAsync(pairing, ct);
    public Task<IReadOnlyList<RemoteControlClient>> ListClientsAsync(CancellationToken ct) => host.ListClientsAsync(ct);
    public Task RevokeClientAsync(string clientId, CancellationToken ct) => host.RevokeClientAsync(clientId, ct);
    public ValueTask DisposeAsync() => host.DisposeAsync();
}
