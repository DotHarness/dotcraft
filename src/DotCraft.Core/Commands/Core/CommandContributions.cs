using DotCraft.Commands.Custom;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Commands.Core;

/// <summary>One invocation of a contributed slash command.</summary>
/// <param name="Name">The canonical, lowercase, slash-prefixed name that matched.</param>
/// <param name="Arguments">Everything after the command token, trimmed; empty when the command was invoked bare.</param>
public sealed record CommandInvocation(string Name, string Arguments)
{
    /// <summary>Gets the thread the command was invoked on, when the caller knows it.</summary>
    public string? ThreadId { get; init; }
}

/// <summary>
/// A code-backed slash command, listed and invoked exactly where a markdown custom command is.
/// Expansion only: a contributed command produces model input, never a direct client reply.
/// </summary>
public interface ICodeCommand : IContributionContract
{
    /// <summary>Gets the command name, with or without the leading slash.</summary>
    string Name { get; }

    /// <summary>Gets the description shown in command listings and in the model-visible summary.</summary>
    string Description { get; }

    /// <summary>Gets the alternate names this command also answers to.</summary>
    IReadOnlyList<string> Aliases => [];

    /// <summary>Expands one invocation into model input, or returns <see langword="null"/> to decline so the next contribution may answer.</summary>
    string? Expand(CommandInvocation invocation);
}

/// <summary>One contributed command as the host reads it: names already normalized, description already guarded.</summary>
public readonly record struct ContributedCommand(
    string Name,
    IReadOnlyList<string> Aliases,
    string Description);

/// <summary>
/// Reads the <see cref="ICodeCommand"/> contribution point. Contributed commands are consulted last, so one
/// never shadows a built-in handler, a markdown custom command, or a module's prompt command.
/// </summary>
public static class CommandContributions
{
    /// <summary>The category contributed commands report, so every client surface that already serves markdown custom commands serves them unchanged.</summary>
    public const string Category = "custom";

    /// <summary>Returns the canonical form of a command name: lowercase and slash-prefixed.</summary>
    public static string Normalize(string name)
    {
        var trimmed = name.Trim();
        return trimmed.StartsWith('/') ? trimmed.ToLowerInvariant() : $"/{trimmed.ToLowerInvariant()}";
    }

    /// <summary>Lists the contributed commands for a thread. A name claimed twice is listed once, by the contribution that claims it first.</summary>
    public static IReadOnlyList<ContributedCommand> List(
        IContributionView? contributions,
        string? threadId = null,
        ILogger? logger = null)
    {
        var resolved = contributions?.Resolve<ICodeCommand>(threadId);
        if (resolved is not { Count: > 0 })
            return [];

        // Claiming as we go: the first contribution to claim a name or alias keeps it.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listed = new List<ContributedCommand>(resolved.Count);
        foreach (var contribution in resolved)
        {
            if (!TryDescribe(contribution, logger, out var described) || !claimed.Add(described.Name))
                continue;

            listed.Add(described with { Aliases = [.. described.Aliases.Where(claimed.Add)] });
        }

        return listed;
    }

    /// <summary>Projects contributed commands into the shape the model-visible custom command summary renders.</summary>
    public static IReadOnlyList<CustomCommandInfo> ToCommandInfos(IReadOnlyList<ContributedCommand> commands)
    {
        var infos = new CustomCommandInfo[commands.Count];
        for (var index = 0; index < commands.Count; index++)
        {
            infos[index] = new CustomCommandInfo
            {
                Name = commands[index].Name.TrimStart('/'),
                Description = commands[index].Description,
                Source = "plugin"
            };
        }

        return infos;
    }

    /// <summary>Expands one invocation through the contribution point: the first contribution claiming the name that does not decline answers it.</summary>
    public static string? Expand(
        IContributionView? contributions,
        string command,
        string arguments,
        string? threadId = null,
        ILogger? logger = null)
    {
        var resolved = contributions?.Resolve<ICodeCommand>(threadId);
        if (resolved is not { Count: > 0 })
            return null;

        var normalized = Normalize(command);
        var invocation = new CommandInvocation(normalized, arguments.Trim()) { ThreadId = threadId };
        // Names the command being expanded so the failure report quotes the matched name, not the raw type.
        var expanding = normalized;
        return ContributionRead.FirstOpinion(
            resolved,
            contribution =>
            {
                if (!TryDescribe(contribution, logger, out var described))
                    return null;
                if (!string.Equals(described.Name, normalized, StringComparison.OrdinalIgnoreCase)
                    && !described.Aliases.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    return null;
                }

                expanding = described.Name;
                return contribution.Expand(invocation);
            },
            (_, exception) => logger?.LogWarning(
                exception,
                "Contributed command '{Command}' threw while expanding and was skipped.",
                expanding));
    }

    private static bool TryDescribe(
        ICodeCommand contribution,
        ILogger? logger,
        out ContributedCommand described)
    {
        try
        {
            var name = Normalize(contribution.Name);
            if (name.Length <= 1)
            {
                described = default;
                return false;
            }

            var aliases = contribution.Aliases
                .Where(static alias => !string.IsNullOrWhiteSpace(alias))
                .Select(Normalize)
                .Where(alias => alias.Length > 1 && !string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            described = new ContributedCommand(name, aliases, contribution.Description ?? string.Empty);
            return true;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(
                exception,
                "Command contribution {Contribution} threw while describing itself and was skipped.",
                contribution.GetType().FullName);
            described = default;
            return false;
        }
    }
}
