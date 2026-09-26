using System.Text;
using System.Text.Json;
using DotCraft.Configuration;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static void ApplyForkTurns(
        SessionThread childThread,
        SessionThread parentThread,
        string forkTurns,
        DateTimeOffset now)
    {
        var selected = SelectForkTurns(parentThread, forkTurns, childThread.Id, now);
        if (selected.Count > 0)
            childThread.Turns.AddRange(selected);
    }

    private static string BuildExternalForkContext(SessionThread parentThread, string forkTurns)
    {
        var selected = SelectForkTurns(parentThread, forkTurns, parentThread.Id, DateTimeOffset.UtcNow);
        return RenderTurnsAsPromptContext(selected);
    }

    private static string BuildExternalThreadContextPrompt(SessionThread childThread, string message)
    {
        var context = RenderTurnsAsPromptContext(childThread.Turns.Where(turn => !IsActiveTurn(turn)).ToArray());
        if (string.IsNullOrWhiteSpace(context))
            return message;

        return
$$"""
## Existing Thread Context

{{context}}

## Task

{{message}}
""";
    }

    private static List<SessionTurn> SelectForkTurns(
        SessionThread source,
        string forkTurns,
        string targetThreadId,
        DateTimeOffset now)
    {
        if (string.Equals(forkTurns, "none", StringComparison.OrdinalIgnoreCase))
            return [];

        var stable = source.Turns.Where(turn => !IsActiveTurn(turn)).ToList();
        if (int.TryParse(forkTurns, out var count))
            stable = stable.TakeLast(count).ToList();

        var selected = DeepCloneTurns(stable);
        var active = source.Turns.LastOrDefault(IsActiveTurn);
        if (active?.Input != null)
        {
            var activeInput = DeepCloneTurns([new SessionTurn
            {
                Id = active.Id,
                ThreadId = active.ThreadId,
                Status = TurnStatus.Completed,
                Input = active.Input,
                Items = [active.Input],
                StartedAt = active.StartedAt,
                CompletedAt = now,
                OriginChannel = active.OriginChannel,
                Initiator = active.Initiator
            }]).Single();
            selected.Add(activeInput);
        }

        RetargetTurns(selected, targetThreadId);
        return selected;
    }

    private static List<SessionTurn> DeepCloneTurns(IReadOnlyList<SessionTurn> turns)
    {
        var json = JsonSerializer.Serialize(turns, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<List<SessionTurn>>(json, SessionJsonOptions.Default) ?? [];
    }

    private static void RetargetTurns(List<SessionTurn> turns, string threadId)
    {
        foreach (var turn in turns)
        {
            turn.ThreadId = threadId;
            foreach (var item in turn.Items)
                item.TurnId = turn.Id;
            turn.Input = turn.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage) ?? turn.Input;
        }
    }

    private static bool IsActiveTurn(SessionTurn turn) =>
        turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput;

    private static string RenderTurnsAsPromptContext(IReadOnlyList<SessionTurn> turns)
    {
        var sb = new StringBuilder();
        foreach (var turn in turns)
        {
            var user = turn.Input?.AsUserMessage?.Text;
            if (!string.IsNullOrWhiteSpace(user))
            {
                sb.AppendLine("User:");
                sb.AppendLine(user.Trim());
                sb.AppendLine();
            }

            var agentText = ExtractFinalAgentText(turn);
            if (!string.IsNullOrWhiteSpace(agentText))
            {
                sb.AppendLine("Agent:");
                sb.AppendLine(agentText.Trim());
                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeForkTurns(string? forkTurns)
    {
        var normalized = NormalizeOptional(forkTurns) ?? "all";
        if (string.Equals(normalized, "all", StringComparison.OrdinalIgnoreCase))
            return "all";
        if (string.Equals(normalized, "none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (int.TryParse(normalized, out var count) && count > 0)
            return count.ToString();

        throw new ArgumentException("'forkTurns' must be 'all', 'none', or a positive integer string.", nameof(forkTurns));
    }

    private static string BuildExternalRuntimePrompt(
        string prompt,
        SubAgentRoleConfig role,
        string forkContext)
    {
        var rolePrompt = BuildExternalRolePrompt(prompt, role);
        if (string.IsNullOrWhiteSpace(forkContext))
            return rolePrompt;

        return
$$"""
## Parent Context

{{forkContext}}

{{rolePrompt}}
""";
    }

    private static string BuildExternalRolePrompt(string prompt, SubAgentRoleConfig role)
    {
        if (string.IsNullOrWhiteSpace(role.Instructions))
            return prompt;

        return
$$"""
## SubAgent Role: {{role.Name}}

{{role.Instructions.Trim()}}

## Task

{{prompt}}
""";
    }
}
