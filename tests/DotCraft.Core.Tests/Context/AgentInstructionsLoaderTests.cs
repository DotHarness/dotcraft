using DotCraft.Configuration;
using DotCraft.Context;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class AgentInstructionsLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-agents-{Guid.NewGuid():N}");

    public AgentInstructionsLoaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Load_CombinesUserThenProjectRootToCwd()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.md"), "user rules");
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "root rules");
        var cwd = Directory.CreateDirectory(Path.Combine(repo, "src", "feature")).FullName;
        File.WriteAllText(Path.Combine(repo, "src", "AGENTS.md"), "src rules");

        var result = Load(cwd, userRoot);

        Assert.Equal(
            [
                Path.Combine(userRoot, "AGENTS.md"),
                Path.Combine(repo, "AGENTS.md"),
                Path.Combine(repo, "src", "AGENTS.md")
            ],
            result.Sources);
        Assert.True(result.Content.IndexOf("user rules", StringComparison.Ordinal)
                    < result.Content.IndexOf("root rules", StringComparison.Ordinal));
        Assert.True(result.Content.IndexOf("root rules", StringComparison.Ordinal)
                    < result.Content.IndexOf("src rules", StringComparison.Ordinal));
        Assert.Contains("--- project-doc ---", result.Content);
        Assert.Contains("<INSTRUCTIONS>", result.Content);
    }

    [Fact]
    public void Load_ProjectEmptyOverrideShadowsDefaultWithoutSource()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.override.md"), "   \r\n");
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "must not load");

        var result = Load(repo, userRoot);

        Assert.Empty(result.Sources);
        Assert.Empty(result.Content);
    }

    [Fact]
    public void Load_ProjectOverrideDirectoryFallsBackToDefaultFile()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(Path.Combine(repo, "AGENTS.override.md"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "default project rules");

        var result = Load(repo, userRoot);

        Assert.Equal([Path.Combine(repo, "AGENTS.md")], result.Sources);
        Assert.Contains("default project rules", result.Content);
    }

    [Fact]
    public void Load_UserEmptyOverrideFallsBackToDefault()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.override.md"), "\n");
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.md"), "default user rules");
        var cwd = Directory.CreateDirectory(Path.Combine(_root, "plain")).FullName;

        var result = Load(cwd, userRoot);

        Assert.Equal([Path.Combine(userRoot, "AGENTS.md")], result.Sources);
        Assert.Contains("default user rules", result.Content);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Load_NoGitMarkerChecksOnlyCwd()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        File.WriteAllText(Path.Combine(_root, "AGENTS.md"), "ancestor rules");
        var cwd = Directory.CreateDirectory(Path.Combine(_root, "plain", "cwd")).FullName;
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "cwd rules");

        var result = Load(cwd, userRoot);

        Assert.Equal([Path.Combine(cwd, "AGENTS.md")], result.Sources);
        Assert.DoesNotContain("ancestor rules", result.Content);
    }

    [Fact]
    public void Load_ProjectBudgetTruncatesRawBytesBeforeLossyUtf8Decode()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: elsewhere");
        File.WriteAllBytes(Path.Combine(repo, "AGENTS.md"), [0x41, 0xF0, 0x9F, 0x92, 0xA9]);

        var result = Load(repo, userRoot, projectDocMaxBytes: 3);

        Assert.True(result.IsTruncated);
        Assert.Contains("A�", result.Content);
        Assert.Single(result.Sources);
    }

    [Fact]
    public void Load_WhitespaceProjectFileConsumesSharedRawByteBudget()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var cwd = Directory.CreateDirectory(Path.Combine(repo, "child")).FullName;
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "    ");
        File.WriteAllText(Path.Combine(cwd, "AGENTS.md"), "child rules");

        var result = Load(cwd, userRoot, projectDocMaxBytes: 4);

        Assert.Empty(result.Sources);
        Assert.Empty(result.Content);
    }

    [Fact]
    public void Load_ZeroBudgetDisablesOnlyProjectFiles()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.md"), "user rules");
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "project rules");

        var result = Load(repo, userRoot, projectDocMaxBytes: 0);

        Assert.Equal([Path.Combine(userRoot, "AGENTS.md")], result.Sources);
        Assert.Contains("user rules", result.Content);
        Assert.DoesNotContain("project rules", result.Content);
    }

    [Fact]
    public void Load_UserFileIsNotLimitedByProjectBudget()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var userRules = new string('u', 128);
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.md"), userRules);
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "project");

        var result = Load(repo, userRoot, projectDocMaxBytes: 1);

        Assert.Contains(userRules, result.Content);
        Assert.Contains("p", result.Content);
        Assert.Equal(2, result.Sources.Count);
    }

    [Fact]
    public void Load_ProjectReadFailureDiscardsTheWholeProjectChainButKeepsUserRules()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        File.WriteAllText(Path.Combine(userRoot, "AGENTS.md"), "user rules");
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "root project rules");
        var cwd = Directory.CreateDirectory(Path.Combine(repo, "src")).FullName;
        var lockedPath = Path.Combine(cwd, "AGENTS.md");
        File.WriteAllText(lockedPath, "locked project rules");
        using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = Load(cwd, userRoot);

        Assert.Equal([Path.Combine(userRoot, "AGENTS.md")], result.Sources);
        Assert.Contains("user rules", result.Content);
        Assert.DoesNotContain("root project rules", result.Content);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Load_SymlinkSourceKeepsLogicalPath()
    {
        var userRoot = Directory.CreateDirectory(Path.Combine(_root, "home", ".craft")).FullName;
        var repo = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var target = Path.Combine(_root, "shared-agents.md");
        File.WriteAllText(target, "linked rules");
        var logical = Path.Combine(repo, "AGENTS.md");
        try
        {
            File.CreateSymbolicLink(logical, target);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        var result = Load(repo, userRoot);

        Assert.Equal([logical], result.Sources);
        Assert.Contains("linked rules", result.Content);
        Assert.DoesNotContain(target, result.Sources);
    }

    private static AgentInstructionsLoadResult Load(
        string cwd,
        string userRoot,
        int projectDocMaxBytes = 32768) =>
        new AgentInstructionsLoader().Load(cwd, new AppConfig
        {
            GlobalConfigPath = Path.Combine(userRoot, "config.json"),
            ProjectDocMaxBytes = projectDocMaxBytes
        });

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test-only files.
        }
    }
}
