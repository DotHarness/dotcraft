using System.Text.Json;

namespace DotCraft.CodexGateway.Workspace;

internal sealed partial class WorkspaceProjector
{
    private static bool IsCodeMode(JsonElement source)
    {
        var payload = source.Get("payload");
        return payload.Get("source").Text("kind") == "CoreNative"
            && payload.Get("source").Text("sourceId") == "code-mode"
            && payload.Get("source").Text("sourceToolId") is "CodeMode" or "CodeModeWait"
            && payload.Get("presentation").Text("presentationId") == "core.code-mode";
    }

    private static bool IsContextHelper(JsonElement source)
    {
        var payload = source.Get("payload");
        return source.Text("invocationOrigin") == "codeMode"
            && source.Text("invocationSourceId") is { Length: > 0 }
            && payload.Get("source").Text("kind") == "CoreNative"
            && (payload.Get("source").Text("sourceId") == "core-native"
                && payload.Get("source").Text("sourceToolId") is "ReadFile" or "GrepFiles" or "FindFiles" or "SkillView"
                || payload.Get("source").Text("sourceId") == "user-coordination"
                    && payload.Get("source").Text("sourceToolId") == "CurrentTime");
    }

    private static bool FailedOutput(JsonElement source)
    {
        var payload = source.Get("payload");
        return payload.Get("success").ValueKind == JsonValueKind.False
            || payload.Text("errorCode") is not null || payload.Text("errorMessage") is not null
            || IsCodeMode(source) && payload.Get("structuredContent").Text("status") == "failed";
    }

    private static bool HasIndependentResult(JsonElement payload) =>
        payload.Get("contentItems").Rows().Any(content => content.Text("type") is not (null or "text"))
        || payload.Get("structuredContent").Text("kind") is { Length: > 0 };

    private bool IsInternal(JsonElement source, string itemId)
    {
        var classification = source;
        if (_operations.TryGetValue(itemId, out var operation))
        {
            lock (operation.Gate)
                if (operation.Invocation.ValueKind == JsonValueKind.Object) classification = operation.Invocation;
        }
        return IsCodeMode(classification) || IsContextHelper(classification);
    }

    private bool OmitInternalSuccess(JsonElement source, string itemId) => source.Text("type") == "toolResult"
        && IsInternal(source, itemId) && !FailedOutput(source) && !HasIndependentResult(source.Get("payload"));

    private bool IsVisible(JsonElement source, Dictionary<string, object?> item)
    {
        if (source.Text("type") is not ("toolCall" or "toolResult") || !IsInternal(source, (string)item["id"]!)) return true;
        return source.Text("type") == "toolResult"
            && (FailedOutput(source) || item.GetValueOrDefault("status") is "failed"
                || HasIndependentResult(source.Get("payload")));
    }

    private void RememberVisibleSource(JsonElement source, Dictionary<string, object?> item)
    {
        if (!_operations.TryGetValue((string)item["id"]!, out var operation)) return;
        lock (operation.Gate) operation.VisibleSourceId ??= source.Required("id");
    }
}
