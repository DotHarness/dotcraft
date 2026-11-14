using System.Text.Json;

namespace DotCraft.CodexGateway;

public sealed record RemoteControlCredentials(string AccessToken, string AccountId);

public sealed record RemoteControlHostOptions
{
    public required string StateDirectory { get; init; }
    public string Name { get; init; } = Environment.MachineName + " · DotCraft";
    public string AppServerVersion { get; init; } = "0.166.0";
    public Uri BackendBaseUri { get; init; } = new("https://chatgpt.com/backend-api/");
}

public sealed record RemoteControlHostStatus(
    string InstallationId, string ServerName, string? EnvironmentId, string Status);

public sealed record RemoteControlPairing(
    string PairingCode, string? ManualPairingCode, string EnvironmentId, DateTimeOffset ExpiresAt)
{
    public Uri PairingUri => new("https://chatgpt.com/codex/pair?pairing_code=" + Uri.EscapeDataString(PairingCode));
}

public sealed record RemoteControlPeer(string ClientId, string StreamId);

public interface IRemoteControlRpcSession : IAsyncDisposable
{
    Task ReceiveAsync(JsonElement message, CancellationToken cancellationToken);
}

public delegate IRemoteControlRpcSession RemoteControlSessionFactory(
    RemoteControlPeer peer, Func<JsonElement, CancellationToken, Task> send);
