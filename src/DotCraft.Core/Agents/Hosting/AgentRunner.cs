using System.Text;
using DotCraft.Protocol;
using DotCraft.Tools;
using Spectre.Console;

namespace DotCraft.Agents;

/// <summary>
/// Result of a background agent run (cron, heartbeat, gateway cron).
/// </summary>
public sealed record AgentRunResult(
    string? Result,
    string? ThreadId,
    int? InputTokens,
    int? OutputTokens,
    string? Error);

/// <summary>
/// Delegate for running an agent session.
/// </summary>
/// <param name="threadDisplayName">
/// Optional sidebar title for new threads (e.g. cron job name); avoids deriving the name from the system prompt wrapper.
/// </param>
/// <param name="cancellationToken">Cancellation token (last parameter per common .NET convention).</param>
public delegate Task<AgentRunResult?> AgentRunSessionDelegate(
    string prompt,
    string sessionKey,
    string? threadDisplayName = null,
    CancellationToken cancellationToken = default);

/// <summary>
/// Shared agent execution logic used across all channel modes for running an agent session
/// via <see cref="ISessionService"/>. Used for heartbeat and cron-triggered runs.
/// </summary>
public sealed class AgentRunner(string workspacePath, ISessionService? sessionService = null, bool quiet = false)
{
    /// <summary>
    /// Run agent with a prompt, manage session lifecycle, stream output, and log results.
    /// Delegates to <see cref="ISessionService"/> for unified Thread management.
    /// When <paramref name="quiet"/> is true all console progress lines are suppressed;
    /// use this in subprocess contexts where output is forwarded via another channel.
    /// </summary>
    /// <inheritdoc cref="AgentRunSessionDelegate" />
    public async Task<AgentRunResult?> RunAsync(
        string prompt,
        string sessionKey,
        string? threadDisplayName = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionService == null)
            return null;

        var rawUserPrompt = prompt;

        var tag = sessionKey.StartsWith("heartbeat") ? "Heartbeat"
            : sessionKey.StartsWith("cron:") ? "Cron"
            : "Agent";

        if (sessionKey.StartsWith("cron:"))
        {
            prompt = $"[System: Scheduled Task Triggered]\n" +
                     $"The following is a scheduled cron job that has just been triggered. " +
                     $"Execute the task described below directly and respond with the result. " +
                     $"Do NOT treat this as a user conversation or create a new scheduled task.\n\n" +
                     $"Task: {prompt}";
        }

        if (!quiet)
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Running: [dim]{Markup.Escape(prompt.Length > 120 ? prompt[..120] + "..." : prompt)}[/]");

        // Build identity for this session type
        var channelName = sessionKey.StartsWith("heartbeat") ? "heartbeat"
            : sessionKey.StartsWith("cron:") ? "cron"
            : "agent";
        var identity = new SessionIdentity
        {
            ChannelName = channelName,
            UserId = sessionKey,
            WorkspacePath = workspacePath
        };

        // Find or create a Thread for this session key
        IReadOnlyList<ThreadSummary> existing;
        try { existing = await sessionService.FindThreadsAsync(identity, ct: cancellationToken); }
        catch { existing = []; }

        SessionThread thread;
        var matchingThread = existing.FirstOrDefault(s => s.Status != ThreadStatus.Archived);
        string? displayNameForNewThread = null;
        if (sessionKey.StartsWith("cron:"))
        {
            if (!string.IsNullOrWhiteSpace(threadDisplayName))
                displayNameForNewThread = TruncateDisplay(threadDisplayName.Trim());
            else if (!string.IsNullOrWhiteSpace(rawUserPrompt))
                displayNameForNewThread = TruncateDisplay(rawUserPrompt);
        }

        if (matchingThread != null)
            thread = await sessionService.ResumeThreadAsync(matchingThread.Id, cancellationToken);
        else
            thread = await sessionService.CreateThreadAsync(identity, displayName: displayNameForNewThread, ct: cancellationToken);

