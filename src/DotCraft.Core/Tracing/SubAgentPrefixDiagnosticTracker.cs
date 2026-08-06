using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Tracing;

internal sealed class SubAgentPrefixDiagnosticTracker(TraceStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<string, RequestShapeSnapshot> _latestRequestShapes = new();
    private readonly ConcurrentDictionary<string, PrefixAnchor> _pendingAnchors = new();
    private readonly ConcurrentDictionary<string, byte> _completedSessions = new();

    public void BindChild(string sessionKey, string parentSessionKey, bool expectsSharedInputPrefix = false)
    {
        if (_completedSessions.ContainsKey(sessionKey) || _pendingAnchors.ContainsKey(sessionKey))
            return;

        if (store.GetLatestEvents([sessionKey], TraceEventType.SubAgentPrefixDiagnostic).ContainsKey(sessionKey)
            || store.GetLatestEvents([sessionKey], TraceEventType.PromptCacheRequestShape).ContainsKey(sessionKey))
        {
            _completedSessions.TryAdd(sessionKey, 0);
            return;
        }

        RequestShapeSnapshot? parentShape;
        if (_latestRequestShapes.TryGetValue(parentSessionKey, out var liveParentShape))
        {
            parentShape = liveParentShape;
        }
        else
        {
            var parentEvent = store.GetLatestEvents(
                [parentSessionKey],
                TraceEventType.PromptCacheRequestShape).GetValueOrDefault(parentSessionKey);
            parentShape = TryReadRequestShape(parentEvent);
        }

        _pendingAnchors.TryAdd(
            sessionKey,
            new PrefixAnchor(parentSessionKey, parentShape, expectsSharedInputPrefix));
    }

    public void RecordRequestShape(
        string sessionKey,
        PromptCacheRequestShapeSnapshot snapshot,
        int? requestIndex,
        int? attemptNumber,
        DateTimeOffset timestamp)
    {
        var tracedShape = new RequestShapeSnapshot(
            snapshot.Protocol,
            snapshot.Model,
            snapshot.PromptCacheKeyHash,
            snapshot.InstructionsHash,
            snapshot.ToolsHash,
            snapshot.ReasoningHash,
            snapshot.InputItemCount,
            snapshot.InputItemHashes,
            requestIndex,
            attemptNumber);
        _latestRequestShapes[sessionKey] = tracedShape;

        if (!_pendingAnchors.TryRemove(sessionKey, out var anchor))
            return;

        store.Record(CreateDiagnostic(
            sessionKey,
            anchor.ParentSessionKey,
            anchor.ParentShape,
            tracedShape,
            anchor.ExpectsSharedInputPrefix,
            timestamp));
        _completedSessions.TryAdd(sessionKey, 0);
    }

    private static TraceEvent CreateDiagnostic(
        string childSessionKey,
        string parentSessionKey,
        RequestShapeSnapshot? parent,
        RequestShapeSnapshot child,
        bool expectsSharedInputPrefix,
        DateTimeOffset timestamp)
    {
        if (parent == null)
        {
            return CreateEvent(childSessionKey, child, timestamp, "unavailable", new
            {
                schemaVersion = 3,
                status = "unavailable",
                parentSessionKey,
                parentRequestIndex = (int?)null,
                parentAttemptNumber = (int?)null,
                childRequestIndex = child.RequestIndex,
                childAttemptNumber = child.AttemptNumber,
                matchedInputItemCount = 0,
                parentInputItemCount = (int?)null,
                childInputItemCount = child.InputItemCount,
                divergenceIndex = (int?)null,
                expectedSharedPrefix = expectsSharedInputPrefix,
                changedFields = Array.Empty<string>(),
                parent = (object?)null,
                child = DescribeShape(child)
            });
        }

        var changedFields = new List<string>(capacity: 7);
        AddChangedField(changedFields, "protocol", parent.Protocol, child.Protocol);
        AddChangedField(changedFields, "model", parent.Model, child.Model);
        AddChangedField(changedFields, "cacheKey", parent.PromptCacheKeyHash, child.PromptCacheKeyHash);
        AddChangedField(changedFields, "instructions", parent.InstructionsHash, child.InstructionsHash);
        AddChangedField(changedFields, "tools", parent.ToolsHash, child.ToolsHash);
        AddChangedField(changedFields, "reasoning", parent.ReasoningHash, child.ReasoningHash);

        var matchedInputItemCount = 0;
        var comparableCount = Math.Min(parent.InputItemHashes.Count, child.InputItemHashes.Count);
        while (matchedInputItemCount < comparableCount
               && string.Equals(
                   parent.InputItemHashes[matchedInputItemCount],
                   child.InputItemHashes[matchedInputItemCount],
                   StringComparison.Ordinal))
        {
            matchedInputItemCount++;
        }

        var exactParentInputPrefix = matchedInputItemCount == parent.InputItemHashes.Count;
        var retainsInputPrefix = matchedInputItemCount > 0;
        var staticPrefixCompatible = changedFields.Count == 0;
        if (!retainsInputPrefix)
            changedFields.Add("inputPrefix");

        // A broken static prefix is always a defect. An empty input prefix is only reported as a
        // separate grade, because a child spawned without inherited turns has nothing to share.
        var status = staticPrefixCompatible
            ? retainsInputPrefix ? "compatible" : "staticShared"
            : "diverged";
        return CreateEvent(childSessionKey, child, timestamp, status, new
        {
            schemaVersion = 3,
            status,
            parentSessionKey,
            parentRequestIndex = parent.RequestIndex,
            parentAttemptNumber = parent.AttemptNumber,
            childRequestIndex = child.RequestIndex,
            childAttemptNumber = child.AttemptNumber,
            matchedInputItemCount,
            parentInputItemCount = parent.InputItemCount,
            childInputItemCount = child.InputItemCount,
            divergenceIndex = exactParentInputPrefix ? (int?)null : matchedInputItemCount,
            exactParentInputPrefix,
            expectedSharedPrefix = expectsSharedInputPrefix,
            cacheIdentityShared = !changedFields.Contains("cacheKey", StringComparer.Ordinal),
            staticPrefixCompatible,
            changedFields,
            parent = DescribeShape(parent),
            child = DescribeShape(child)
        });
    }

    private static TraceEvent CreateEvent(
        string sessionKey,
        RequestShapeSnapshot child,
        DateTimeOffset timestamp,
        string status,
        object metadata) => new()
    {
        Type = TraceEventType.SubAgentPrefixDiagnostic,
        SessionKey = sessionKey,
        Timestamp = timestamp,
        Content = $"Parent cache prefix {status}",
        ModelId = child.Model,
        RequestIndex = child.RequestIndex,
        MetadataJson = JsonSerializer.Serialize(metadata, JsonOptions)
    };

    private static object DescribeShape(RequestShapeSnapshot shape) => new
    {
        protocol = shape.Protocol,
        model = shape.Model,
        promptCacheKeyHash = shape.PromptCacheKeyHash,
        instructionsHash = shape.InstructionsHash,
        toolsHash = shape.ToolsHash,
        reasoningHash = shape.ReasoningHash
    };

    private static void AddChangedField(
        ICollection<string> changedFields,
        string field,
        string? parentValue,
        string? childValue)
    {
        if (!string.Equals(parentValue, childValue, StringComparison.Ordinal))
            changedFields.Add(field);
    }

    private static RequestShapeSnapshot? TryReadRequestShape(TraceEvent? evt)
    {
        if (evt?.MetadataJson is not { Length: > 0 } metadataJson)
            return null;

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var root = document.RootElement;
            var protocol = ReadString(root, "protocol");
            var model = ReadString(root, "model");
            var toolsHash = ReadString(root, "toolsHash");
            if (protocol == null || model == null || toolsHash == null)
                return null;

            if (!root.TryGetProperty("inputItemHashes", out var hashes)
                || hashes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var inputItemHashes = hashes.EnumerateArray()
                .Where(static item => item.ValueKind == JsonValueKind.String)
                .Select(static item => item.GetString()!)
                .ToArray();
            var inputItemCount = ReadInt32(root, "inputItemCount") ?? inputItemHashes.Length;
            if (inputItemCount != inputItemHashes.Length)
                return null;

            return new RequestShapeSnapshot(
                protocol,
                model,
                ReadString(root, "promptCacheKeyHash"),
                ReadString(root, "instructionsHash"),
                toolsHash,
                ReadString(root, "reasoningHash"),
                inputItemCount,
                inputItemHashes,
                evt.RequestIndex,
                ReadInt32(root, "attemptNumber"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private sealed record RequestShapeSnapshot(
        string Protocol,
        string Model,
        string? PromptCacheKeyHash,
        string? InstructionsHash,
        string ToolsHash,
        string? ReasoningHash,
        int InputItemCount,
        IReadOnlyList<string> InputItemHashes,
        int? RequestIndex,
        int? AttemptNumber);

    private sealed record PrefixAnchor(
        string ParentSessionKey,
        RequestShapeSnapshot? ParentShape,
        bool ExpectsSharedInputPrefix);
}
