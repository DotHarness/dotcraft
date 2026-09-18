namespace DotCraft.Security.ShellCommands;

public sealed class DangerousCommandDetector(IShellScriptLowerer posixLowerer)
{
    private const int MaxWrapperDepth = 8;

    private const string ForcedRemoveReason = "rm -f style commands are not permitted";

    private const string WrapperDepthReason = "command wrappers nest too deeply";

    private static readonly HashSet<string> PosixShells = new(StringComparer.Ordinal) { "bash", "sh", "zsh" };

    public DangerousCommandMatch? Match(IReadOnlyList<string> command, ShellFamily family, CommandPlatform platform) =>
        MatchAtDepth(command, family, platform, wrapperDepth: 0);

    public DangerousCommandMatch? MatchAny(LoweredScript lowering, CommandPlatform platform)
    {
        foreach (var command in lowering.LiteralCommands)
        {
            if (Match(command, lowering.Family, platform) is { } match)
                return match;
        }

        return null;
    }

    internal static string CommandEvidence(IReadOnlyList<string> command) => string.Join(' ', command);

    private DangerousCommandMatch? MatchAtDepth(
        IReadOnlyList<string> command,
        ShellFamily family,
        CommandPlatform platform,
        int wrapperDepth)
    {
        if (command.Count == 0)
            return null;
        if (wrapperDepth > MaxWrapperDepth)
            return new DangerousCommandMatch(DangerousCommandKind.Other, CommandEvidence(command), WrapperDepthReason);

        var match = family switch
        {
            ShellFamily.Posix => MatchPosix(command, platform, wrapperDepth),
            ShellFamily.PowerShell => WindowsDangerousCommands.MatchPowerShellWords(command, platform),
            ShellFamily.Cmd => MatchCmd(command, platform, wrapperDepth),
            _ => null
        };
        if (match is not null)
            return match;

        // A Posix or Cmd script on Windows can still reach the same launchers through an alias.
        return platform == CommandPlatform.Windows
            ? WindowsDangerousCommands.MatchUrlLaunch(command, platform)
            : null;
    }

    private DangerousCommandMatch? MatchPosix(
        IReadOnlyList<string> command,
        CommandPlatform platform,
        int wrapperDepth)
    {
        var name = ExecutableName(command[0], platform);
        switch (name)
        {
            case "rm":
                return IncludesForceOption(command)
                    ? new DangerousCommandMatch(DangerousCommandKind.ForcedRemove, CommandEvidence(command), ForcedRemoveReason)
                    : null;
            case "sudo":
                return MatchAtDepth(Slice(command, 1), ShellFamily.Posix, platform, wrapperDepth + 1);
            case "env":
                return MatchEnv(command, platform, wrapperDepth);
            case "trap":
                return MatchTrap(command, platform, wrapperDepth);
            default:
                return PosixShells.Contains(name) ? MatchShellScript(command, platform, wrapperDepth) : null;
        }
    }

    private static bool IncludesForceOption(IReadOnlyList<string> command)
    {
        for (var index = 1; index < command.Count; index++)
        {
            var argument = command[index];
            if (argument == "--")
                return false;
            if (argument == "--force")
                return true;
            if (argument.Length > 1 && argument[0] == '-' && argument[1] != '-' && argument.Contains('f'))
                return true;
        }

        return false;
    }

    private DangerousCommandMatch? MatchEnv(
        IReadOnlyList<string> command,
        CommandPlatform platform,
        int wrapperDepth)
    {
        var index = 1;
        while (index < command.Count)
        {
            var argument = command[index];
            if (argument == "--")
            {
                index++;
                break;
            }

            if (argument is "-i" or "--ignore-environment" || IsEnvironmentAssignment(argument))
            {
                index++;
                continue;
            }

            break;
        }

        return MatchAtDepth(Slice(command, index), ShellFamily.Posix, platform, wrapperDepth + 1);
    }

    private static bool IsEnvironmentAssignment(string argument) =>
        argument.Length > 0 && argument[0] != '-' && argument.IndexOf('=') > 0;

    private DangerousCommandMatch? MatchTrap(
        IReadOnlyList<string> command,
        CommandPlatform platform,
        int wrapperDepth)
    {
        var index = 1;
        if (index < command.Count && command[index] == "--")
            index++;
        if (index >= command.Count)
            return null;

        // A trap action is shell source held in the first operand; options come after it.
        var action = command[index];
        return action.StartsWith('-') ? null : MatchScript(action, platform, wrapperDepth);
    }

    private DangerousCommandMatch? MatchShellScript(
        IReadOnlyList<string> command,
        CommandPlatform platform,
        int wrapperDepth)
    {
        if (command.Count != 3 || command[1] is not ("-c" or "-lc"))
            return null;

        return MatchScript(command[2], platform, wrapperDepth);
    }

    private DangerousCommandMatch? MatchScript(string script, CommandPlatform platform, int wrapperDepth)
    {
        foreach (var inner in posixLowerer.Lower(script).LiteralCommands)
        {
            if (MatchAtDepth(inner, ShellFamily.Posix, platform, wrapperDepth + 1) is { } match)
                return match;
        }

        return null;
    }

    private DangerousCommandMatch? MatchCmd(
        IReadOnlyList<string> command,
        CommandPlatform platform,
        int wrapperDepth)
    {
        var name = ExecutableName(command[0], platform);
        if (Is(name, "cmd"))
        {
            var body = CmdWrapperBody(command);
            return body is null ? null : MatchAtDepth(body, ShellFamily.Cmd, platform, wrapperDepth + 1);
        }

        if (Is(name, "del") || Is(name, "erase"))
            return HasFlag(command, "/f") ? ForcedDelete(command) : null;
        if (Is(name, "rd") || Is(name, "rmdir"))
            return HasFlag(command, "/s") && HasFlag(command, "/q") ? ForcedDelete(command) : null;
        if (Is(name, "start"))
            return WindowsDangerousCommands.MatchUrlLaunch(command, platform);

        return null;
    }

    private static IReadOnlyList<string>? CmdWrapperBody(IReadOnlyList<string> command)
    {
        for (var index = 1; index < command.Count; index++)
        {
            var argument = command[index];
            if (Is(argument, "/c") || Is(argument, "/r") || Is(argument, "-c"))
                return index + 1 < command.Count ? Slice(command, index + 1) : null;
            if (!argument.StartsWith('/'))
                return null;
        }

        return null;
    }

    private static DangerousCommandMatch ForcedDelete(IReadOnlyList<string> command) =>
        new(DangerousCommandKind.ForcedRemove, CommandEvidence(command), WindowsDangerousCommands.ForcedDeleteReason);

    private static bool HasFlag(IReadOnlyList<string> command, string flag)
    {
        for (var index = 1; index < command.Count; index++)
        {
            if (Is(command[index], flag))
                return true;
        }

        return false;
    }

    private static bool Is(string value, string other) =>
        string.Equals(value, other, StringComparison.OrdinalIgnoreCase);

    internal static string ExecutableName(string word, CommandPlatform platform) =>
        ShellIdentityResolver.ExecutableName(word, platform == CommandPlatform.Windows);

    private static IReadOnlyList<string> Slice(IReadOnlyList<string> command, int start) =>
        start >= command.Count ? [] : [.. command.Skip(start)];
}
