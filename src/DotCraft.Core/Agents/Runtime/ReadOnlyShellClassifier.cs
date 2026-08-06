namespace DotCraft.Agents;

/// <summary>
/// Classifies a shell command as non-mutating. Used by Plan mode and by SubAgent roles
/// that grant read-only shell access, so read-only callers can still observe workspace
/// state through commands such as <c>git diff</c>.
/// </summary>
internal static class ReadOnlyShellClassifier
{
    private static readonly HashSet<string> PowerShellReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem",
        "gci",
        "dir",
        "ls",
        "Get-Content",
        "gc",
        "cat",
        "type",
        "Select-String",
        "sls",
        "Measure-Object",
        "measure",
        "Resolve-Path",
        "Test-Path"
    };

    private static readonly HashSet<string> UnixReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls",
        "find",
        "grep",
        "rg"
    };

    private static readonly HashSet<string> ReadOnlyGitSubcommands = new(StringComparer.Ordinal)
    {
        "status",
        "diff",
        "log",
        "show"
    };

    /// <summary>
    /// Git global options that carry a separate value argument and redirect git away from the
    /// current repository or inject configuration. Rejected outright.
    /// </summary>
    private static readonly HashSet<string> UnsafeGitGlobalOptionsWithValue = new(StringComparer.Ordinal)
    {
        "-C",
        "-c",
        "--git-dir",
        "--work-tree",
        "--exec-path",
        "--namespace",
        "--config-env",
        "--super-prefix"
    };

    /// <summary>
    /// Git options that can invoke an external program even under a read-only subcommand.
    /// </summary>
    private static readonly HashSet<string> UnsafeGitSubcommandOptions = new(StringComparer.Ordinal)
    {
        "--output",
        "--ext-diff",
        "--textconv",
        "--exec"
    };

    /// <summary>
    /// Returns true when every segment of <paramref name="command"/> is a read-only observation.
    /// </summary>
    public static bool IsReadOnly(string? command, string? shell, out string reason)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            reason = "Read-only shell access requires a command that can be classified as read-only.";
            return false;
        }

        if (ContainsUnclassifiableShellSyntax(command, out reason))
            return false;

        // A chain of read-only observations stays read-only, so classify each segment
        // independently and require all of them to pass.
        var segments = SplitSegments(command);
        if (segments.Count == 0)
        {
            reason = "Read-only shell access requires a command that can be classified as read-only.";
            return false;
        }

        foreach (var segment in segments)
        {
            if (!IsReadOnlySegment(segment, shell, out reason))
                return false;
        }

        reason = "";
        return true;
    }

    private static bool IsReadOnlySegment(string segment, string? shell, out string reason)
    {
        var tokens = Tokenize(segment);
        if (tokens.Count == 0)
        {
            reason = "Read-only shell access requires a command that can be classified as read-only.";
            return false;
        }

        var executable = tokens[0];
        if (string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlyGit(tokens, out reason);

        if (IsPowerShellShell(shell) || PowerShellReadOnlyCommands.Contains(executable))
        {
            if (PowerShellReadOnlyCommands.Contains(executable))
            {
                reason = "";
                return true;
            }
        }

        if (UnixReadOnlyCommands.Contains(executable))
        {
            reason = "";
            return true;
        }

        if (string.Equals(executable, "sed", StringComparison.OrdinalIgnoreCase)
            && tokens.Skip(1).Any(t => string.Equals(t, "-n", StringComparison.Ordinal)))
        {
            reason = "";
            return true;
        }

        reason = $"Read-only shell access denied shell command '{executable}' because it is not in the read-only allow list.";
        return false;
    }

    private static bool IsReadOnlyGit(IReadOnlyList<string> tokens, out string reason)
    {
        // Skip benign global options such as `--no-pager` to find the subcommand, while
        // rejecting the ones that retarget the repository or inject configuration.
        var index = 1;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (!token.StartsWith('-'))
                break;

            var name = token.Split('=', 2)[0];
            if (UnsafeGitGlobalOptionsWithValue.Contains(name))
            {
                reason = $"Read-only shell access denied git because global option '{name}' can retarget the repository.";
                return false;
            }

            if (string.Equals(token, "-p", StringComparison.Ordinal)
                || string.Equals(token, "--paginate", StringComparison.Ordinal))
            {
                reason = "Read-only shell access denied git because pagination can block on interactive output.";
                return false;
            }

            index++;
        }

        if (index >= tokens.Count)
        {
            reason = "Read-only shell access allows only explicit read-only git subcommands.";
            return false;
        }

        var subcommand = tokens[index];
        var arguments = tokens.Skip(index + 1).ToList();

        var unsafeOption = arguments.FirstOrDefault(argument =>
            UnsafeGitSubcommandOptions.Contains(argument.Split('=', 2)[0]));
        if (unsafeOption != null)
        {
            reason = $"Read-only shell access denied git {subcommand} because option '{unsafeOption}' can run an external program.";
            return false;
        }

        if (ReadOnlyGitSubcommands.Contains(subcommand))
        {
            reason = "";
            return true;
        }

        if (subcommand == "branch"
            && arguments.Count == 1
            && string.Equals(arguments[0], "--show-current", StringComparison.Ordinal))
        {
            reason = "";
            return true;
        }

        reason = $"Read-only shell access denied git {subcommand} because only read-only git subcommands are allowed.";
        return false;
    }

    private static bool IsPowerShellShell(string? shell) =>
        !string.IsNullOrWhiteSpace(shell)
        && (shell.Contains("powershell", StringComparison.OrdinalIgnoreCase)
            || shell.Contains("pwsh", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Rejects syntax whose effect cannot be determined from the command text alone.
    /// Segment separators are handled by <see cref="SplitSegments"/> and are not rejected here.
    /// </summary>
    private static bool ContainsUnclassifiableShellSyntax(string command, out string reason)
    {
        var inSingle = false;
        var inDouble = false;
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;
            else if (!inSingle && !inDouble)
            {
                if (c is '<' or '>')
                {
                    reason = "Read-only shell access denies redirection because it can write files.";
                    return true;
                }

                // A lone `&` backgrounds the segment; `&&` is a supported separator.
                if (c == '&' && (i + 1 >= command.Length || command[i + 1] != '&')
                    && (i == 0 || command[i - 1] != '&'))
                {
                    reason = "Read-only shell access denies background execution.";
                    return true;
                }

                if (c == '`' || (c == '$' && i + 1 < command.Length && command[i + 1] == '('))
                {
                    reason = "Read-only shell access denies command substitution because the substituted command cannot be classified.";
                    return true;
                }
            }
        }

        if (inSingle || inDouble)
        {
            reason = "Read-only shell access denied the command because its quoting is unbalanced.";
            return true;
        }

        reason = "";
        return false;
    }

    /// <summary>
    /// Splits a command on <c>;</c>, <c>&amp;&amp;</c>, <c>||</c>, and <c>|</c> outside quotes.
    /// </summary>
    private static List<string> SplitSegments(string command)
    {
        var segments = new List<string>();
        var current = new List<char>();
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                current.Add(c);
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                current.Add(c);
                continue;
            }

            if (!inSingle && !inDouble)
            {
                if (c == ';')
                {
                    Flush();
                    continue;
                }

                if ((c == '&' || c == '|') && i + 1 < command.Length && command[i + 1] == c)
                {
                    Flush();
                    i++;
                    continue;
                }

                if (c == '|')
                {
                    Flush();
                    continue;
                }
            }

            current.Add(c);
        }

        Flush();
        return segments;

        void Flush()
        {
            var segment = new string(current.ToArray()).Trim();
            current.Clear();
            if (segment.Length > 0)
                segments.Add(segment);
        }
    }

    private static List<string> Tokenize(string command)
    {
        var tokens = new List<string>();
        var current = new List<char>();
        var inSingle = false;
        var inDouble = false;

        foreach (var c in command)
        {
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                continue;
            }

            if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingle && !inDouble)
            {
                Flush();
                continue;
            }

            current.Add(c);
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Count == 0)
                return;
            tokens.Add(new string(current.ToArray()));
            current.Clear();
        }
    }
}
