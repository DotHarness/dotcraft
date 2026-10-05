using System.Text.Json.Serialization;

namespace DotCraft.Hub;

public sealed record HubMobileState(
    string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureMessage,
    int Port,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<HubMobileDevice> Devices,
    HubMobileRelay? Relay);

public sealed record HubMobileRelay(string Url, string State);

public sealed record HubMobileDevice(
    string DeviceId,
    string DisplayName,
    string Platform,
    string OsVersion,
    string AppVersion,
    DateTimeOffset PairedAt,
    DateTimeOffset? LastSeenAt,
    bool Connected);

public sealed record HubMobilePairing(string PairingId, string QrPayload, DateTimeOffset ExpiresAt);

public sealed class MobileRelayRequest
{
    public string? Url { get; set; }

    public string? Token { get; set; }
}

public sealed class MobilePairRequest
{
    public string? Code { get; set; }

    public string? DisplayName { get; set; }

    public string? Platform { get; set; }

    public string? OsVersion { get; set; }

    public string? AppVersion { get; set; }
}

public sealed record MobileComputer(string ComputerId, string Name, int Port, string Fingerprint, IReadOnlyList<string> Addresses);

public sealed record MobilePairResponse(string DeviceId, string Credential, MobileComputer Computer);

public sealed record MobileHello(
    string ComputerId,
    string Name,
    string Version,
    int Port,
    string Fingerprint,
    IReadOnlyList<string> Addresses,
    MobileHelloRelay? Relay);

public sealed record MobileHelloRelay(string Url);

public sealed record MobileProject(string ProjectId, string DisplayName, bool Running, DateTimeOffset? LastActiveAt);

public sealed record MobileProjectList(IReadOnlyList<MobileProject> Projects);
