using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class ShellIdentityResolverTests
{
    private static readonly string SystemRoot = Path.Combine(Path.GetTempPath(), "shell-identity-system-root");

    private static readonly string SystemPowerShell =
        Path.Combine(SystemRoot, @"System32\WindowsPowerShell\v1.0\powershell.exe");

    private static readonly string SystemCmd = Path.Combine(SystemRoot, @"System32\cmd.exe");

    private static readonly string PwshOnPath = Path.Combine(SystemRoot, "pwsh-install", "pwsh.exe");

    private static readonly string CustomPowerShell = Path.Combine(SystemRoot, "custom", "powershell.exe");

    [Theory]
    [InlineData("")]
    [InlineData("powershell")]
    [InlineData("powershell.exe")]
    public void TryResolve_WindowsPowerShellSelectors_UseTheSystemPowerShell(string selector)
    {
        Assert.True(Windows().TryResolve(selector, out var identity, out _));

        Assert.Equal(ShellKind.PowerShell, identity!.Kind);
        Assert.Equal(SystemPowerShell, identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_WindowsCmdSelector_ResolvesTheSystemCmd()
    {
        Assert.True(Windows().TryResolve("cmd", out var identity, out _));

        Assert.Equal(ShellKind.Cmd, identity!.Kind);
        Assert.Equal(SystemCmd, identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_WindowsPwshSelector_TakesThePathLookupResult()
    {
        var resolver = Windows(findOnPath: name => name == "pwsh.exe" ? PwshOnPath : null);

        Assert.True(resolver.TryResolve("pwsh", out var identity, out _));

        Assert.Equal(ShellKind.Pwsh, identity!.Kind);
        Assert.Equal(PwshOnPath, identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_WindowsPwshMissingFromThePath_Fails()
    {
        var resolver = Windows(fileExists: _ => false, findOnPath: _ => null);

        Assert.False(resolver.TryResolve("pwsh", out _, out var reason));

        Assert.Contains("Pwsh", reason!);
    }

    [Fact]
    public void TryResolve_WindowsBashSelector_FailsNamingTheSelector()
    {
        Assert.False(Windows().TryResolve("bash", out _, out var reason));

        Assert.Contains("bash", reason!);
    }

    [Fact]
    public void TryResolve_WindowsAbsolutePowerShellPath_ResolvesThatExecutable()
    {
        var resolver = Windows(fileExists: path => path == CustomPowerShell);

        Assert.True(resolver.TryResolve(CustomPowerShell, out var identity, out _));

        Assert.Equal(ShellKind.PowerShell, identity!.Kind);
        Assert.Equal(CustomPowerShell, identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_PosixWithoutASelector_UsesBinBash()
    {
        Assert.True(Posix().TryResolve(null, out var identity, out _));

        Assert.Equal(ShellKind.Bash, identity!.Kind);
        Assert.Equal(Path.GetFullPath("/bin/bash"), identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_PosixZshSelector_TakesThePathLookupResult()
    {
        var resolver = Posix(findOnPath: name => name == "zsh" ? "/usr/bin/zsh" : null);

        Assert.True(resolver.TryResolve("zsh", out var identity, out _));

        Assert.Equal(ShellKind.Zsh, identity!.Kind);
        Assert.Equal(Path.GetFullPath("/usr/bin/zsh"), identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_PosixAbsolutePwshPath_ResolvesThatExecutable()
    {
        var resolver = Posix(fileExists: path => path == "/usr/local/bin/pwsh");

        Assert.True(resolver.TryResolve("/usr/local/bin/pwsh", out var identity, out _));

        Assert.Equal(ShellKind.Pwsh, identity!.Kind);
        Assert.Equal(Path.GetFullPath("/usr/local/bin/pwsh"), identity.ExecutablePath);
    }

    [Fact]
    public void TryResolve_PosixUnsupportedSelector_FailsNamingTheSelector()
    {
        Assert.False(Posix().TryResolve("fish", out _, out var reason));

        Assert.Contains("fish", reason!);
    }

    [Fact]
    public void TryResolve_PosixLocalProgramThatExists_IsStillNotAShell()
    {
        Assert.False(Posix().TryResolve("./evil", out _, out var reason));

        Assert.Contains("./evil", reason!);
    }

    [Theory]
    [InlineData(@"C:\Tools\Git.EXE", true, "git")]
    [InlineData("powershell.exe", true, "powershell")]
    [InlineData("/usr/bin/git", false, "git")]
    [InlineData("./evil", false, "evil")]
    public void ExecutableName_ReducesAPathToItsComparableName(string value, bool isWindows, string expected) =>
        Assert.Equal(expected, ShellIdentityResolver.ExecutableName(value, isWindows));

    private static ShellIdentityResolver Windows(
        Func<string, bool>? fileExists = null,
        Func<string, string?>? findOnPath = null) =>
        new(new ShellExecutableProbe(
            IsWindows: true,
            SystemRoot: SystemRoot,
            FileExists: fileExists ?? (_ => true),
            FindOnPath: findOnPath ?? (_ => null)));

    private static ShellIdentityResolver Posix(
        Func<string, bool>? fileExists = null,
        Func<string, string?>? findOnPath = null) =>
        new(new ShellExecutableProbe(
            IsWindows: false,
            SystemRoot: null,
            FileExists: fileExists ?? (_ => true),
            FindOnPath: findOnPath ?? (_ => null)));
}
