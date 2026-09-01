using System.Text.Json;
using DotCraft.Context;
using DotCraft.Sessions;

namespace DotCraft.Tools;

internal sealed class RemoteToolHostRuntimeContextContributor(IRemoteToolHostClient client)
    : IRuntimeContextContributor
{
    private const int MaximumValueChars = 512;

    public string? BuildRuntimeContext(SessionThread thread)
    {
        if (!client.TryGetConnectionSnapshot(thread.Id, out var snapshot))
            return null;

        return
$"""
## Remote Tool Host
Status: {snapshot.Status}
HostId: {Encode(snapshot.HostId)}
WorkspaceId: {Encode(snapshot.WorkspaceId)}
HostName: {Encode(snapshot.Environment.HostName)}
OperatingSystem: {Encode(snapshot.Environment.OperatingSystem)}
UserName: {Encode(snapshot.Environment.UserName)}
RemoteWorkingDirectory: {Encode(snapshot.Environment.WorkspacePath)}
""";
    }

    private static string Encode(string? value)
    {
        value ??= string.Empty;
        var length = Math.Min(value.Length, MaximumValueChars);
        if (length < value.Length && length > 0 && char.IsHighSurrogate(value[length - 1]))
            length--;
        var bounded = length == value.Length ? value : value[..length] + "...";
        return JsonSerializer.Serialize(bounded);
    }
}
