using System.Buffers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DotCraft.RemoteTools;

internal static class SatelliteWire
{
    public const string ControlPath = "/satellite/control";
    public const string DataPath = "/satellite/data";
    public const string InvitePathPrefix = "/i/";

    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Heartbeat = "heartbeat";
    public const string OpenSession = "openSession";
    public const string SessionFailed = "sessionFailed";
    public const string Revoked = "revoked";

    public const string OfflineClose = "satelliteOffline";
    public const string SessionFailedClose = "satelliteSessionFailed";

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan OpenSessionTimeout = TimeSpan.FromSeconds(15);

    private const int MaxFrameBytes = 256 * 1024;

    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120)
    ];

    /// <summary>Jitter keeps a Hub restart from being answered by every peer at the same instant.</summary>
    public static TimeSpan ReconnectDelay(int attempt)
    {
        var baseDelay = ReconnectDelays[Math.Clamp(attempt, 0, ReconnectDelays.Length - 1)];
        var jitter = (RandomNumberGenerator.GetInt32(0, 401) - 200) / 1000d;
        return baseDelay * (1 + jitter);
    }

    public static async Task SendAsync(WebSocket socket, SatelliteFrame frame, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, RemoteToolHostProtocol.JsonOptions);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<SatelliteFrame?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var payload = new ArrayBufferWriter<byte>();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (payload.WrittenCount + result.Count > MaxFrameBytes)
                    throw new InvalidOperationException("Satellite control frame exceeded the size limit.");
                payload.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                    continue;
                return JsonSerializer.Deserialize<SatelliteFrame>(
                    payload.WrittenSpan,
                    RemoteToolHostProtocol.JsonOptions);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static string? ReadBearer(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        return authorizationHeader is not null
               && authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && authorizationHeader.Length > prefix.Length
            ? authorizationHeader[prefix.Length..].Trim()
            : null;
    }

    public static string DescribeInvitePage(string joinCommand, DateTimeOffset expiresAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("DotCraft Remote Tool Host invitation");
        builder.AppendLine();
        builder.AppendLine("Run this on the machine that should share its workspace:");
        builder.AppendLine();
        builder.AppendLine("  " + joinCommand);
        builder.AppendLine();
        builder.AppendLine($"This invitation expires at {expiresAt.ToUniversalTime():u} and can be used once.");
        return builder.ToString();
    }
}

internal sealed record SatelliteWorkspaceInfo(
    string WorkspaceId,
    string Path,
    bool Busy,
    string? BusyOwner = null,
    DateTimeOffset? LeaseExpiresAt = null);

internal sealed record SatelliteFrame
{
    public required string Kind { get; init; }
    public string? PeerId { get; init; }
    public string? Credential { get; init; }
    public string? DisplayName { get; init; }
    public string? MachineName { get; init; }
    public string? OperatingSystem { get; init; }
    public string? UserName { get; init; }
    public string? BuildVersion { get; init; }
    public string? HubVersion { get; init; }
    public string? HubLabel { get; init; }
    public string? Purpose { get; init; }
    public string? SessionId { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<SatelliteWorkspaceInfo>? Workspaces { get; init; }
}
