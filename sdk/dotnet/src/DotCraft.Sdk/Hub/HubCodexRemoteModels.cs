namespace DotCraft.Sdk.Hub;

public sealed record HubCodexRemoteStatus(
    string State,
    bool SignedIn,
    string HostName,
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed record HubCodexRemotePairing(
    string PairingUrl,
    string? ManualCode,
    DateTimeOffset ExpiresAt);
