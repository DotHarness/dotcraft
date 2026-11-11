namespace DotCraft.RemoteTools;

public sealed partial class RemoteToolHostRuntime
{
    /// <summary>Changes one pairing after closing its live execution resources.</summary>
    public async Task SetAuthorizationAsync(string peerId, string authorizationMode)
    {
        if (!RemoteToolAuthorization.IsValid(authorizationMode))
            throw new ArgumentException("Choose an authorization mode.", nameof(authorizationMode));
        await DisconnectAsync(peerId).ConfigureAwait(false);
        _storage.UpdateHostState(current =>
        {
            var state = current ?? throw new InvalidOperationException("Host is not configured.");
            if (!state.Peers.Any(peer => peer.PeerId == peerId))
                throw new ArgumentException("Pairing does not exist.", nameof(peerId));
            return state with
            {
                Peers = state.Peers.Select(peer => peer.PeerId == peerId
                    ? peer with { AuthorizationMode = authorizationMode, AuthorizationRevision = peer.AuthorizationRevision + 1 }
                    : peer).ToArray(),
                CatalogRevision = state.CatalogRevision + 1
            };
        });
        Refresh();
    }
}
