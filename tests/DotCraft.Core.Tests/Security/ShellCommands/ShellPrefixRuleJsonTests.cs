using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellPrefixRuleJsonTests
{
    [Fact]
    public void Read_ParsesPrefixDecisionAndJustification()
    {
        var rules = ShellPrefixRuleJson.Read(Parse(
            """
            [
              { "prefix": ["git", "push"], "decision": "prompt", "justification": "pushes leave the machine" },
              { "prefix": ["ls"], "decision": "allow" }
            ]
            """));

        Assert.Equal(2, rules.Count);
        Assert.Equal(new[] { "git", "push" }, rules[0].Prefix.ToArray());
        Assert.Equal(ShellDecision.Prompt, rules[0].Decision);
        Assert.Equal("pushes leave the machine", rules[0].Justification);
        Assert.Equal(ShellDecision.Allow, rules[1].Decision);
        Assert.Null(rules[1].Justification);
    }

    [Fact]
    public void Write_ThenRead_RoundTripsTheRule()
    {
        var rule = new ShellPrefixRule(["git", "push"], ShellDecision.Forbidden, "pushes leave the machine");

        var array = new JsonArray { ShellPrefixRuleJson.Write(rule) };
        var rules = ShellPrefixRuleJson.Read(Parse(array.ToJsonString()));

        Assert.Equal(rule, Assert.Single(rules));
    }

    [Fact]
    public void Read_UnknownDecision_ThrowsNamingTheIndex()
    {
        var element = Parse(
            """[{ "prefix": ["ls"], "decision": "allow" }, { "prefix": ["rm"], "decision": "deny" }]""");

        var error = Assert.Throws<ArgumentException>(() => ShellPrefixRuleJson.Read(element));

        Assert.Contains("index 1", error.Message, StringComparison.Ordinal);
        Assert.Contains("deny", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_MissingOrEmptyPrefix_ThrowsNamingTheIndex()
    {
        var missing = Assert.Throws<ArgumentException>(
            () => ShellPrefixRuleJson.Read(Parse("""[{ "decision": "allow" }]""")));
        Assert.Contains("index 0", missing.Message, StringComparison.Ordinal);

        var empty = Assert.Throws<ArgumentException>(
            () => ShellPrefixRuleJson.Read(Parse("""[{ "prefix": ["ls"], "decision": "allow" }, { "prefix": [], "decision": "allow" }]""")));
        Assert.Contains("index 1", empty.Message, StringComparison.Ordinal);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
