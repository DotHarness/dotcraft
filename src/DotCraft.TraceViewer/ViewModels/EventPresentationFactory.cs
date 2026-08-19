using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Tracing;

namespace DotCraft.TraceViewer.ViewModels;

internal static class EventPresentationFactory
{
    private static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static EventRowItem Create(TraceEvent traceEvent, TraceEvent? toolStart = null)
    {
        TraceEvent[] sourceEvents = toolStart is null ? [traceEvent] : [toolStart, traceEvent];
        var metadata = TraceMetadata.Parse(traceEvent.MetadataJson);
        var presentation = Describe(traceEvent, toolStart, metadata);
        return new EventRowItem
        {
            Id = toolStart is null ? traceEvent.Id : $"tool:{traceEvent.CallId ?? traceEvent.Id}",
            SourceEvents = sourceEvents,
            Title = presentation.Title,
            Summary = presentation.Summary,
            Time = traceEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.CurrentCulture),
            Badge = presentation.Badge,
            Lane = presentation.Lane,
            IsDiagnostic = presentation.IsDiagnostic,
            IsError = presentation.IsError,
            Detail = CreateDetail(presentation, sourceEvents, metadata),
        };
    }

    private static EventDescription Describe(TraceEvent e, TraceEvent? start, TraceMetadata metadata) => e.Type switch
    {
        TraceEventType.Request => new("User request", Preview(SessionTitleFormatter.Split(e.Content).UserContent), "Input", TimelineLane.Input, false, false, "User content"),
        TraceEventType.Thinking => new("Model thinking", Preview(e.Content), "Model", TimelineLane.Model, false, false, "Reasoning"),
        TraceEventType.Response => new("Model response", Preview(e.Content), "Model", TimelineLane.Model, false, false, "Response"),
        TraceEventType.ResponseTerminal => new("Response completed", e.FinishReason ?? "Terminal response marker", "Model", TimelineLane.Model, true, false, null),
        TraceEventType.TokenUsage => new("Token usage", FormatTokens(e), "Usage", TimelineLane.Model, false, false, null),
        TraceEventType.ToolCallStarted => new($"{e.ToolName ?? "Tool"} started", Preview(e.ToolArguments), "Tool", TimelineLane.Tools, false, false, "Arguments"),
        TraceEventType.ToolCallCompleted => new(e.ToolName ?? start?.ToolName ?? "Tool call", Preview(start?.ToolArguments ?? e.ToolArguments), "Tool", TimelineLane.Tools, false, false, null),
        TraceEventType.Error => new("Runtime error", Preview(e.Content), "Error", TimelineLane.Tools, false, true, "Error"),
        TraceEventType.ProviderError => new("Provider error", Preview(e.Content), "Error", TimelineLane.Model, false, true, "Error"),
        TraceEventType.TurnCompleted => new("Turn completed", Duration(e.DurationMs), "Turn", TimelineLane.Input, false, false, null),
        TraceEventType.SessionMetadata => new("Session context", Changed(e), "Context", TimelineLane.Input, true, false, null),
        TraceEventType.ToolInjection => new("Tools changed", Join(e.ChangedToolNames ?? e.ToolNames), "Tool", TimelineLane.Tools, false, false, null),
        TraceEventType.DeferredToolLoading => new("Deferred tools activated", metadata.Get("trigger", "strategy", "effectiveMode") ?? Preview(e.Content), "Tool", TimelineLane.Tools, false, false, null),
        TraceEventType.SkillReferenced => new("Skill referenced", e.ToolName ?? Preview(e.Content), "Skill", TimelineLane.Tools, false, false, null),
        TraceEventType.ContextCompaction => new("Context compacted", Preview(e.Content), "Maintenance", TimelineLane.Model, false, false, "Summary"),
        TraceEventType.MaintenanceForkRequest => new("Maintenance request", e.ToolName ?? metadata.Get("taskKind") ?? Preview(e.Content), "Maintenance", TimelineLane.Model, false, false, "Prompt"),
        TraceEventType.MaintenanceForkResponse => new("Maintenance response", Preview(e.Content), "Maintenance", TimelineLane.Model, false, false, "Response"),
        TraceEventType.ThreadRollback => new("Thread rollback", Preview(e.Content), "Maintenance", TimelineLane.Input, false, false, "Summary"),
        TraceEventType.ProviderResponseDiagnostic => new("Provider response", metadata.Get("outcome", "eventType", "status") ?? "Provider diagnostic", "Diagnostic", TimelineLane.Model, true, false, null),
        TraceEventType.PromptCachePoint => new("Prompt cache point", metadata.Get("model", "role") ?? e.ModelId ?? "Cache point recorded", "Diagnostic", TimelineLane.Model, true, false, null),
        TraceEventType.PromptCacheDiagnostic => new("Prompt cache", metadata.Get("classification") ?? Changed(e), "Diagnostic", TimelineLane.Model, true, false, null),
        TraceEventType.PromptCacheRequestShape => new("Prompt request shape", metadata.Get("protocol", "model") ?? e.ModelId ?? "Request shape", "Diagnostic", TimelineLane.Model, true, false, null),
        TraceEventType.SubAgentPrefixDiagnostic => new("Sub-agent prefix", metadata.Get("status") ?? "Prefix comparison", "Diagnostic", TimelineLane.Tools, true, false, null),
        _ => new(SplitName(e.Type.ToString()), Preview(e.Content), "System", TimelineLane.Input, false, false, "Content"),
    };

    private static EventDetailItem CreateDetail(
        EventDescription description,
        TraceEvent[] events,
        TraceMetadata metadata)
    {
        var e = events[^1];
        var start = events.Length > 1 ? events[0] : null;
        var sections = new List<DetailSectionItem>
        {
            Section("Overview", OverviewFields(e, start), []),
        };

        var content = new List<DetailBlockItem>();
        if (e.Type == TraceEventType.Request)
        {
            var split = SessionTitleFormatter.Split(e.Content);
            AddBlock(content, "User content", split.UserContent);
        }
        else if (e.Type == TraceEventType.ToolCallCompleted)
        {
            AddBlock(content, "Arguments", start?.ToolArguments ?? e.ToolArguments, isCode: true);
            AddBlock(content, "Result", e.ToolResult, isCode: true);
        }
        else
        {
            AddBlock(content, description.ContentLabel ?? "Content", e.ToolArguments ?? e.ToolResult ?? e.Content);
        }

        if (content.Count > 0)
            sections.Add(Section("Content", [], content));

        var contextFields = new List<DetailFieldItem>();
        Add(contextFields, "Tools", Join(e.ToolNames));
        Add(contextFields, "Changed tools", Join(e.ChangedToolNames));
        Add(contextFields, "Prompt cache event", e.PromptCacheEventKind);
        Add(contextFields, "Changed fields", Join(e.PromptCacheChangedFields));
        Add(contextFields, "System prompt hash", e.CurrentSystemPromptHash ?? e.SystemPromptHash);
        Add(contextFields, "Tool schema hash", e.CurrentToolSchemaHash ?? e.ToolSchemaHash);
        var contextBlocks = new List<DetailBlockItem>();
        if (e.Type == TraceEventType.Request)
            AddBlock(contextBlocks, "Injected runtime context", SessionTitleFormatter.Split(e.Content).RuntimeContext);
        AddBlock(contextBlocks, "System prompt", e.FinalSystemPrompt);
        if (contextFields.Count > 0 || contextBlocks.Count > 0)
            sections.Add(Section("Context", contextFields, contextBlocks));

        var diagnosticFields = DiagnosticFields(e, metadata);
        if (diagnosticFields.Count > 0 || metadata.FormattedJson.Length > 0)
        {
            var blocks = new List<DetailBlockItem>();
            AddBlock(blocks, "Metadata", metadata.FormattedJson, isCode: true);
            sections.Add(Section("Diagnostics", diagnosticFields, blocks));
        }

        sections.Add(Section("Raw", [],
        [
            new DetailBlockItem(events.Length == 1 ? "Trace event" : "Source events", JsonSerializer.Serialize(events, RawJsonOptions), IsCode: true),
        ]));

        return new EventDetailItem
        {
            Title = description.Title,
            Subtitle = $"{e.Type} · {e.Timestamp.ToLocalTime():G}",
            Sections = sections,
        };
    }

    private static List<DetailFieldItem> OverviewFields(TraceEvent e, TraceEvent? start)
    {
        var fields = new List<DetailFieldItem>();
        Add(fields, "Type", e.Type.ToString());
        Add(fields, "Timestamp", e.Timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture));
        Add(fields, "Duration", e.DurationMs is { } duration ? Duration(duration) : null);
        Add(fields, "Model", e.ModelId);
        Add(fields, "Tool", e.ToolName ?? start?.ToolName);
        Add(fields, "Finish reason", e.FinishReason);
        Add(fields, "Reasoning effort", e.ReasoningEffort);
        Add(fields, "Request", e.RequestIndex?.ToString(CultureInfo.CurrentCulture));
        Add(fields, "LLM call", e.LlmCallIndex?.ToString(CultureInfo.CurrentCulture));
        Add(fields, "Call ID", e.CallId ?? start?.CallId);
        Add(fields, "Response ID", e.ResponseId);
        Add(fields, "Message ID", e.MessageId);
        Add(fields, "Event ID", e.Id);
        if (e.Type == TraceEventType.TokenUsage)
        {
            Add(fields, "Input tokens", Number(e.InputTokens));
            Add(fields, "Fresh input", Number(e.FreshInputTokens));
            Add(fields, "Cached input", Number(e.CachedInputTokens));
            Add(fields, "Cache write", Number(e.CacheWriteInputTokens));
            Add(fields, "Non-cached input", Number(e.NonCachedInputTokens));
            Add(fields, "Output tokens", Number(e.OutputTokens));
            Add(fields, "Reasoning output", Number(e.ReasoningOutputTokens));
            Add(fields, "Total tokens", Number(e.TotalTokens));
        }

        return fields;
    }

    private static List<DetailFieldItem> DiagnosticFields(TraceEvent e, TraceMetadata metadata)
    {
        var fields = new List<DetailFieldItem>();
        Add(fields, "Cache break", e.PromptCacheBreakDetected?.ToString());
        Add(fields, "Prompt drift", e.PromptDriftDetected?.ToString());
        fields.AddRange(e.Type switch
        {
            TraceEventType.ProviderResponseDiagnostic => metadata.Promote(
                ("Event", ["eventType"]), ("Outcome", ["outcome"]), ("Attempt", ["attempt"]),
                ("Retry", ["retry", "isRetry"]), ("Status", ["status", "statusCode"]), ("Request ID", ["requestId"])),
            TraceEventType.ProviderError => metadata.Promote(("Error code", ["errorCode"]), ("Content type", ["contentType"])),
            TraceEventType.DeferredToolLoading => metadata.Promote(
                ("Strategy", ["strategy"]), ("Mode", ["effectiveMode", "mode"]), ("Protocol", ["protocol"]),
                ("Trigger", ["trigger"]), ("Query", ["query"]), ("Wire shape", ["wireShape"]), ("Tool count", ["toolCount"])),
            TraceEventType.PromptCacheDiagnostic => metadata.Promote(
                ("Classification", ["classification"]), ("Cache read drop", ["cacheReadDrop"]), ("TTL", ["ttl"])),
            TraceEventType.PromptCacheRequestShape => metadata.Promote(
                ("Protocol", ["protocol"]), ("Attempt", ["attempt"]), ("Input count", ["inputCount"]),
                ("Input bytes", ["inputBytes"]), ("Tool choice", ["toolChoice"]), ("Streaming", ["streaming"])),
            TraceEventType.SubAgentPrefixDiagnostic => metadata.Promote(
                ("Status", ["status"]), ("Parent session", ["parentSessionKey"]), ("Matched", ["matchedCount"]),
                ("Parent items", ["parentCount"]), ("Child items", ["childCount"]), ("Divergence", ["divergenceIndex"])),
            TraceEventType.MaintenanceForkRequest => metadata.Promote(
                ("Task", ["taskKind"]), ("Provider", ["provider"]), ("Mode", ["mode"]),
                ("Snapshot items", ["snapshotCount"]), ("Tail items", ["tailCount"]), ("Budget", ["budget"])),
            TraceEventType.MaintenanceForkResponse => metadata.Promote(
                ("Provider", ["provider"]), ("Fallback", ["fallback"]), ("Response messages", ["responseMessages"])),
            TraceEventType.ThreadRollback => metadata.Promote(
                ("Thread", ["threadId"]), ("Turns removed", ["numTurns"]), ("Turns remaining", ["remainingTurns"]), ("Source", ["source"])),
            _ => [],
        });
        return fields;
    }

    private static DetailSectionItem Section(string label, IReadOnlyList<DetailFieldItem> fields, IReadOnlyList<DetailBlockItem> blocks) =>
        new() { Label = label, Fields = fields, Blocks = blocks };

    private static void Add(List<DetailFieldItem> fields, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new(label, value));
    }

    private static void AddBlock(List<DetailBlockItem> blocks, string label, string? value, bool isCode = false)
    {
        if (!string.IsNullOrWhiteSpace(value))
            blocks.Add(new(label, value, isCode));
    }

    private static string Preview(string? value)
    {
        var text = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 120 ? text : text[..117] + "...";
    }

    private static string FormatTokens(TraceEvent e) => e.TotalTokens is { } total
        ? $"{total:N0} total · {e.OutputTokens ?? 0:N0} output"
        : "Usage recorded";

    private static string Changed(TraceEvent e) => Join(e.PromptCacheChangedFields ?? e.ChangedToolNames) is { Length: > 0 } value
        ? value
        : e.PromptCacheEventKind ?? "Runtime context recorded";

    private static string Join(IEnumerable<string>? values) => values is null ? string.Empty : string.Join(", ", values);

    private static string Number(long? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? string.Empty;

    private static string Duration(double? value) => value is { } milliseconds ? $"{milliseconds:N0} ms" : string.Empty;

    private static string SplitName(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? $" {char.ToLowerInvariant(character)}" : character.ToString()));

    private sealed record EventDescription(
        string Title,
        string Summary,
        string Badge,
        TimelineLane Lane,
        bool IsDiagnostic,
        bool IsError,
        string? ContentLabel);
}
