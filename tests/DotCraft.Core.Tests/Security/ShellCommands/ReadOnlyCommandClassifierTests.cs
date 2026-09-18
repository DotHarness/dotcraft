using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ReadOnlyCommandClassifierTests
{
    [Theory]
    [InlineData("Get-Content README.md | Select-String x", true, "")]
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
}
