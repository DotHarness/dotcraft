using DotCraft.Security.ShellCommands;
using Xunit;

namespace DotCraft.Tests.Security.ShellCommands;

public sealed class DangerousCommandDetectorTests
{
    private readonly DangerousCommandDetector _detector = new(new PosixScriptLowerer());

    public static TheoryData<string[]> ForcedPosixRemovals => Commands(
        ["rm", "-rf", "/"],
        ["rm", "-f", "/"],
        ["/bin/rm", "-fr", "/tmp/example"],
        ["rm", "-r", "-f", "/tmp/example"],
        ["rm", "--force", "/tmp/example"],
        ["rm", "/tmp/example", "-f"],
        ["sudo", "rm", "-rf", "/tmp/example"],
        ["env", "TARGET=/tmp/example", "rm", "-rf", "/tmp/example"],
        ["trap", "rm -rf /tmp/example", "EXIT"],
        ["bash", "-lc", "rm -rf /tmp/example"],
        ["sh", "-c", "cd /tmp; rm -rf example"]);

    public static TheoryData<string[]> SafePosixCommands => Commands(
        ["rm", "-r", "/tmp/example"],
        ["rm", "--", "-f"],
        ["env", "TARGET=/tmp/example", "rm", "-r", "/tmp/example"],
        ["trap", "echo rm -rf /tmp/example", "EXIT"],
        ["bash", "-lc", "echo 'rm -rf x'"],
        ["bash", "-lc", "ls -la"],
        ["echo", "rm", "-rf", "/"]);

    public static TheoryData<string[]> ForcedPowerShellDeletes => Commands(
        ["Remove-Item", "test", "-Force"],
        ["ri", "test", "-Force"],
        ["del", "-Force", @"C:\foo"],
        ["remove-item", "test", "-force"]);

    public static TheoryData<string[]> SafePowerShellCommands => Commands(
        ["Remove-Item", "test"],
        ["Get-ChildItem", "-Force"],
        ["Start-Process", "notepad.exe"],
        ["explorer.exe", "."]);

    public static TheoryData<string[]> PowerShellUrlLaunches => Commands(
        ["Start-Process", "https://example.com"],
        ["Start-Process", "hTtPs://example.com"],
        ["msedge.exe", "https://example.com"],
        ["rundll32", "url.dll,FileProtocolHandler", "https://example.com"],
        ["rundll32", "url.dll", "FileProtocolHandler", "https://example.com"]);

    public static TheoryData<string[]> ForcedCmdDeletes => Commands(
        ["del", "/f", "test.txt"],
        ["erase", "/F", "test.txt"],
        ["rd", "/s", "/q", "test"],
        ["cmd", "/c", "del", "/f", "file.txt"],
        ["cmd.exe", "/r", "del", "/f", "file.txt"]);

    public static TheoryData<string[]> SafeCmdCommands => Commands(
        ["del", "test.txt"],
        ["del", "C:/foo/bar.txt"],
        ["rd", "/s", "test"],
        ["echo", "del", "/f"]);

    [Theory]
    [MemberData(nameof(ForcedPosixRemovals))]
    public void Match_PosixForcedRemoval_IsForcedRemove(string[] command)
    {
        var match = _detector.Match(command, ShellFamily.Posix, CommandPlatform.Posix);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
        Assert.Equal("rm -f style commands are not permitted", match.Reason);
    }

    [Theory]
    [MemberData(nameof(SafePosixCommands))]
    public void Match_PosixSafeCommand_HasNoMatch(string[] command) =>
        Assert.Null(_detector.Match(command, ShellFamily.Posix, CommandPlatform.Posix));

