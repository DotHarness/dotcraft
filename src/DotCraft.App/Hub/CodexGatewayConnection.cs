using DotCraft.CodexGateway;

namespace DotCraft.Hub;

internal interface ICodexGatewayConnection : IAsyncDisposable
{
    Task RunAsync(CancellationToken ct);
    Task<RemoteControlPairing> PairAsync(CancellationToken ct);
    Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken ct);
}

internal sealed class CodexGatewayConnection(RemoteControlHost host) : ICodexGatewayConnection
{
    public Task RunAsync(CancellationToken ct) => host.RunAsync(ct);
    public Task<RemoteControlPairing> PairAsync(CancellationToken ct) => host.PairAsync(ct);
    public Task<bool> IsPairingClaimedAsync(RemoteControlPairing pairing, CancellationToken ct)
        => host.IsPairingClaimedAsync(pairing, ct);
    public ValueTask DisposeAsync() => host.DisposeAsync();
}
