namespace DotCraft.Security.ShellCommands;

public sealed class LoweredScript
{
    public required ShellFamily Family { get; init; }

    public IReadOnlyList<IReadOnlyList<string>>? PlainCommands { get; init; }

    public string? PlainRejectReason { get; init; }

    public required IReadOnlyList<IReadOnlyList<string>> LiteralCommands { get; init; }

    /// <summary>Leading commands that move the shell itself, stopping at the first one behind
    /// <c>&amp;&amp;</c> or <c>||</c> and the first one in a pipeline.</summary>
    public int UnconditionalPrefix { get; init; }

    public bool IsPlain => PlainCommands is not null;

    public static LoweredScript Plain(
        ShellFamily family,
        IReadOnlyList<IReadOnlyList<string>> commands,
        int unconditionalPrefix) => new()
    {
        Family = family,
        PlainCommands = commands,
        LiteralCommands = commands,
        UnconditionalPrefix = unconditionalPrefix
    };

    public static LoweredScript Opaque(
        ShellFamily family,
        string reason,
        IReadOnlyList<IReadOnlyList<string>>? literalCommands = null) => new()
    {
        Family = family,
        PlainRejectReason = reason,
        LiteralCommands = literalCommands ?? []
    };
}

public interface IShellScriptLowerer
{
    ShellFamily Family { get; }

    LoweredScript Lower(string script);
}

public static class ShellScriptSentinels
{
    public const string Posix = "__shell_script__";

    public const string PowerShell = "__powershell_script__";

    public const string Cmd = "__cmd_script__";

    public static string For(ShellFamily family) => family switch
    {
        ShellFamily.PowerShell => PowerShell,
        ShellFamily.Cmd => Cmd,
        _ => Posix
    };
}