    [Fact]
    public void Match_PosixExecutableOnWindows_IgnoresCaseAndExtension()
    {
        var match = _detector.Match(["RM.EXE", "-rf", "x"], ShellFamily.Posix, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
    }

    [Fact]
    public void Match_WrappersWithinDepthBound_StillReachesTheCommand()
    {
        var match = _detector.Match(EnvWrapped(8), ShellFamily.Posix, CommandPlatform.Posix);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
    }

    [Fact]
    public void Match_WrappersPastDepthBound_IsOther()
    {
        var match = _detector.Match(EnvWrapped(9), ShellFamily.Posix, CommandPlatform.Posix);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.Other, match.Kind);
        Assert.Equal("command wrappers nest too deeply", match.Reason);
    }

    [Theory]
    [MemberData(nameof(ForcedPowerShellDeletes))]
    public void Match_PowerShellForcedDelete_IsForcedRemove(string[] command)
    {
        var match = _detector.Match(command, ShellFamily.PowerShell, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
        Assert.Equal("forced deletes are not permitted", match.Reason);
    }

    [Theory]
    [MemberData(nameof(SafePowerShellCommands))]
    public void Match_PowerShellSafeCommand_HasNoMatch(string[] command) =>
        Assert.Null(_detector.Match(command, ShellFamily.PowerShell, CommandPlatform.Windows));

    [Theory]
    [MemberData(nameof(PowerShellUrlLaunches))]
    public void Match_PowerShellUrlLaunch_IsOther(string[] command)
    {
        var match = _detector.Match(command, ShellFamily.PowerShell, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.Other, match.Kind);
        Assert.Equal("launching URLs is not permitted", match.Reason);
    }

    [Fact]
    public void Match_PosixWordsOnWindows_StillSeesAUrlLaunch()
    {
        var match = _detector.Match(["start", "https://example.com"], ShellFamily.Posix, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.Other, match.Kind);
    }

    [Theory]
    [MemberData(nameof(ForcedCmdDeletes))]
    public void Match_CmdForcedDelete_IsForcedRemove(string[] command)
    {
        var match = _detector.Match(command, ShellFamily.Cmd, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
    }

    [Theory]
    [MemberData(nameof(SafeCmdCommands))]
    public void Match_CmdSafeCommand_HasNoMatch(string[] command) =>
        Assert.Null(_detector.Match(command, ShellFamily.Cmd, CommandPlatform.Windows));

    [Theory]
    [InlineData("start", "https://example.com")]
    [InlineData("start", "", "https://example.com")]
    public void Match_CmdUrlLaunch_IsOther(params string[] command)
    {
        var match = _detector.Match(command, ShellFamily.Cmd, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.Other, match.Kind);
        Assert.Equal("launching URLs is not permitted", match.Reason);
    }

    [Fact]
    public void MatchAny_WhenNoLiteralCommandIsDangerous_HasNoMatch()
    {
        var lowering = Lowered([["Get-ChildItem", "-Force"], ["Remove-Item", "test"]]);

        Assert.Null(_detector.MatchAny(lowering, CommandPlatform.Windows));
    }

    [Fact]
    public void MatchAny_WhenALiteralCommandIsDangerous_ReportsIt()
    {
        var lowering = Lowered([["if"], ["Remove-Item", "test", "-Force"]]);

        var match = _detector.MatchAny(lowering, CommandPlatform.Windows);

        Assert.NotNull(match);
        Assert.Equal(DangerousCommandKind.ForcedRemove, match.Kind);
        Assert.Equal("Remove-Item test -Force", match.Evidence);
    }

    private static TheoryData<string[]> Commands(params string[][] commands)
    {
        var data = new TheoryData<string[]>();
        foreach (var command in commands)
            data.Add(command);
        return data;
    }

    private static LoweredScript Lowered(IReadOnlyList<IReadOnlyList<string>> commands) =>
        LoweredScript.Opaque(ShellFamily.PowerShell, "script is opaque", commands);

    private static string[] EnvWrapped(int depth)
    {
        var command = new List<string>();
        for (var index = 0; index < depth; index++)
            command.Add("env");
        command.AddRange(["rm", "-rf", "/tmp/example"]);
        return [.. command];
    }
}
