namespace DotCraft.Security.ShellCommands;

public static class ReadOnlyCommandClassifier
{
    private const string Prefix = "Read-only shell access ";

    private static readonly PosixScriptLowerer Posix = new();

    private static readonly PowerShellScriptLowerer PowerShell = new();

    private static readonly HashSet<string> PowerShellReadOnlyCmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-ChildItem",
        "Get-Content",
        "Get-Item",
        "Get-ItemProperty",
        "Get-ItemPropertyValue",
        "Get-FileHash",
        "Get-Acl",
        "Get-Location",
        "Test-Path",
        "Resolve-Path",
        "Convert-Path",
        "Join-Path",
        "Split-Path",
        "Select-String",
        "Select-Object",
        "Sort-Object",
        "Group-Object",
        "Measure-Object",
        "Compare-Object",
        "Get-Unique",
        "Format-Table",
        "Format-List",
        "Format-Wide",
        "Format-Hex",
        "Out-String",
        "ConvertTo-Json",
        "ConvertFrom-Json",
        "ConvertTo-Csv",
        "ConvertFrom-Csv",
        "Write-Output",
        "Get-Date",
        "Get-Process",
        "Get-Service",
        "Get-Module",
        "Get-Alias",
        "Get-Culture",
        "Get-TimeZone",
        "Get-Host",
        "Get-PSDrive"
    };

    // Aliases that collide with native executables on PowerShell 7 (sort, sc, curl, wget) are left out,
    // because the native program would run instead of the cmdlet.
    private static readonly Dictionary<string, string> PowerShellAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gci"] = "Get-ChildItem",
        ["dir"] = "Get-ChildItem",
        ["ls"] = "Get-ChildItem",
        ["gc"] = "Get-Content",
        ["cat"] = "Get-Content",
        ["type"] = "Get-Content",
        ["gi"] = "Get-Item",
        ["gp"] = "Get-ItemProperty",
        ["gpv"] = "Get-ItemPropertyValue",
        ["gl"] = "Get-Location",
        ["pwd"] = "Get-Location",
        ["rvpa"] = "Resolve-Path",
        ["cvpa"] = "Convert-Path",
        ["sls"] = "Select-String",
        ["select"] = "Select-Object",
        ["group"] = "Group-Object",
        ["measure"] = "Measure-Object",
        ["compare"] = "Compare-Object",
        ["gu"] = "Get-Unique",
        ["ft"] = "Format-Table",
        ["fl"] = "Format-List",
        ["fw"] = "Format-Wide",
        ["fhx"] = "Format-Hex",
        ["echo"] = "Write-Output",
        ["write"] = "Write-Output",
        ["gps"] = "Get-Process",
        ["ps"] = "Get-Process",
        ["gsv"] = "Get-Service",
        ["gmo"] = "Get-Module",
        ["gal"] = "Get-Alias",
        ["gdr"] = "Get-PSDrive"
    };

    private static readonly HashSet<string> UnixReadOnlyCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ls",
        "find",
        "grep",
        "rg"
    };

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

    private static readonly HashSet<string> UnsafeRipgrepOptionsWithValue = new(StringComparer.Ordinal)
    {
        "--pre",
        "--hostname-bin"
    };

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

    private static readonly HashSet<string> UnsafeGitSubcommandOptions = new(StringComparer.Ordinal)
    {
        "--output",
        "--ext-diff",
        "--textconv",
        "--exec"
    };

    /// <summary>The family of the host's default shell, or null when it is neither PowerShell nor POSIX.</summary>
    public static ShellFamily? HostShellFamily { get; } =
        ShellIdentityResolver.Host.TryResolve(null, out var identity, out _)
        && identity.Kind.ToFamily() is ShellFamily.PowerShell or ShellFamily.Posix
            ? identity.Kind.ToFamily()
            : null;

    public static bool IsReadOnly(string? command, string? shell, out string reason) =>
        IsReadOnly(command, shell, targetFamily: null, out reason);

    /// <summary>
    /// Classifies under the target shell's grammar only; without a known target the command must be plain under both.
    /// </summary>
    public static bool IsReadOnly(string? command, string? shell, ShellFamily? targetFamily, out string reason)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            reason = Prefix + "requires a command that can be classified as read-only.";
            return false;
        }

        // The override is the launched executable; honoring it would start an arbitrary program.
        if (!string.IsNullOrWhiteSpace(shell))
        {
            reason = Prefix + "denies the shell override because the selected executable cannot be classified.";
            return false;
        }

        var commands = new List<(ShellFamily Family, IReadOnlyList<string> Words)>();
        if (targetFamily != ShellFamily.PowerShell)
        {
            var posix = Posix.Lower(command);
            if (!posix.IsPlain)
            {
                reason = Prefix + posix.PlainRejectReason;
                return false;
            }
            commands.AddRange(posix.PlainCommands!.Select(static words => (ShellFamily.Posix, words)));
        }

        if (targetFamily != ShellFamily.Posix)
        {
            var powerShell = PowerShell.Lower(command);
            if (!powerShell.IsPlain)
            {
                reason = Prefix + "denies the command because it is not a plain command under PowerShell: "
                    + powerShell.PlainRejectReason;
                return false;
            }
            commands.AddRange(powerShell.PlainCommands!.Select(static words => (ShellFamily.PowerShell, words)));
        }

        foreach (var (family, words) in commands)
        {
            if (!IsReadOnlySegment(family, words, out reason))
                return false;
        }

        reason = "";
        return true;
    }

    private static bool IsReadOnlySegment(ShellFamily family, IReadOnlyList<string> tokens, out string reason)
    {
        if (tokens.Count == 0)
        {
            reason = Prefix + "requires a command that can be classified as read-only.";
            return false;
        }

        var executable = tokens[0];
        if (string.Equals(executable, "git", StringComparison.OrdinalIgnoreCase))
            return IsReadOnlyGit(tokens, out reason);

        if (family == ShellFamily.PowerShell
            && PowerShellReadOnlyCmdlets.Contains(PowerShellAliases.GetValueOrDefault(executable, executable)))
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

        reason = Prefix + $"denied shell command '{executable}' because it is not in the read-only allow list.";
        return false;
    }

    private static bool IsReadOnlyFind(IReadOnlyList<string> tokens, out string reason)
    {
        var unsafeOption = tokens.FirstOrDefault(UnsafeFindOptions.Contains);
        if (unsafeOption != null)
        {
            reason = Prefix + $"denied find because option '{unsafeOption}' can delete files or run an external program.";
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
            reason = Prefix + $"denied rg because option '{unsafeOption}' can run an external program.";
            return false;
        }

        reason = "";
        return true;
    }

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

        reason = Prefix + "denied sed because only 'sed -n <N|M,N>p' is allowed.";
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
        var index = 1;
        while (index < tokens.Count)
        {
            var token = tokens[index];
            if (!token.StartsWith('-'))
                break;

            var name = token.Split('=', 2)[0];
            if (UnsafeGitGlobalOptionsWithValue.Contains(name))
            {
                reason = Prefix + $"denied git because global option '{name}' can retarget the repository.";
                return false;
            }

            if (string.Equals(token, "-p", StringComparison.Ordinal)
                || string.Equals(token, "--paginate", StringComparison.Ordinal))
            {
                reason = Prefix + "denied git because pagination can block on interactive output.";
                return false;
            }

            index++;
        }

        if (index >= tokens.Count)
        {
            reason = Prefix + "allows only explicit read-only git subcommands.";
            return false;
        }

        var subcommand = tokens[index];
        var arguments = tokens.Skip(index + 1).ToList();

        var unsafeOption = arguments.FirstOrDefault(argument =>
            UnsafeGitSubcommandOptions.Contains(argument.Split('=', 2)[0]));
        if (unsafeOption != null)
        {
            reason = Prefix + $"denied git {subcommand} because option '{unsafeOption}' can run an external program.";
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

        reason = Prefix + $"denied git {subcommand} because only read-only git subcommands are allowed.";
        return false;
    }
}
