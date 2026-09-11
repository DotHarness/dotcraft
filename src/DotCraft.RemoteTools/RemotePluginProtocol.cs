using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal static class RemotePluginProtocol
{
    internal const string Capability = "plugins-v1";
    internal const string Prepare = "dotcraft/remoteToolHost/plugins/prepare";
    internal const string Activate = "dotcraft/remoteToolHost/plugins/activate";
    internal const string Abort = "dotcraft/remoteToolHost/plugins/abort";
    internal const string ReleaseThread = "dotcraft/remoteToolHost/plugins/releaseThread";
}

internal sealed record PluginBundleTransfer(RemotePluginBundle Bundle, TransferFileManifest Manifest);
internal sealed record PluginPrepareRequest(string LeaseId, string WorkspaceId, string ThreadId,
    long SnapshotRevision, string Mode, IReadOnlyList<PluginBundleTransfer> Bundles,
    IReadOnlyList<RemoteToolContractSummary> Tools);
internal sealed record PluginUpload(string PluginId, string? TransferId);
internal sealed record PluginPrepareResponse(string PreparationId, IReadOnlyList<PluginUpload> Uploads);
internal sealed record PluginActivateRequest(string PreparationId);
internal sealed record PluginPreparedBinding(string DefinitionId, string ContractHash, string BindingId);
internal sealed record PluginActivateResponse(IReadOnlyList<PluginPreparedBinding> Bindings);
internal sealed record PluginThreadRelease(string LeaseId, string WorkspaceId, string ThreadId);
