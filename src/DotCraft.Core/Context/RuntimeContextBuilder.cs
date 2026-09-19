using Microsoft.Extensions.AI;
using DotCraft.Context.WorldState;
using DotCraft.Sessions;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;

namespace DotCraft.Context;

/// <summary>
/// Builds the reminder appended to each user message. It carries only facts that belong to that
/// message; state that persists across turns belongs to <see cref="WorldState"/>.
/// </summary>
public static class RuntimeContextBuilder
{
    public static IList<AIContent> AppendRuntimeContext(
        this IList<AIContent> contents,
        TurnInitiatorContext? initiator = null,
        string? workspacePath = null,
        ThreadGoal? threadGoal = null,
        IReadOnlyList<IChatContextProvider>? chatContextProviders = null)
    {
        if (BuildBlock(initiator, workspacePath, threadGoal, chatContextProviders) is { } block)
            contents.Add(new TextContent($"\n{block}"));
        return contents;
    }

    internal static string? BuildBlock(
        TurnInitiatorContext? initiator = null,
        string? workspacePath = null,
        ThreadGoal? threadGoal = null,
        IReadOnlyList<IChatContextProvider>? chatContextProviders = null)
    {
        var sections = new List<string>();

        var providerLines = (chatContextProviders ?? [])
            .SelectMany(provider => provider.GetRuntimeContextLines())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (providerLines.Count > 0)
            sections.Add("## Additional Runtime Context\n" + string.Join("\n", providerLines));

        if (ThreadGoalSection.RenderUsage(threadGoal) is { } usage)
            sections.Add(usage);

        var initiatorLines = BuildInitiatorLines(initiator, workspacePath);
        if (initiatorLines.Count > 0)
            sections.Add("## Request Source\n" + string.Join("\n", initiatorLines));

        return sections.Count == 0
            ? null
            : "<system-reminder>\n" + string.Join("\n\n", sections) + "\n</system-reminder>";
    }

    private static List<string> BuildInitiatorLines(TurnInitiatorContext? initiator, string? workspacePath)
    {
        var lines = new List<string>();
        if (initiator is null)
            return lines;

        var channel = initiator.ChannelName?.Trim();
        if (IsInformativeChannel(channel))
            AddLine(lines, "Channel", channel);

        if (!IsWorkspaceChannelContextForWorkspace(initiator.ChannelContext, workspacePath))
            AddLine(lines, "Conversation", initiator.ChannelContext);

        if (string.IsNullOrWhiteSpace(initiator.UserName) && !IsLocalSenderId(initiator.UserId))
            AddLine(lines, "SenderId", initiator.UserId);
        AddLine(lines, "SenderName", initiator.UserName);
        AddLine(lines, "SenderRole", initiator.UserRole);
        AddLine(lines, "GroupChatId", initiator.GroupId);
        return lines;
    }

    private static bool IsInformativeChannel(string? channel) =>
        !string.IsNullOrWhiteSpace(channel)
        && !string.Equals(channel, "cli", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(channel, "acp", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(channel, "desktop", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalSenderId(string? senderId) =>
        string.IsNullOrWhiteSpace(senderId)
        || string.Equals(senderId, "local", StringComparison.OrdinalIgnoreCase)
        || string.Equals(senderId, "dotcraft-desktop", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorkspaceChannelContextForWorkspace(string? channelContext, string? workspacePath)
    {
        const string prefix = "workspace:";
        if (string.IsNullOrWhiteSpace(channelContext)
            || string.IsNullOrWhiteSpace(workspacePath)
            || !channelContext.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var contextPath = channelContext[prefix.Length..].Trim();
        return PathsEqual(contextPath, workspacePath);
    }

    private static bool PathsEqual(string left, string right)
    {
        var normalizedLeft = NormalizePathForComparison(left);
        var normalizedRight = NormalizePathForComparison(right);
        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForComparison(string path)
    {
        var trimmed = path.Trim();
        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (PathTooLongException)
        {
        }

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value}");
    }
}
