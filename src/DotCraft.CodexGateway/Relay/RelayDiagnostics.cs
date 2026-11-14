using System.Diagnostics;

namespace DotCraft.CodexGateway.Relay;

internal static class RelayDiagnostics
{
    public static void Failure(RemoteControlPeer? peer, string stage, Exception error) =>
        Trace.TraceWarning("codex_remote_control.failure stage={0} client={1} stream={2} errorType={3}",
            stage, Identity(peer?.ClientId), Identity(peer?.StreamId), error.GetType().Name);

    private static string Identity(string? value) => value is null ? "-"
        : new string(value.Take(64).Select(character => char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' ? character : '_').ToArray());
}
