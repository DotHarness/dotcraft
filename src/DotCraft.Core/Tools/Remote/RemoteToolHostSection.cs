using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;

namespace DotCraft.Tools;

internal sealed class RemoteToolHostSection(IRemoteToolHostClient client) : IWorldStateSection
{
    public const string SectionId = "remote_tool_host";

    private const int MaximumValueChars = 512;

    private const string RemovalNotice =
        "The remote tool host reported earlier is no longer routed to this thread.";

    public string Id => SectionId;

    public JsonNode? Snapshot(WorldStateContext context)
    {
        // Keep an object: JSON null would delete the section and lose the fact that a host was routed.
        var values = new JsonObject();
        if (client.TryGetConnectionSnapshot(context.Thread.Id, out var snapshot))
            values["host"] = WorldStateHash.Of(Render(snapshot));
        return values;
    }

    public string? RenderDiff(WorldStateContext context, PreviousSectionState previous)
    {
        if (client.TryGetConnectionSnapshot(context.Thread.Id, out var snapshot))
            return Render(snapshot);

        var previouslyRouted = previous.Kind switch
        {
            PreviousSectionKind.Known => previous.Value is JsonObject values && values.ContainsKey("host"),
            PreviousSectionKind.Unknown => true,
            _ => false
        };
        return previouslyRouted ? $"## Remote Tool Host\n{RemovalNotice}" : null;
    }

    private static string Render(RemoteToolConnectionSnapshot snapshot) =>
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

    private static string Encode(string value)
    {
        var length = Math.Min(value.Length, MaximumValueChars);
        if (length < value.Length && char.IsHighSurrogate(value[length - 1]))
            length--;
        var bounded = length == value.Length ? value : value[..length] + "...";
        return JsonSerializer.Serialize(bounded);
    }
}
