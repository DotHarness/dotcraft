using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellPolicyTests
{
    [Fact]
    public void Match_ExactCommand_ReturnsTheRule()
    {
        var rule = Rule(ShellDecision.Prompt, "git", "push");
        var policy = new ShellPolicy([rule]);

        var matches = policy.Match(["git", "push"], CommandPlatform.Posix);

        var match = Assert.Single(matches);
        Assert.Same(rule, match.Rule);
        Assert.Equal(ShellDecision.Prompt, match.Decision);
    }

    [Fact]
    public void Match_LongerCommand_MatchesTheShorterPrefix()
    {
        var policy = new ShellPolicy([Rule(ShellDecision.Allow, "git", "status")]);

        Assert.Single(policy.Match(["git", "status", "--short"], CommandPlatform.Posix));
    }

    [Fact]
    public void Match_ShorterCommand_DoesNotMatchTheLongerPrefix()
    {
        var policy = new ShellPolicy([Rule(ShellDecision.Allow, "git", "status")]);

        Assert.Empty(policy.Match(["git"], CommandPlatform.Posix));
    }

    [Fact]
    public void Match_ReturnsEveryMatchingRuleInRuleOrder()
    {
        var broad = Rule(ShellDecision.Prompt, "git");
        var narrow = Rule(ShellDecision.Allow, "git", "status");
        var policy = new ShellPolicy([broad, narrow]);

        var matches = policy.Match(["git", "status"], CommandPlatform.Posix);

        Assert.Equal(new[] { broad, narrow }, matches.Select(match => match.Rule).ToArray());
    }

    [Fact]
    public void Match_OnWindows_NormalizesTheFirstWordOnly()
    {
        var policy = new ShellPolicy([Rule(ShellDecision.Prompt, "git", "status")]);

        Assert.Single(policy.Match(["Git.exe", "status"], CommandPlatform.Windows));
        Assert.Empty(policy.Match(["Git.exe", "status"], CommandPlatform.Posix));
        Assert.Empty(policy.Match(["git", "STATUS"], CommandPlatform.Windows));
    }

    [Fact]
    public void Match_PathFirstWord_FallsBackToTheFileName()
    {
        var policy = new ShellPolicy([Rule(ShellDecision.Prompt, "git", "status")]);

        Assert.Single(policy.Match(["/usr/bin/git", "status"], CommandPlatform.Posix));
        Assert.Empty(policy.Match(["/usr/bin/git"], CommandPlatform.Posix));
    }

    [Fact]
    public void Match_WhenTheExactFirstWordMatched_SkipsTheFileNameFallback()
    {
        var exact = Rule(ShellDecision.Allow, "/usr/bin/git", "status");
        var byFileName = Rule(ShellDecision.Forbidden, "git");
        var policy = new ShellPolicy([exact, byFileName]);

        var matches = policy.Match(["/usr/bin/git", "status"], CommandPlatform.Posix);

        Assert.Same(exact, Assert.Single(matches).Rule);
    }

    [Fact]
    public void Constructor_KeepsRuleOrderAndDropsEqualRules()
    {
        var push = Rule(ShellDecision.Prompt, "git", "push");
        var list = Rule(ShellDecision.Allow, "ls");
        var policy = new ShellPolicy([push, list, Rule(ShellDecision.Prompt, "git", "push")]);

        Assert.Equal(new[] { push, list }, policy.Rules.ToArray());
    }

    [Fact]
    public void Fingerprint_IgnoresRuleOrder()
    {
        var list = Rule(ShellDecision.Allow, "ls");
        var push = Rule(ShellDecision.Prompt, "git", "push");

        Assert.Equal(new ShellPolicy([list, push]).Fingerprint, new ShellPolicy([push, list]).Fingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesWhenARuleIsAdded()
    {
        var policy = new ShellPolicy([Rule(ShellDecision.Allow, "ls")]);

        var extended = new ShellPolicy([.. policy.Rules, Rule(ShellDecision.Prompt, "git", "push")]);

        Assert.NotEqual(policy.Fingerprint, extended.Fingerprint);
    }

    [Fact]
    public void IsBannedPrefix_MatchesWholeEntriesOnly()
    {
        Assert.True(ShellPolicy.IsBannedPrefix(["git"], CommandPlatform.Posix));
        Assert.False(ShellPolicy.IsBannedPrefix(["git", "status"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["rm", "-rf"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["npm", "run"], CommandPlatform.Posix));
        Assert.False(ShellPolicy.IsBannedPrefix(["npm", "run", "build"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["powershell", "-EncodedCommand"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["pwsh.exe", "-File"], CommandPlatform.Posix));
        Assert.False(ShellPolicy.IsBannedPrefix(["dotnet", "build"], CommandPlatform.Posix));
        Assert.False(ShellPolicy.IsBannedPrefix([], CommandPlatform.Posix));
    }

    [Fact]
    public void IsBannedPrefix_IgnoresCaseOnlyOnWindows()
    {
        Assert.True(ShellPolicy.IsBannedPrefix(["RM"], CommandPlatform.Windows));
        Assert.False(ShellPolicy.IsBannedPrefix(["RM"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["remove-item"], CommandPlatform.Windows));
        Assert.False(ShellPolicy.IsBannedPrefix(["remove-item"], CommandPlatform.Posix));
        Assert.True(ShellPolicy.IsBannedPrefix(["Remove-Item"], CommandPlatform.Posix));
    }

    private static ShellPrefixRule Rule(ShellDecision decision, params string[] prefix) => new(prefix, decision);
}
