using System.Security.Cryptography;
using System.Text;

namespace DotCraft.Security.ShellCommands;

public sealed class ShellPolicy
{
    private const char FieldSeparator = '\u001f';

    private static readonly string[][] BannedPrefixes = BuildBannedPrefixes();

    private readonly ShellPrefixRule[] _rules;

    private readonly Dictionary<string, List<ShellPrefixRule>> _posixIndex = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<ShellPrefixRule>> _windowsIndex = new(StringComparer.Ordinal);

    public ShellPolicy(IEnumerable<ShellPrefixRule> rules)
    {
        var unique = new HashSet<ShellPrefixRule>();
        var ordered = new List<ShellPrefixRule>();
        foreach (var rule in rules)
        {
            if (unique.Add(rule))
                ordered.Add(rule);
        }

        _rules = [.. ordered];
        foreach (var rule in _rules)
        {
            AddToIndex(_posixIndex, rule.Prefix[0], rule);
            AddToIndex(_windowsIndex, ShellIdentityResolver.ExecutableName(rule.Prefix[0], isWindows: true), rule);
        }

        Fingerprint = ComputeFingerprint(_rules);
    }

    public static ShellPolicy Empty { get; } = new([]);

    public IReadOnlyList<ShellPrefixRule> Rules => _rules;

    public string Fingerprint { get; }

    public IReadOnlyList<ShellPrefixRuleMatch> Match(IReadOnlyList<string> command, CommandPlatform platform)
    {
        if (command.Count == 0 || _rules.Length == 0)
            return [];

        var windows = platform == CommandPlatform.Windows;
        var index = windows ? _windowsIndex : _posixIndex;
        var firstWord = windows ? ShellIdentityResolver.ExecutableName(command[0], isWindows: true) : command[0];
        var matches = Collect(index, firstWord, command);
        if (matches.Count > 0)
            return matches;

        var fileName = ShellIdentityResolver.ExecutableName(command[0], windows);
        return string.Equals(fileName, firstWord, StringComparison.Ordinal)
            ? matches
            : Collect(index, fileName, command);
    }

    public static bool IsBannedPrefix(IReadOnlyList<string> prefix, CommandPlatform platform)
    {
        var comparer = platform == CommandPlatform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        return BannedPrefixes.Any(
            banned => banned.Length == prefix.Count && banned.SequenceEqual(prefix, comparer));
    }

    private static List<ShellPrefixRuleMatch> Collect(
        Dictionary<string, List<ShellPrefixRule>> index,
        string firstWord,
        IReadOnlyList<string> command)
    {
        var matches = new List<ShellPrefixRuleMatch>();
        if (!index.TryGetValue(firstWord, out var candidates))
            return matches;

        foreach (var rule in candidates)
        {
            if (rule.Prefix.Count > command.Count)
                continue;

            var matched = true;
            for (var i = 1; i < rule.Prefix.Count; i++)
            {
                if (!string.Equals(rule.Prefix[i], command[i], StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
                matches.Add(new ShellPrefixRuleMatch(command, rule));
        }

        return matches;
    }

    private static void AddToIndex(
        Dictionary<string, List<ShellPrefixRule>> index,
        string firstWord,
        ShellPrefixRule rule)
    {
        if (!index.TryGetValue(firstWord, out var bucket))
            index[firstWord] = bucket = [];

        bucket.Add(rule);
    }

    private static string ComputeFingerprint(IReadOnlyList<ShellPrefixRule> rules)
    {
        var texts = new string[rules.Count];
        for (var i = 0; i < rules.Count; i++)
            texts[i] = CanonicalText(rules[i]);
        Array.Sort(texts, StringComparer.Ordinal);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', texts)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string CanonicalText(ShellPrefixRule rule)
    {
        var builder = new StringBuilder();
        builder.Append(rule.Decision).Append(FieldSeparator).Append(rule.Justification);
        foreach (var word in rule.Prefix)
            builder.Append(FieldSeparator).Append(word);
        return builder.ToString();
    }

    private static string[][] BuildBannedPrefixes()
    {
        string[] powerShellHosts = ["powershell", "powershell.exe", "pwsh", "pwsh.exe"];
        string[] powerShellInlineFlags = ["-Command", "-c", "-EncodedCommand", "-e", "-ec", "-File"];

        List<string[]> prefixes =
        [
            ["bash"], ["bash", "-c"], ["bash", "-lc"],
            ["sh"], ["sh", "-c"],
            ["zsh"], ["zsh", "-c"], ["zsh", "-lc"],
            ["dash"], ["fish"],
            ["cmd"], ["cmd", "/c"], ["cmd", "/k"],
            ["cmd.exe"], ["cmd.exe", "/c"],
            ["python"], ["python", "-c"],
            ["python3"], ["python3", "-c"],
            ["py", "-c"],
            ["node"], ["node", "-e"], ["node", "--eval"],
            ["perl", "-e"], ["ruby", "-e"],
            ["deno", "eval"], ["deno", "run"],
            ["bun", "-e"], ["php", "-r"],
            ["env"], ["sudo"], ["doas"], ["xargs"], ["nohup"], ["timeout"], ["nice"], ["busybox"],
            ["eval"], ["exec"], ["source"],
            ["rm"], ["rm", "-rf"], ["rm", "-r"], ["rm", "-f"],
            ["del"], ["erase"], ["rd"], ["rmdir"], ["Remove-Item"], ["ri"],
            ["git"], ["npm", "run"], ["npx"], ["yarn", "run"], ["pnpm", "run"], ["dotnet", "run"], ["make"],
            ["curl"], ["wget"], ["ssh"], ["scp"], ["chmod"], ["chown"], ["dd"], ["mkfs"], ["format"],
            ["reg"], ["regedit"], ["schtasks"], ["sc"], ["net"],
            ["Set-ExecutionPolicy"], ["Invoke-Expression"], ["iex"],
            ["Start-Process"], ["saps"],
            ["Invoke-WebRequest"], ["iwr"], ["Invoke-RestMethod"], ["irm"]
        ];

        foreach (var host in powerShellHosts)
        {
            prefixes.Add([host]);
            foreach (var flag in powerShellInlineFlags)
                prefixes.Add([host, flag]);
        }

        return [.. prefixes];
    }
}
