namespace DotCraft.Security.ShellCommands;

public enum ShellKind
{
    PowerShell,
    Pwsh,
    Cmd,
    Bash,
    Sh,
    Zsh
}

public enum ShellFamily
{
    PowerShell,
    Cmd,
    Posix
}

public enum CommandPlatform
{
    Posix,
    Windows
}

public static class ShellKindExtensions
{
    public static ShellFamily ToFamily(this ShellKind kind) => kind switch
    {
        ShellKind.PowerShell or ShellKind.Pwsh => ShellFamily.PowerShell,
        ShellKind.Cmd => ShellFamily.Cmd,
        _ => ShellFamily.Posix
    };

    public static CommandPlatform HostPlatform() =>
        OperatingSystem.IsWindows() ? CommandPlatform.Windows : CommandPlatform.Posix;
}