        var threadId = thread.Id;

        // Mark automation-triggered turns so client UIs can render a
        // "Sent via automation" affordance on the synthesized user message.
        TurnTriggerInfo? triggerInfo = null;
        if (sessionKey.StartsWith("heartbeat"))
        {
            triggerInfo = new TurnTriggerInfo { Kind = "heartbeat", Label = threadDisplayName };
        }
        else if (sessionKey.StartsWith("cron:"))
        {
            var jobId = sessionKey.Length > 5 ? sessionKey[5..] : null;
            triggerInfo = new TurnTriggerInfo
            {
                Kind = "cron",
                RefId = string.IsNullOrEmpty(jobId) ? null : jobId,
                Label = !string.IsNullOrWhiteSpace(threadDisplayName)
                    ? threadDisplayName
                    : (rawUserPrompt.Length > 50 ? rawUserPrompt[..50] + "..." : rawUserPrompt)
            };
        }

        using var triggerScope = triggerInfo != null ? TurnTriggerScope.Set(triggerInfo) : null;

        // Consume the event stream and accumulate the agent response text
        var sb = new StringBuilder();
        int? inputTokens = null;
        int? outputTokens = null;
        await foreach (var evt in sessionService.SubmitInputAsync(thread.Id, prompt, ct: cancellationToken))
        {
            switch (evt.EventType)
            {
                case SessionEventType.ItemDelta when evt.DeltaPayload is { } delta && !string.IsNullOrEmpty(delta.TextDelta):
                    sb.Append(delta.TextDelta);
                    break;

                case SessionEventType.ItemStarted when evt.ItemPayload?.Type == ItemType.ToolCall &&
                                                       evt.ItemPayload.AsToolCall is { } tc:
                {
                    if (!quiet)
                    {
                        var icon = ToolRegistry.GetToolIcon(tc.ToolName);
                        var displayText = tc.Arguments != null
                            ? ToolRegistry.FormatToolCall(tc.ToolName, tc.Arguments) ?? tc.ToolName
                            : tc.ToolName;
                        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");
                    }
                    break;
                }

                case SessionEventType.ItemCompleted when evt.ItemPayload?.Type == ItemType.ToolResult &&
                                                         evt.ItemPayload.AsToolResult is { } tr:
                {
                    if (!quiet)
                    {
                        var result = tr.Result;
                        var preview = result.Length > 200 ? result[..200] + "..." : result;
                        var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
                        AnsiConsole.MarkupLine($"[grey][[{tag}]][/]   [grey]{Markup.Escape(normalized)}[/]");
                    }
                    break;
                }

                case SessionEventType.TurnCompleted:
                {
                    var usage = evt.TurnPayload?.TokenUsage;
                    if (usage != null)
                    {
                        inputTokens = (int)usage.InputTokens;
                        outputTokens = (int)usage.OutputTokens;
                        if (!quiet && (usage.InputTokens > 0 || usage.OutputTokens > 0))
                            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {usage.InputTokens} input[/] [green]↓ {usage.OutputTokens} output[/]");
                    }
                    break;
                }

                case SessionEventType.TurnFailed:
                {
                    var errMsg = evt.TurnPayload?.Error ?? "Turn failed";
                    if (!quiet)
                        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]Turn failed: {Markup.Escape(errMsg)}[/]");
                    return new AgentRunResult(null, threadId, null, null, errMsg);
                }
            }
        }

        var response = sb.Length > 0 ? sb.ToString() : null;
        if (response != null && !quiet)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");
        }

        return new AgentRunResult(response, threadId, inputTokens, outputTokens, null);
    }

    /// <summary>Matches session list preview length in <see cref="Protocol.SessionService"/>.</summary>
    private static string TruncateDisplay(string text, int maxLen = 50)
    {
        var normalized = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (normalized.Length <= maxLen)
            return normalized;
        return normalized[..maxLen] + "...";
    }
}
