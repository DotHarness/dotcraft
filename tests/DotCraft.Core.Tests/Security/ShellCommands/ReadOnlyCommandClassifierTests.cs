using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ReadOnlyCommandClassifierTests
{
    [Theory]
    [InlineData("Get-Content README.md | Select-String x", false, "'Get-Content'")]
    [InlineData("sed -n '1,5p' README.md", true, "")]
    [InlineData("sed -n 1,5p README.md", false, "PowerShell")]
    [InlineData("find . -name *.cs", false, "'*'")]
    [InlineData("ls\n", true, "")]
    [InlineData("git log -p -1", true, "")]
    [InlineData("git -p log", false, "pagination")]
    [InlineData("rg --search-zip x", false, "--search-zip")]
    public void IsReadOnly_ClassifiesTheCommandAndExplainsARefusal(
        string command,
        bool expected,
        string reasonFragment)
    {
        var readOnly = ReadOnlyCommandClassifier.IsReadOnly(command, shell: null, out var reason);

        Assert.Equal(expected, readOnly);
        Assert.Contains(reasonFragment, reason);
    }

    [Theory]
    [InlineData(@"Get-ChildItem src\*.cs | Select -First 30", true, "")]
    [InlineData("gci -Recurse | Sort-Object Length | Format-Table Name", true, "")]
    [InlineData("Get-ChildItem | Where-Object { $_.Length -gt 1 }", false, "PowerShell")]
    [InlineData("Get-ChildItem | Tee-Object out.txt", false, "Tee-Object")]
    [InlineData("compare a.png b.png out.png", true, "")]
    public void IsReadOnly_UnderPowerShell_UsesOnlyThePowerShellGrammar(string command, bool expected, string reasonFragment)
    {
        var readOnly = ReadOnlyCommandClassifier.IsReadOnly(command, shell: null, ShellFamily.PowerShell, out var reason);

        Assert.Equal(expected, readOnly);
        Assert.Contains(reasonFragment, reason);
    }

    [Theory]
    [InlineData("compare a.png b.png out.png")]
    [InlineData("gp -f commands.gp")]
    [InlineData("Get-Content README.md")]
    public void IsReadOnly_UnderPosix_DoesNotTreatPowerShellCommandsAsReadOnly(string command)
    {
        Assert.False(ReadOnlyCommandClassifier.IsReadOnly(command, shell: null, ShellFamily.Posix, out var reason));
        Assert.Contains("read-only allow list", reason);
    }

    [Fact]
    public void IsReadOnly_WithoutATargetShell_StillRequiresBothGrammars()
    {
        Assert.False(ReadOnlyCommandClassifier.IsReadOnly("Get-ChildItem *.cs", shell: null, out var reason));
        Assert.Contains("'*'", reason);
    }
}
