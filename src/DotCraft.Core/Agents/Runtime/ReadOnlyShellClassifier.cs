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

    /// <summary>
    /// <c>find</c> options that execute arbitrary commands, delete matches, or write pathnames
    /// to a file.
    /// </summary>
    private static readonly HashSet<string> UnsafeFindOptions = new(StringComparer.Ordinal)
    {
        "-exec",
        "-execdir",
        "-ok",
        "-okdir",
        "-delete",
        "-fls",
        "-fprint",
        "-fprint0",
        "-fprintf"
    };

    /// <summary>
    /// <c>rg</c> options that take a command to run for each match or for hostname lookup.
    /// </summary>
    private static readonly HashSet<string> UnsafeRipgrepOptionsWithValue = new(StringComparer.Ordinal)
    {
        "--pre",
        "--hostname-bin"
    };

    /// <summary>
    /// <c>rg</c> options that shell out to decompression tools.
    /// </summary>
    private static readonly HashSet<string> UnsafeRipgrepOptions = new(StringComparer.Ordinal)
    {
        "--search-zip",
        "-z"
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

        // The shell override becomes the launched executable, so honoring it would let a
        // read-only classification start an arbitrary program.
        if (!string.IsNullOrWhiteSpace(shell))
        {
            reason = "Read-only shell access denies the shell override because the selected executable cannot be classified.";
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
            if (!IsReadOnlySegment(segment, out reason))
                return false;
        }

        reason = "";
        return true;
    }

    private static bool IsReadOnlySegment(string segment, out string reason)
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

        if (PowerShellReadOnlyCommands.Contains(executable))
        {
            reason = "";
            return true;
        }

        if (string.Equals(executable, "find", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlyFind(tokens, out reason);

        if (string.Equals(executable, "rg", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlyRipgrep(tokens, out reason);

        if (string.Equals(executable, "sed", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlySed(tokens, out reason);

        if (UnixReadOnlyCommands.Contains(executable))
        {
            reason = "";
            return true;
        }

        reason = $"Read-only shell access denied shell command '{executable}' because it is not in the read-only allow list.";
        return false;
    }

    private static bool IsReadOnlyFind(IReadOnlyList<string> tokens, out string reason)
    {
        var unsafeOption = tokens.FirstOrDefault(UnsafeFindOptions.Contains);
        if (unsafeOption != null)
        {
            reason = $"Read-only shell access denied find because option '{unsafeOption}' can delete files or run an external program.";
            return false;
        }

        reason = "";
        return true;
    }

    private static bool IsReadOnlyRipgrep(IReadOnlyList<string> tokens, out string reason)
    {
        var unsafeOption = tokens.FirstOrDefault(token =>
            UnsafeRipgrepOptions.Contains(token)
            || UnsafeRipgrepOptionsWithValue.Contains(token.Split('=', 2)[0]));
        if (unsafeOption != null)
        {
            reason = $"Read-only shell access denied rg because option '{unsafeOption}' can run an external program.";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>
    /// Admits only <c>sed -n &lt;N|M,N&gt;p &lt;file&gt;</c>, which prints selected lines and
    /// cannot write.
    /// </summary>
    private static bool IsReadOnlySed(IReadOnlyList<string> tokens, out string reason)
    {
        if (tokens.Count <= 4
            && tokens.Count >= 3
            && string.Equals(tokens[1], "-n", StringComparison.Ordinal)
            && IsSedPrintRange(tokens[2]))
        {
            reason = "";
            return true;
        }

        reason = "Read-only shell access denied sed because only 'sed -n <N|M,N>p' is allowed.";
        return false;
    }

    private static bool IsSedPrintRange(string argument)
    {
        if (!argument.EndsWith('p'))
            return false;

        var parts = argument[..^1].Split(',');
        return parts.Length is 1 or 2
               && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
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
                // An unquoted newline separates commands in both Bash and PowerShell.
                if (c is ';' or '\r' or '\n')
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
