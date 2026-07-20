using System.Text;
using System.Text.Json;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.ContextExport;

/// <summary>
/// Exports DotCraft thread, memory, rollback, and compaction context to Markdown.
/// </summary>
public sealed class ContextExportService
{
    private readonly ContextWorkspaceReader _reader = new();

    /// <summary>
    /// Exports the requested thread into a Markdown handoff or transcript document.
    /// </summary>
    /// <param name="options">Export options.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The exported Markdown and non-fatal warnings.</returns>
    public async Task<ContextExportResult> ExportAsync(
        ContextExportOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ThreadId))
            throw new ArgumentException("Thread id is required.", nameof(options));

        var loaded = await _reader.LoadThreadAsync(options.WorkspacePath, options.ThreadId.Trim(), ct)
            .ConfigureAwait(false);
        if (loaded == null)
            throw new KeyNotFoundException($"Thread '{options.ThreadId}' was not found in the selected workspace.");

        var warnings = loaded.Warnings.ToList();
        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(
            loaded.RolloutPath,
            loaded.Thread.Turns,
            excludedTurnId: null,
            ct,
            options.ThreadId.Trim()).ConfigureAwait(false);
        foreach (var warning in replay.Warnings ?? [])
        {
            warnings.Add(string.IsNullOrWhiteSpace(warning.TurnId)
                ? $"Model history warning ({warning.Code}): {warning.Message}"
                : $"Model history warning for turn '{warning.TurnId}' ({warning.Code}): {warning.Message}");
        }
        var memory = _reader.LoadMemory(loaded.Paths, options.History, options.HistoryTailChars);
        var markdown = BuildMarkdown(loaded, memory, replay, options, warnings);

        return new ContextExportResult
        {
            ThreadId = loaded.Thread.Id,
            Markdown = markdown,
            RolloutPath = loaded.RolloutPath,
            Warnings = warnings
        };
    }

    private static string BuildMarkdown(
        ContextLoadedThread loaded,
        ContextWorkspaceMemory memory,
        ModelHistoryReplayResult modelHistory,
        ContextExportOptions options,
        List<string> warnings)
    {
        var thread = loaded.Thread;
        var sb = new StringBuilder();

        sb.AppendLine($"# DotCraft Context Export: {thread.Id}");
        sb.AppendLine();
        sb.AppendLine("## Export Metadata");
        AppendMetadata(sb, "Generated At", DateTimeOffset.UtcNow.ToString("O"));
        AppendMetadata(sb, "Profile", options.Profile.ToString());
        AppendMetadata(sb, "Tool Results", options.ToolResults.ToString());
        AppendMetadata(sb, "History", options.History.ToString());
        AppendMetadata(sb, "Workspace", loaded.Paths.WorkspacePath);
        AppendMetadata(sb, "Craft Path", loaded.Paths.CraftPath);
        AppendMetadata(sb, "Rollout", loaded.RolloutPath);
        sb.AppendLine();

        sb.AppendLine("## Thread Metadata");
        AppendMetadata(sb, "Display Name", thread.DisplayName ?? "(none)");
        AppendMetadata(sb, "Status", thread.Status.ToString());
        AppendMetadata(sb, "Created At", thread.CreatedAt.ToString("O"));
        AppendMetadata(sb, "Last Active At", thread.LastActiveAt.ToString("O"));
        AppendMetadata(sb, "Origin Channel", thread.OriginChannel);
        if (options.Profile == ContextExportProfile.Transcript)
            AppendMetadata(sb, "Channel Context", SafeContextProjection.RedactText(thread.ChannelContext ?? "(none)"));
        AppendMetadata(sb, "History Mode", thread.HistoryMode.ToString());
        AppendMetadata(sb, "Turn Count", thread.Turns.Count.ToString());
        sb.AppendLine();

        AppendMemory(sb, memory, options);
        AppendContinuity(sb, loaded.ContinuityEvents);
        AppendCurrentContext(sb, modelHistory, options);
        AppendConversation(sb, thread, options);

        if (warnings.Count > 0)
        {
            sb.AppendLine("## Warnings");
            foreach (var warning in warnings.Distinct(StringComparer.Ordinal))
                sb.AppendLine($"- {warning}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendMemory(
        StringBuilder sb,
        ContextWorkspaceMemory memory,
        ContextExportOptions options)
    {
        sb.AppendLine("## Workspace Memory");
        sb.AppendLine($"Source: `{memory.MemoryPath}`");
        if (string.IsNullOrWhiteSpace(memory.Memory))
        {
            sb.AppendLine();
            sb.AppendLine("(empty)");
        }
        else
        {
            AppendCodeBlock(sb, "markdown", memory.Memory.TrimEnd());
        }

        sb.AppendLine();
        sb.AppendLine("## Memory History");
        if (options.History == ContextExportHistoryMode.None)
        {
            sb.AppendLine("Omitted by `--history none`.");
        }
        else
        {
            sb.AppendLine($"Source: `{memory.HistoryPath}`");
            if (string.IsNullOrWhiteSpace(memory.History))
            {
                sb.AppendLine();
                sb.AppendLine("(empty)");
            }
            else
            {
                var label = options.History == ContextExportHistoryMode.Tail
                    ? $"markdown title=\"tail {options.HistoryTailChars} chars\""
                    : "markdown";
                AppendCodeBlock(sb, label, memory.History.TrimEnd());
            }
        }

        sb.AppendLine();
    }

    private static void AppendContinuity(
        StringBuilder sb,
        IReadOnlyList<ContextContinuityEvent> events)
    {
        sb.AppendLine("## Continuity Events");
        if (events.Count == 0)
        {
            sb.AppendLine("No rollback or compaction checkpoint records were found in the rollout.");
            sb.AppendLine();
            return;
        }

        foreach (var evt in events.OrderBy(e => e.LineNumber))
        {
            if (evt.Kind == ContextContinuityEventKind.Rollback)
            {
                sb.AppendLine($"- Rollback at line {evt.LineNumber} ({evt.Timestamp:O}): removed {evt.NumTurns ?? 0} turn(s).");
            }
            else
            {
                sb.AppendLine(
                    $"- Compaction at line {evt.LineNumber} ({evt.Timestamp:O}): checkpoint `{evt.CheckpointId}`, covered through `{evt.CoveredThroughTurnId}`, trigger `{evt.Trigger}`, mode `{evt.Mode}`, tokens {evt.TokensBefore}->{evt.TokensAfter}.");
            }
        }
        sb.AppendLine();
    }

    private static void AppendCurrentContext(
        StringBuilder sb,
        ModelHistoryReplayResult replay,
        ContextExportOptions options)
    {
        sb.AppendLine("## Current Model-Visible Context");
        sb.AppendLine(replay.HasModelHistoryRecords
            ? $"Recovered with canonical model-history replay; {replay.RejectedRecords} record(s) rejected and {(replay.FallbackTurnIds?.Count ?? 0)} turn(s) reconstructed from visible items."
            : "No exact model-history records were found; reconstructed context from surviving visible turns.");
        sb.AppendLine();

        if (replay.Messages.Count == 0)
        {
            sb.AppendLine("(empty)");
            sb.AppendLine();
            return;
        }

        AppendChatMessages(sb, replay.Messages, options);
        sb.AppendLine();
    }

    private static void AppendConversation(
        StringBuilder sb,
        SessionThread thread,
        ContextExportOptions options)
    {
        sb.AppendLine("## Conversation");
        if (thread.Turns.Count == 0)
        {
            sb.AppendLine("(empty)");
            sb.AppendLine();
            return;
        }

        foreach (var turn in thread.Turns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            sb.AppendLine($"### Turn {turn.Id}");
            AppendMetadata(sb, "Status", turn.Status.ToString());
            AppendMetadata(sb, "Started", turn.StartedAt.ToString("O"));
            if (turn.CompletedAt.HasValue)
                AppendMetadata(sb, "Completed", turn.CompletedAt.Value.ToString("O"));
            if (!string.IsNullOrWhiteSpace(turn.Error))
                AppendMetadata(sb, "Turn Error", turn.Error!);
            if (turn.TokenUsage != null)
                AppendMetadata(sb, "Tokens", $"{turn.TokenUsage.InputTokens} input, {turn.TokenUsage.OutputTokens} output, {turn.TokenUsage.TotalTokens} total");
            sb.AppendLine();

            foreach (var item in turn.Items.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id, StringComparer.Ordinal))
                AppendItem(sb, item, options);

            sb.AppendLine();
        }
    }

    private static void AppendItem(
        StringBuilder sb,
        SessionItem item,
        ContextExportOptions options)
    {
        var timestamp = item.CompletedAt ?? item.CreatedAt;
        sb.AppendLine($"#### {FormatItemType(item.Type)} `{item.Id}` ({item.Status}, {timestamp:O})");

        switch (item.Type)
        {
            case ItemType.UserMessage when item.AsUserMessage is { } user:
                AppendTextOrEmpty(sb, user.Text);
                break;

            case ItemType.AgentMessage when item.AsAgentMessage is { } agent:
                AppendTextOrEmpty(sb, agent.Text);
                break;

            case ItemType.ReasoningContent:
                sb.AppendLine("[reasoning omitted]");
                break;

            case ItemType.CommandExecution when item.AsCommandExecution is { } command:
                AppendMetadata(sb, "Command", $"`{command.Command}`");
                AppendMetadata(sb, "Working Directory", command.WorkingDirectory);
                AppendMetadata(sb, "Status", command.Status);
                if (command.ExitCode.HasValue)
                    AppendMetadata(sb, "Exit Code", command.ExitCode.Value.ToString());
                AppendResult(sb, command.AggregatedOutput, options.ToolResults, options.ToolResultPreviewChars, "Command output");
                break;

            case ItemType.ToolExecution when item.AsToolExecution is { } toolExecution:
                AppendMetadata(sb, "Tool", toolExecution.ToolName);
                AppendMetadata(sb, "Call Id", toolExecution.CallId);
                AppendMetadata(sb, "Status", toolExecution.Status);
                if (toolExecution.Success.HasValue)
                    AppendMetadata(sb, "Success", toolExecution.Success.Value.ToString());
                if (!string.IsNullOrWhiteSpace(toolExecution.ErrorMessage))
                    AppendMetadata(sb, "Error", toolExecution.ErrorMessage!);
                if (!string.IsNullOrWhiteSpace(toolExecution.ResultPreview))
                    AppendResult(sb, toolExecution.ResultPreview!, options.ToolResults, options.ToolResultPreviewChars, "Tool execution preview");
                break;

            case ItemType.ImageGeneration when item.AsImageGeneration is { } imageGeneration:
                AppendMetadata(sb, "Call Id", imageGeneration.CallId);
                AppendMetadata(sb, "Status", imageGeneration.Status);
                if (!string.IsNullOrWhiteSpace(imageGeneration.RevisedPrompt))
                    AppendMetadata(sb, "Revised Prompt", imageGeneration.RevisedPrompt!);
                if (!string.IsNullOrWhiteSpace(imageGeneration.SavedPath))
                    AppendMetadata(sb, "Saved Path", imageGeneration.SavedPath!);
                if (!string.IsNullOrWhiteSpace(imageGeneration.ErrorMessage))
                    AppendMetadata(sb, "Error", imageGeneration.ErrorMessage!);
                break;

            case ItemType.ToolCall when item.AsToolCall is { } toolCall:
                AppendMetadata(sb, "Tool", toolCall.ToolName);
                AppendMetadata(sb, "Call Id", toolCall.CallId);
                if (toolCall.Arguments != null)
                {
                    var arguments = options.ToolResults == ContextExportToolResultMode.None
                        ? "[redacted]"
                        : SafeContextProjection.RedactJson(toolCall.Arguments.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
                    AppendCodeBlock(sb, "json", arguments);
                }
                break;

            case ItemType.PluginFunctionCall when item.AsPluginFunctionCall is { } pluginCall:
                AppendMetadata(sb, "Plugin", pluginCall.PluginId);
                AppendMetadata(sb, "Namespace", pluginCall.Namespace ?? "(none)");
                AppendMetadata(sb, "Function", pluginCall.FunctionName);
                AppendMetadata(sb, "Call Id", pluginCall.CallId);
                AppendMetadata(sb, "Success", pluginCall.Success.ToString());
                if (pluginCall.Arguments != null)
                {
                    var arguments = options.ToolResults == ContextExportToolResultMode.None
                        ? "[redacted]"
                        : SafeContextProjection.RedactJson(pluginCall.Arguments.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
                    AppendCodeBlock(sb, "json", arguments);
                }
                AppendStructuredResult(sb, pluginCall.StructuredResult, pluginCall.ErrorCode, pluginCall.ErrorMessage, options);
                break;

            case ItemType.DynamicToolCall when item.Payload is DynamicToolCallPayload dynamicCall:
                AppendMetadata(sb, "Namespace", dynamicCall.Namespace ?? "(none)");
                AppendMetadata(sb, "Tool", dynamicCall.ToolName);
                AppendMetadata(sb, "Call Id", dynamicCall.CallId);
                AppendMetadata(sb, "Status", dynamicCall.Status);
                if (dynamicCall.Success.HasValue)
                    AppendMetadata(sb, "Success", dynamicCall.Success.Value.ToString());
                if (dynamicCall.Arguments != null)
                {
                    var arguments = options.ToolResults == ContextExportToolResultMode.None
                        ? "[redacted]"
                        : SafeContextProjection.RedactJson(dynamicCall.Arguments.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
                    AppendCodeBlock(sb, "json", arguments);
                }
                AppendStructuredResult(sb, dynamicCall.StructuredContent, dynamicCall.ErrorCode, dynamicCall.ErrorMessage, options);
                break;

            case ItemType.ToolResult when item.AsToolResult is { } toolResult:
                AppendMetadata(sb, "Call Id", toolResult.CallId);
                AppendMetadata(sb, "Success", toolResult.Success.ToString());
                AppendResult(sb, toolResult.Result, options.ToolResults, options.ToolResultPreviewChars, "Tool result");
                break;

            case ItemType.ApprovalRequest when item.AsApprovalRequest is { } approvalRequest:
                AppendMetadata(sb, "Approval Type", approvalRequest.ApprovalType);
                AppendMetadata(sb, "Operation", approvalRequest.Operation);
                AppendMetadata(sb, "Target", approvalRequest.Target);
                AppendMetadata(sb, "Request Id", approvalRequest.RequestId);
                AppendTextOrEmpty(sb, approvalRequest.Reason);
                break;

            case ItemType.ApprovalResponse when item.AsApprovalResponse is { } approvalResponse:
                AppendMetadata(sb, "Request Id", approvalResponse.RequestId);
                AppendMetadata(sb, "Approved", approvalResponse.Approved.ToString());
                AppendMetadata(sb, "Decision", approvalResponse.Decision.ToString());
                break;

            case ItemType.UserInputRequest when item.AsUserInputRequest is { } inputRequest:
                AppendMetadata(sb, "Request Id", inputRequest.RequestId);
                foreach (var question in inputRequest.Questions)
                    sb.AppendLine($"- {question.Header}: {question.Question}");
                break;

            case ItemType.UserInputResponse when item.AsUserInputResponse is { } inputResponse:
                AppendMetadata(sb, "Request Id", inputResponse.RequestId);
                AppendJsonPayload(sb, inputResponse.Response);
                break;

            case ItemType.SystemNotice when item.AsSystemNotice is { } notice:
                AppendMetadata(sb, "Kind", notice.Kind);
                AppendMetadata(sb, "Trigger", notice.Trigger);
                AppendMetadata(sb, "Mode", notice.Mode);
                if (!string.IsNullOrWhiteSpace(notice.SourceThreadId))
                    AppendMetadata(sb, "Source Thread", notice.SourceThreadId);
                AppendMetadata(sb, "Tokens", $"{notice.TokensBefore}->{notice.TokensAfter}");
                break;

            case ItemType.Error when item.AsError is { } error:
                AppendMetadata(sb, "Code", error.Code);
                AppendMetadata(sb, "Fatal", error.Fatal.ToString());
                AppendTextOrEmpty(sb, error.Message);
                break;

            default:
                if (item.Payload != null)
                    AppendJsonPayload(sb, item.Payload);
                else
                    sb.AppendLine("(no payload)");
                break;
        }

        sb.AppendLine();
    }

    private static void AppendStructuredResult(
        StringBuilder sb,
        object? structuredResult,
        string? errorCode,
        string? errorMessage,
        ContextExportOptions options)
    {
        if (!string.IsNullOrWhiteSpace(errorCode))
            AppendMetadata(sb, "Error Code", errorCode!);
        if (!string.IsNullOrWhiteSpace(errorMessage))
            AppendMetadata(sb, "Error Message", errorMessage!);
        if (structuredResult == null)
            return;

        var json = JsonSerializer.Serialize(
            structuredResult,
            structuredResult.GetType(),
            new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true });
        AppendResult(sb, json, options.ToolResults, options.ToolResultPreviewChars, "Structured result");
    }

    private static void AppendResult(
        StringBuilder sb,
        string value,
        ContextExportToolResultMode mode,
        int previewChars,
        string label)
    {
        if (string.IsNullOrEmpty(value))
        {
            sb.AppendLine($"{label}: (empty)");
            return;
        }

        switch (mode)
        {
            case ContextExportToolResultMode.None:
                sb.AppendLine($"{label}: omitted by `--tool-results none`.");
                break;
            case ContextExportToolResultMode.Summary:
                sb.AppendLine($"{label} preview:");
                AppendCodeBlock(sb, string.Empty, ContextWorkspaceReader.Bound(value, Math.Max(1, previewChars)));
                break;
            case ContextExportToolResultMode.Full:
                sb.AppendLine($"{label}:");
                AppendCodeBlock(sb, string.Empty, value.TrimEnd());
                break;
        }
    }

    private static void AppendChatMessages(
        StringBuilder sb,
        IReadOnlyList<ChatMessage> messages,
        ContextExportOptions options)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            sb.AppendLine($"### Context Message {i + 1}: {message.Role}");
            if (message.Contents.Count == 0)
            {
                sb.AppendLine("(empty)");
                sb.AppendLine();
                continue;
            }

            foreach (var content in message.Contents)
            {
                if (content is TextContent textContent)
                {
                    AppendTextOrEmpty(sb, textContent.Text);
                    continue;
                }

                var typeName = content.GetType().Name;
                if (typeName.Contains("Reasoning", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine("[reasoning omitted]");
                    continue;
                }

                if (content is FunctionCallContent)
                {
                    sb.AppendLine($"Tool call `{GetPropertyValue(content, "Name") ?? "unknown"}` (`{GetPropertyValue(content, "CallId") ?? "unknown"}`):");
                    AppendCodeBlock(sb, "json", SerializeProperty(content, "Arguments"));
                    continue;
                }

                if (content is FunctionResultContent)
                {
                    sb.AppendLine($"Tool result (`{GetPropertyValue(content, "CallId") ?? "unknown"}`):");
                    AppendResult(
                        sb,
                        SerializeProperty(content, "Result"),
                        options.ToolResults,
                        options.ToolResultPreviewChars,
                        "Tool result");
                    continue;
                }

                sb.AppendLine($"[{content.GetType().Name} payload omitted]");
            }

            sb.AppendLine();
        }
    }

    private static string SerializeProperty(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        if (property?.GetValue(value) is not { } propertyValue)
            return "{}";

        if (propertyValue is string text)
            return SafeContextProjection.RedactText(text);

        return SafeContextProjection.RedactJson(JsonSerializer.Serialize(
            propertyValue,
            propertyValue.GetType(),
            new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
    }

    private static string? GetPropertyValue(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName);
        return property?.GetValue(value)?.ToString();
    }

    private static void AppendJsonPayload(StringBuilder sb, object payload)
    {
        var json = JsonSerializer.Serialize(
            payload,
            payload.GetType(),
            new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true });
        AppendCodeBlock(sb, "json", json);
    }

    private static void AppendTextOrEmpty(StringBuilder sb, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            sb.AppendLine("(empty)");
        else
            sb.AppendLine(value.TrimEnd());
    }

    private static void AppendMetadata(StringBuilder sb, string name, string value)
    {
        sb.AppendLine($"- {name}: {value}");
    }

    private static string FormatItemType(ItemType type) => type switch
    {
        ItemType.UserMessage => "User",
        ItemType.AgentMessage => "Assistant",
        ItemType.ReasoningContent => "Reasoning",
        ItemType.CommandExecution => "Command",
        ItemType.ToolExecution => "Tool Execution",
        ItemType.ImageGeneration => "Image Generation",
        ItemType.ToolCall => "Tool Call",
        ItemType.PluginFunctionCall => "Plugin Function Call",
        ItemType.DynamicToolCall => "Dynamic Tool Call",
        ItemType.ToolResult => "Tool Result",
        ItemType.ApprovalRequest => "Approval Request",
        ItemType.ApprovalResponse => "Approval Response",
        ItemType.UserInputRequest => "User Input Request",
        ItemType.UserInputResponse => "User Input Response",
        ItemType.SystemNotice => "System Notice",
        ItemType.Error => "Error",
        _ => type.ToString()
    };

    private static string EscapeInline(string value) =>
        value.Replace("`", "\\`", StringComparison.Ordinal);

    private static void AppendCodeBlock(StringBuilder sb, string language, string content)
    {
        var fence = "```";
        while (content.Contains(fence, StringComparison.Ordinal))
            fence += "`";

        sb.AppendLine($"{fence}{language}");
        sb.AppendLine(content);
        sb.AppendLine(fence);
    }

}
