using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public sealed partial class StreamingFunctionInvokingChatClient
{
    private IReadOnlyList<ToolCallArgumentsDeltaContent>? AddToolCallArgumentPreviews(
        ChatResponseUpdate update,
        Dictionary<int, ToolCallTracker> trackers)
    {
        if (!EnableToolCallArgumentPreviews)
            return null;

        List<ToolCallArgumentsDeltaContent>? addedContents = null;
        foreach (var delta in ExtractDeltas(update.RawRepresentation))
        {
            if (!trackers.TryGetValue(delta.Index, out var tracker))
            {
                tracker = new ToolCallTracker();
                trackers[delta.Index] = tracker;
            }

            tracker.CallId ??= delta.CallId;
            tracker.ToolName ??= delta.ToolName;

            if (string.IsNullOrEmpty(delta.ArgumentsDelta) || tracker.ToolName is null || !IsEligible(tracker.ToolName))
                continue;

            var isFirst = !tracker.FirstChunkEmitted;
            tracker.FirstChunkEmitted = true;
            var content = new ToolCallArgumentsDeltaContent
            {
                ToolCallIndex = delta.Index,
                ToolName = isFirst ? tracker.ToolName : null,
                CallId = isFirst ? tracker.CallId : null,
                ArgumentsDelta = delta.ArgumentsDelta
            };
            update.Contents.Add(content);
            (addedContents ??= []).Add(content);
        }

        return addedContents;
    }

    private static void RemoveToolCallArgumentPreviews(
        ChatResponseUpdate update,
        IReadOnlyList<ToolCallArgumentsDeltaContent>? addedContents)
    {
        if (addedContents is not { Count: > 0 })
            return;
        foreach (var content in addedContents)
            update.Contents.Remove(content);
    }

    private bool IsEligible(string toolName) =>
        IsStreamableTool?.Invoke(toolName)
        ?? StreamableToolNames?.Contains(toolName)
        ?? true;

    internal IEnumerable<ToolCallDeltaChunk> ExtractDeltas(object? rawRepresentation)
    {
        if (GetService(typeof(IToolCallArgumentsDeltaExtractor)) is IToolCallArgumentsDeltaExtractor extractor)
        {
            foreach (var chunk in extractor.Extract(rawRepresentation))
                yield return new ToolCallDeltaChunk(
                    chunk.ToolCallIndex,
                    chunk.ToolName,
                    chunk.CallId,
                    chunk.ArgumentsDelta);
        }

        if (rawRepresentation is IToolCallDeltaChunkSource source)
        {
            foreach (var chunk in source.GetToolCallDeltaChunks())
                yield return chunk;
        }
    }
}
