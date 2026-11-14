using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Sessions;

internal sealed class ThreadObservationItems
{
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StringBuilder> _text = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TerminalOutputBuffer> _commandOutput = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StringBuilder> _arguments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolExecutionProgressPayload> _progress = new(StringComparer.Ordinal);

    public List<SessionItem> Items { get; } = [];

    public void Clear()
    {
        Items.Clear();
        _indices.Clear();
        _text.Clear();
        _commandOutput.Clear();
        _arguments.Clear();
        _progress.Clear();
    }

    public void Seed(SessionItem source)
    {
        _indices[source.Id] = Items.Count;
        Items.Add(new SessionItem
        {
            Id = source.Id,
            TurnId = source.TurnId,
            Type = source.Type,
            Status = source.Status,
            Payload = source.Payload,
            CreatedAt = source.CreatedAt,
            CompletedAt = source.CompletedAt,
            InvocationOrigin = source.InvocationOrigin,
            InvocationSourceId = source.InvocationSourceId,
            FreeformCall = source.FreeformCall
        });
    }

    public void Upsert(SessionItem source)
    {
        var item = Clone(source);
        SessionItem? previous = null;
        if (_indices.TryGetValue(item.Id, out var index))
        {
            previous = Items[index];
            if (previous.Status == ItemStatus.Completed && item.Status != ItemStatus.Completed)
                return;
            Items[index] = item;
        }
        else
        {
            _indices[item.Id] = Items.Count;
            Items.Add(item);
        }

        var text = Text(item.Payload);
        if (text is { Length: 0 } && Text(previous?.Payload) is { Length: > 0 } previousText)
            item.Payload = WithText(item.Payload, previousText);
        if (item.Status == ItemStatus.Completed)
        {
            if (text is { Length: 0 } && _text.TryGetValue(item.Id, out var accumulated))
                item.Payload = WithText(item.Payload, accumulated.ToString());
            if (item.Payload is CommandExecutionPayload command && command.AggregatedOutput.Length == 0)
            {
                if (_commandOutput.TryGetValue(item.Id, out var output))
                    item.Payload = CaptureCommand(command, output);
                else if (previous?.Payload is CommandExecutionPayload previousCommand)
                    item.Payload = command with
                    {
                        AggregatedOutput = previousCommand.AggregatedOutput,
                        OriginalOutputChars = previousCommand.OriginalOutputChars,
                        Truncated = previousCommand.Truncated
                    };
            }
            _text.Remove(item.Id);
            _commandOutput.Remove(item.Id);
            _arguments.Remove(item.Id);
            _progress.Remove(item.Id);
        }
        else if (text is { Length: > 0 })
        {
            _text[item.Id] = new StringBuilder(text);
        }
    }

    public void ApplyDelta(string itemId, object? payload)
    {
        if (!_indices.TryGetValue(itemId, out var index))
            return;
        var item = Items[index];
        if (item.Status == ItemStatus.Completed)
            return;

        if (payload is ToolCallArgumentsDelta arguments)
        {
            Append(_arguments, itemId, arguments.Delta);
            return;
        }
        if (payload is ToolExecutionProgressPayload progress)
        {
            _progress[itemId] = Clone(progress);
            return;
        }
        if (payload is CommandExecutionOutputDelta commandDelta && item.Payload is CommandExecutionPayload command)
        {
            if (!_commandOutput.TryGetValue(itemId, out var output))
            {
                _commandOutput[itemId] = output = new TerminalOutputBuffer();
                output.Append(command.AggregatedOutput);
            }
            output.Append(commandDelta.TextDelta);
            item.Status = ItemStatus.Streaming;
            return;
        }
        var delta = payload switch
        {
            AgentMessageDelta agent => agent.TextDelta,
            ReasoningContentDelta reasoning => reasoning.TextDelta,
            _ => null
        };
        if (delta is null)
            return;
        if (!_text.ContainsKey(itemId))
            _text[itemId] = new StringBuilder(Text(item.Payload));
        Append(_text, itemId, delta);
        item.Status = ItemStatus.Streaming;
    }

    public List<SessionItem> Capture(
        ThreadObservationView view,
        IReadOnlyList<ToolName> summaryTools,
        out bool hasMore,
        ThreadObservationItemSelection? selection = null)
    {
        hasMore = false;
        if (view == ThreadObservationView.Metadata)
            return [];
        var selected = SelectItems(view, summaryTools, selection);
        if (selection?.Limit is not { } limit)
            return selected.Select(CaptureItem).ToList();

        var page = selected.Take(limit + 1).ToList();
        hasMore = page.Count > limit;
        if (hasMore)
            page.RemoveAt(limit);
        return page.Select(CaptureItem).ToList();
    }

    private IEnumerable<SessionItem> SelectItems(ThreadObservationView view, IReadOnlyList<ToolName> summaryTools,
        ThreadObservationItemSelection? selection)
    {
        var selected = view == ThreadObservationView.Full ? Items : SelectSummary(summaryTools);
        if (selection is not null)
        {
            if (!selection.IncludeToolExecutions)
                selected = selected.Where(item => item.Type != ItemType.ToolExecution);
            if (selection.ItemIds is { } ids)
                selected = selected.Where(item => ids.Contains(item.Id));
            if (selection.CallIds is { } calls)
                selected = selected.Where(item => ThreadSummaryProjection.From(item).CallId is { } call && calls.Contains(call));
            if (selection.Direction == ThreadHistorySortDirection.Descending)
                selected = selected.Reverse();
            if (selection.AfterItemId is { } after)
                selected = selected.SkipWhile(item => item.Id != after).Skip(1);
        }
        return selected;
    }

