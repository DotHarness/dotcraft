using System.Text.Json.Nodes;
using DotCraft.Tools;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

internal sealed record RemoteHostToolExport(ToolRegistration Registration,
    string? PreparedBinding = null, long? SnapshotRevision = null);

internal sealed partial class RemoteToolHostMcpHandlers
{
    private async ValueTask<IReadOnlyList<RemoteHostToolExport>> ExportToolsAsync(
        HostWorkspaceRuntime runtime, string threadId, RemoteToolHostState state, CancellationToken ct)
    {
        var local = await runtime.LocalRegistrationsAsync(threadId, ct).ConfigureAwait(false);
        var exports = local.Select(static registration => new RemoteHostToolExport(registration))
            .Concat(runtime.PluginRegistrations(threadId)).ToArray();
        var snapshot = new EffectiveToolSnapshotBuilder().Build(exports.Select(static item => item.Registration), state.CatalogRevision);
        foreach (var diagnostic in snapshot.Diagnostics)
            _host.ReportDiagnostic(new(RemoteToolHostDiagnosticLevel.Warning, diagnostic.Code, diagnostic.Message));
        return exports.Where(item => snapshot.Registrations.ContainsKey(item.Registration.Definition.Name)).ToArray();
    }

    private static Tool ToMcpTool(RemoteHostToolExport export, RemoteToolHostState state)
    {
        var definition = export.Registration.Definition;
        var metadata = new JsonObject
        {
            ["profileVersion"] = RemoteToolHostProtocol.ProfileVersion,
            ["definitionId"] = definition.Id.ToString(),
            ["contractHash"] = RemoteToolContractHasher.Compute(definition),
            ["catalogRevision"] = state.CatalogRevision.ToString()
        };
        if (export.PreparedBinding is not null)
        {
            metadata["preparedBinding"] = export.PreparedBinding;
            metadata["snapshotRevision"] = export.SnapshotRevision;
        }
        return new Tool
        {
            Name = definition.Name.ToString(), Description = definition.Description,
            InputSchema = definition.InputSchema, OutputSchema = definition.OutputSchema,
            Meta = new JsonObject { ["dotcraft/remoteTool"] = metadata }
        };
    }
}
