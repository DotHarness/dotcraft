namespace DotCraft.Security.ShellCommands;

internal static class WindowsDangerousCommands
{
    internal const string ForcedDeleteReason = "forced deletes are not permitted";

    internal const string UrlLaunchReason = "launching URLs is not permitted";

    private const string FileProtocolHandler = "url.dll,fileprotocolhandler";

    private static readonly HashSet<string> DeleteCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "remove-item", "ri", "rm", "del", "erase", "rd", "rmdir"
    };

    private static readonly HashSet<string> UrlLaunchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "start-process", "start", "saps", "invoke-item", "ii", "explorer", "mshta"
    };

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "iexplore"
    };

    internal static DangerousCommandMatch? MatchPowerShellWords(IReadOnlyList<string> command, CommandPlatform platform)
    {
        if (HasForcedDelete(command, platform))
        {
            return new DangerousCommandMatch(
                DangerousCommandKind.ForcedRemove,
                DangerousCommandDetector.CommandEvidence(command),
                ForcedDeleteReason);
        }

        return MatchUrlLaunch(command, platform);
    }

    internal static DangerousCommandMatch? MatchUrlLaunch(IReadOnlyList<string> command, CommandPlatform platform)
    {
        if (!LaunchesUrl(command, platform))
            return null;

        return new DangerousCommandMatch(
            DangerousCommandKind.Other,
            DangerousCommandDetector.CommandEvidence(command),
            UrlLaunchReason);
    }

    private static bool HasForcedDelete(IReadOnlyList<string> command, CommandPlatform platform)
    {
        // Literal extraction can place the cmdlet after the first word.
        var hasDelete = DeleteCmdlets.Contains(DangerousCommandDetector.ExecutableName(command[0], platform));
        var hasForce = false;
        foreach (var word in command)
        {
            hasDelete |= DeleteCmdlets.Contains(word);
            hasForce |= string.Equals(word, "-Force", StringComparison.OrdinalIgnoreCase);
        }

        return hasDelete && hasForce;
    }

    private static bool LaunchesUrl(IReadOnlyList<string> command, CommandPlatform platform)
    {
        if (!HasUrl(command))
            return false;

        var name = DangerousCommandDetector.ExecutableName(command[0], platform);
        if (UrlLaunchers.Contains(name) || Browsers.Contains(name))
            return true;

        // The handler is one argument, but a lowered command may have split it at the comma.
        if (string.Equals(name, "rundll32", StringComparison.OrdinalIgnoreCase)
            && string.Join(',', command).Contains(FileProtocolHandler, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var word in command)
        {
            if (word.Contains("shellexecute", StringComparison.OrdinalIgnoreCase)
                || word.Contains("shell.application", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasUrl(IReadOnlyList<string> command)
    {
        foreach (var word in command)
        {
            if (IsHttpUrl(word))
                return true;
        }

        return false;
    }

    private static bool IsHttpUrl(string word) =>
        Uri.TryCreate(word, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}
