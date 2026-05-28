using DotCraft.CLI;

namespace DotCraft.Tests.CLI;

public sealed class SkillCliRunnerTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillcli-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_VerifyJson_ReturnsSuccess()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(craftPath);
        var candidate = WriteCandidate("demo-skill");
        var args = CommandLineArgs.Parse(["skill", "verify", "--candidate", candidate, "--json"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SkillCliRunner.RunAsync(craftPath, args, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"isValid\": true", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallWithoutWorkspace_ReturnsJsonError()
    {
        var candidate = WriteCandidate("demo-skill");
        var args = CommandLineArgs.Parse(["skill", "install", "--candidate", candidate, "--json"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SkillCliRunner.RunAsync(Path.Combine(_tempRoot, ".craft"), args, output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"success\": false", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private string WriteCandidate(string name)
    {
        var candidate = Path.Combine(_tempRoot, "candidate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(candidate);
        File.WriteAllText(
            Path.Combine(candidate, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: Test skill
            ---

            # {name}

            Follow these steps.
            """);
        return candidate;
    }
}
