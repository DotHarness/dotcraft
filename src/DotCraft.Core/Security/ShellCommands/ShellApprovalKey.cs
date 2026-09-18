using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DotCraft.Security.ShellCommands;

public sealed record ShellApprovalKey(
    ShellKind ShellKind,
    string ShellExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> CanonicalCommand,
    string PolicyFingerprint)
{
    private const string CommandSeparator = "&&";

    public string Hash => ComputeHash();

    public static ShellApprovalKey Create(
        ShellIdentity shell,
        string workingDirectory,
        LoweredScript lowering,
        string script,
        string policyFingerprint)
    {
        var onWindows = OperatingSystem.IsWindows();
        var foldDirectoryCase = onWindows || shell.Family is ShellFamily.PowerShell or ShellFamily.Cmd;
        var directory = TrimTrailingSeparators(Path.GetFullPath(workingDirectory));
        var executable = Path.GetFullPath(shell.ExecutablePath);

        return new ShellApprovalKey(
            shell.Kind,
            onWindows ? executable.ToLowerInvariant() : executable,
            foldDirectoryCase ? directory.ToLowerInvariant() : directory,
            Canonicalize(lowering, script),
            policyFingerprint);
    }

    public static IReadOnlyList<string> Canonicalize(LoweredScript lowering, string script)
    {
        if (lowering.PlainCommands is not { } commands)
            return [ShellScriptSentinels.For(lowering.Family), script];

        if (commands.Count == 1)
            return [.. commands[0]];

        var words = new List<string>();
        foreach (var command in commands)
        {
            if (words.Count > 0)
                words.Add(CommandSeparator);
            words.AddRange(command);
        }

        return words;
    }

    public bool Equals(ShellApprovalKey? other) =>
        other is not null
        && ShellKind == other.ShellKind
        && string.Equals(ShellExecutablePath, other.ShellExecutablePath, StringComparison.Ordinal)
        && string.Equals(WorkingDirectory, other.WorkingDirectory, StringComparison.Ordinal)
        && string.Equals(PolicyFingerprint, other.PolicyFingerprint, StringComparison.Ordinal)
        && CanonicalCommand.SequenceEqual(other.CanonicalCommand, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ShellKind);
        hash.Add(ShellExecutablePath, StringComparer.Ordinal);
        hash.Add(WorkingDirectory, StringComparer.Ordinal);
        hash.Add(PolicyFingerprint, StringComparer.Ordinal);
        foreach (var word in CanonicalCommand)
            hash.Add(word, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private string ComputeHash()
    {
        var command = new JsonArray();
        foreach (var word in CanonicalCommand)
            command.Add(word);

        var canonical = new JsonObject
        {
            ["shellKind"] = ShellKind.ToString(),
            ["shellExecutablePath"] = ShellExecutablePath,
            ["workingDirectory"] = WorkingDirectory,
            ["canonicalCommand"] = command,
            ["policyFingerprint"] = PolicyFingerprint
        };

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToJsonString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string TrimTrailingSeparators(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':') ? path : trimmed;
    }
}
