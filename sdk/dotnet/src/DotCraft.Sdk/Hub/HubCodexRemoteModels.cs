namespace DotCraft.Sdk.Hub;

public sealed record HubCodexRemoteStatus(
    string State,
    bool SignedIn,
    string HostName,
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed record HubCodexRemoteClient(
    string ClientId,
    string? DisplayName,
    string? DeviceType,
    string? Platform,
    string? OsVersion,
    string? DeviceModel,
    string? AppVersion,
    DateTimeOffset? LastSeenAt);

public sealed record HubCodexRemoteClients(IReadOnlyList<HubCodexRemoteClient> Clients);

public sealed record HubCodexRemotePairing(
    string PairingUrl,
    string? ManualCode,
    DateTimeOffset ExpiresAt);
