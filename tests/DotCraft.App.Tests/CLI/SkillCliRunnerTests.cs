using DotCraft.CLI;
using Xunit;

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
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SkillCliRunner.VerifyAsync(
            craftPath, candidate, null, true, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"isValid\": true", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_InstallWithoutWorkspace_ReturnsJsonError()
    {
        var candidate = WriteCandidate("demo-skill");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await SkillCliRunner.InstallAsync(
            Path.Combine(_tempRoot, ".craft"), candidate, null, null, false, true, output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"success\": false", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
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