    public IReadOnlyList<SessionItem> CaptureActive() => Items
        .Where(item => item.Status is ItemStatus.Started or ItemStatus.Streaming)
        .Select(CaptureItem).ToArray();

    public IReadOnlyDictionary<string, string> CaptureArguments(HashSet<string> selected) =>
        _arguments.Where(entry => selected.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value.ToString(), StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ToolExecutionProgressPayload> CaptureProgress(HashSet<string> selected) =>
        _progress.Where(entry => selected.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => Clone(entry.Value), StringComparer.Ordinal);

    private SessionItem CaptureItem(SessionItem source)
    {
        var item = Clone(source);
        if (_text.TryGetValue(item.Id, out var text))
            item.Payload = WithText(item.Payload, text.ToString());
        if (item.Payload is CommandExecutionPayload command && _commandOutput.TryGetValue(item.Id, out var output))
            item.Payload = CaptureCommand(command, output);
        return item;
    }

    private IEnumerable<SessionItem> SelectSummary(IReadOnlyList<ToolName> summaryTools)
    {
        var keptCalls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Items)
        {
            switch (item.Payload)
            {
                case ToolCallPayload call when Named(summaryTools, call.Namespace, call.ToolName):
                    keptCalls.Add(call.CallId);
                    break;
                case DynamicToolCallPayload call when Named(summaryTools, call.Namespace, call.ToolName):
                    keptCalls.Add(call.CallId);
                    break;
                case ToolResultPayload result when IsFileChange(result.StructuredContent):
                    keptCalls.Add(result.CallId);
                    break;
            }
        }

        return Items.Where(item => item.Type switch
        {
            ItemType.UserMessage or ItemType.AgentMessage or ItemType.Error
                or ItemType.SystemNotice or ItemType.WebSearch => true,
            ItemType.ImageGeneration => item.Payload is ImageGenerationPayload { Status: not "inProgress" },
            ItemType.McpToolCall => item.Status == ItemStatus.Completed
                && item.Payload is McpToolCallPayload { McpAppResourceUri: { } uri }
                && uri.StartsWith("ui://", StringComparison.Ordinal),
            ItemType.DynamicToolCall => item.Payload is DynamicToolCallPayload call
                && Named(summaryTools, call.Namespace, call.ToolName),
            ItemType.ToolCall => item.Payload is ToolCallPayload call && keptCalls.Contains(call.CallId),
            ItemType.ToolResult => item.Payload is ToolResultPayload result && keptCalls.Contains(result.CallId),
            _ => false
        });
    }

    private static bool Named(IReadOnlyList<ToolName> tools, string? ns, string name) =>
        tools.Any(tool => tool.Name == name && (tool.Namespace ?? "") == (ns ?? ""));

    private static bool IsFileChange(JsonNode? content) =>
        content is JsonObject obj && obj["kind"] is JsonValue kind
        && kind.TryGetValue<string>(out var value) && value == "fileChange";

    private static object? WithText(object? payload, string text) => payload switch
    {
        AgentMessagePayload agent => agent with { Text = text },
        ReasoningContentPayload reasoning => reasoning with { Text = text },
        _ => payload
    };

    private static string? Text(object? payload) => payload switch
    {
        AgentMessagePayload agent => agent.Text,
        ReasoningContentPayload reasoning => reasoning.Text,
        _ => null
    };

    private static CommandExecutionPayload CaptureCommand(CommandExecutionPayload command, TerminalOutputBuffer buffer)
    {
        var (output, originalChars, truncated) = buffer.Snapshot(0);
        var omittedBefore = Math.Max(0L, (long)(command.OriginalOutputChars ?? 0) - command.AggregatedOutput.Length);
        return command with
        {
            AggregatedOutput = output,
            OriginalOutputChars = (int)Math.Min(int.MaxValue, originalChars + omittedBefore),
            Truncated = truncated || command.Truncated == true
        };
    }

    private static void Append(Dictionary<string, StringBuilder> buffers, string id, string delta)
    {
        if (!buffers.TryGetValue(id, out var buffer))
            buffers[id] = buffer = new StringBuilder();
        buffer.Append(delta);
    }

    internal static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, SessionJsonOptions.Default),
            SessionJsonOptions.Default)!;

    internal static SessionTurn CopyTurnMetadata(SessionTurn source) => Clone(new SessionTurn
    {
        Id = source.Id,
        ThreadId = source.ThreadId,
        Status = source.Status,
        StartedAt = source.StartedAt,
        CompletedAt = source.CompletedAt,
        TokenUsage = source.TokenUsage,
        Error = source.Error,
        ProviderError = source.ProviderError,
        HttpStatus = source.HttpStatus,
        OriginChannel = source.OriginChannel,
        Initiator = source.Initiator
    });
}
