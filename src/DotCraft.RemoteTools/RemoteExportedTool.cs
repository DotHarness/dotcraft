using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

/// <summary>A captured Host tool definition, callable only through the session and thread that listed it.</summary>
public sealed class RemoteExportedTool
{
    internal RemoteExportedTool(RemoteExecutionSession session, string threadId, Tool tool,
        string definitionId, string contractHash, string? preparedBinding, long? snapshotRevision)
    {
        Session = session;
        ThreadId = threadId;
        Name = tool.Name;
        Description = tool.Description;
        InputSchema = tool.InputSchema.Clone();
        OutputSchema = tool.OutputSchema?.Clone();
        DefinitionId = definitionId;
        ContractHash = contractHash;
        PreparedBinding = preparedBinding;
        SnapshotRevision = snapshotRevision;
    }

    public string Name { get; }
    public string? Description { get; }
    public JsonElement InputSchema { get; }
    public JsonElement? OutputSchema { get; }
    public string DefinitionId { get; }
    public string ContractHash { get; }
    internal RemoteExecutionSession Session { get; }
    internal string ThreadId { get; }
    internal string? PreparedBinding { get; }
    internal long? SnapshotRevision { get; }
}
