using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellApprovalKeyTests
{
    private static readonly string ShellPath = Path.Combine(Path.GetTempPath(), "shell-a");

    private static readonly string OtherShellPath = Path.Combine(Path.GetTempPath(), "shell-b");

    private static readonly string WorkingDirectory = Path.Combine(Path.GetTempPath(), "work");

    private static readonly string OtherWorkingDirectory = Path.Combine(Path.GetTempPath(), "other");

    [Fact]
    public void Create_SameWordsFromDifferentScriptText_ProducesTheSameKey()
    {
        var first = Key(script: "ls -la");
        var second = Key(script: "ls    -la  ");

        Assert.Equal(first, second);
        Assert.Equal(first.Hash, second.Hash);
    }

    [Fact]
    public void Create_DifferentShellExecutable_ProducesADifferentKey()
    {
        var first = Key();
        var second = Key(shellPath: OtherShellPath);

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Create_DifferentWorkingDirectory_ProducesADifferentKey()
    {
        Assert.NotEqual(Key().Hash, Key(workingDirectory: OtherWorkingDirectory).Hash);
    }

    [Fact]
    public void Create_DifferentPolicyFingerprint_ProducesADifferentKey()
    {
        Assert.NotEqual(Key().Hash, Key(fingerprint: "another-fingerprint").Hash);
    }

    [Fact]
    public void Create_TrailingSeparatorOnTheWorkingDirectory_DoesNotChangeTheKey()
    {
        var withSeparator = WorkingDirectory + Path.DirectorySeparatorChar;

        Assert.Equal(Key().Hash, Key(workingDirectory: withSeparator).Hash);
    }

    [Fact]
    public void Canonicalize_OpaqueScript_CarriesTheSentinelAndTheExactScript()
    {
        const string Script = "Get-ChildItem | ForEach-Object { $_.Name }";
        var lowering = LoweredScript.Opaque(ShellFamily.PowerShell, "pipeline is not plain");

        var canonical = ShellApprovalKey.Canonicalize(lowering, Script);

        Assert.Equal(new[] { ShellScriptSentinels.PowerShell, Script }, canonical.ToArray());
    }

    [Fact]
    public void Canonicalize_SeveralCommands_SeparatesThemWithAMarkerWord()
    {
        var lowering = LoweredScript.Plain(ShellFamily.Posix, [["cd", "src"], ["ls", "-la"]]);

        var canonical = ShellApprovalKey.Canonicalize(lowering, "cd src && ls -la");

        Assert.Equal(new[] { "cd", "src", "&&", "ls", "-la" }, canonical.ToArray());
    }

    private static ShellApprovalKey Key(
        string? shellPath = null,
        string? workingDirectory = null,
        string script = "ls -la",
        string fingerprint = "fingerprint")
    {
        var shell = new ShellIdentity(ShellKind.Bash, shellPath ?? ShellPath);
        var lowering = LoweredScript.Plain(ShellFamily.Posix, [["ls", "-la"]]);
        return ShellApprovalKey.Create(
            shell,
            workingDirectory ?? WorkingDirectory,
            lowering,
            script,
            fingerprint);
    }
}
