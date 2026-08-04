using RuntimeTerminal = DotCraft.Tools.BackgroundTerminals.BackgroundTerminalSnapshot;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>Maps background-terminal runtime snapshots to stable AppServer contracts.</summary>
public static class TerminalContractMapper
{
    public static Contract.BackgroundTerminalSnapshot ToContract(RuntimeTerminal terminal) => new()
    {
        SessionId = terminal.SessionId,
        ThreadId = terminal.ThreadId,
        TurnId = terminal.TurnId,
        CallId = terminal.CallId,
        Command = terminal.Command,
        WorkingDirectory = terminal.WorkingDirectory,
        Source = terminal.Source,
        Status = terminal.Status,
        Output = terminal.Output,
        OutputPath = terminal.OutputPath,
        ExitCode = terminal.ExitCode,
        StartedAt = terminal.StartedAt,
        CompletedAt = terminal.CompletedAt,
        WallTimeMs = terminal.WallTimeMs,
        OriginalOutputChars = terminal.OriginalOutputChars,
        Truncated = terminal.Truncated,
        BackgroundReason = terminal.BackgroundReason
    };
}
