using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class LearnedShellRuleStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"shell_rules_{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_root, "security", "shell-rules.json");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Load_WithoutAFile_ReturnsNoRules() => Assert.Empty(new LearnedShellRuleStore(FilePath).Load());

    [Fact]
    public void Append_CreatesTheDirectoryAndStoresTheRule()
    {
        var rule = new ShellPrefixRule(["git", "status"], ShellDecision.Allow, "reads the working tree");

        new LearnedShellRuleStore(FilePath).Append(rule);

        Assert.True(File.Exists(FilePath));
        Assert.Equal(rule, Assert.Single(new LearnedShellRuleStore(FilePath).Load()));
    }

    [Fact]
    public void Append_EqualRuleTwice_StoresItOnce()
    {
        var store = new LearnedShellRuleStore(FilePath);

        store.Append(new ShellPrefixRule(["ls"], ShellDecision.Allow));
        store.Append(new ShellPrefixRule(["ls"], ShellDecision.Allow));

        Assert.Single(store.Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNoRulesAndLeavesTheFile()
    {
        Write("{ not json");

        Assert.Empty(new LearnedShellRuleStore(FilePath).Load());
        Assert.Equal("{ not json", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Load_ForeignSchemaVersion_ReturnsNoRules()
    {
        Write("""{ "schemaVersion": 2, "rules": [ { "prefix": ["ls"], "decision": "allow" } ] }""");

        Assert.Empty(new LearnedShellRuleStore(FilePath).Load());
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);
    }
}
