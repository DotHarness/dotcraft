using DotCraft.Agents;
using System.Text.Json;

namespace DotCraft.Tracing;

internal static class AuxiliaryRequestDiagnostics
{
    internal static void Record(TraceStore store, ModelRuntimeDiagnostic diagnostic, string sessionKey)
    {
        string[] fields =
        [
            "providerProtocol", "protocolVersion", "requestKind", "turnId", "modelId", "status",
            "statusCode", "inputItemCount", "messageCount", "responseId", "inputTokens", "outputTokens",
            "durationMs", "failureReason"
        ];
        var metadata = fields.Where(diagnostic.Properties.ContainsKey)
            .ToDictionary(key => key, key => diagnostic.Properties[key]);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ProviderResponseDiagnostic,
            SessionKey = sessionKey,
            Content = diagnostic.Name,
            MetadataJson = JsonSerializer.Serialize(metadata)
        });
    }
}
