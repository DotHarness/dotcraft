namespace DotCraft.Security.ShellCommands;

public enum ShellDecision
{
    Allow = 0,
    Prompt = 1,
    Forbidden = 2
}

public sealed record ShellPrefixRule
{
    public ShellPrefixRule(IReadOnlyList<string> prefix, ShellDecision decision, string? justification = null)
    {
        if (prefix.Count == 0 || prefix.Any(string.IsNullOrEmpty))
            throw new ArgumentException("A prefix rule needs at least one non-empty word.", nameof(prefix));
        Prefix = [.. prefix];
        Decision = decision;
        Justification = string.IsNullOrWhiteSpace(justification) ? null : justification;
    }

    public IReadOnlyList<string> Prefix { get; }

    public ShellDecision Decision { get; }

    public string? Justification { get; }

    public bool Equals(ShellPrefixRule? other) =>
        other is not null
        && Decision == other.Decision
        && string.Equals(Justification, other.Justification, StringComparison.Ordinal)
        && Prefix.SequenceEqual(other.Prefix, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Decision);
        hash.Add(Justification);
        foreach (var word in Prefix)
            hash.Add(word, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public abstract record ShellRuleMatch(IReadOnlyList<string> Command, ShellDecision Decision);

public sealed record ShellPrefixRuleMatch(IReadOnlyList<string> Command, ShellPrefixRule Rule)
    : ShellRuleMatch(Command, Rule.Decision);

public sealed record ShellFallbackMatch(IReadOnlyList<string> Command, ShellDecision Decision, string Reason, bool Dangerous = false)
    : ShellRuleMatch(Command, Decision);
