namespace DotCraft.Tools;

public sealed record AcpTerminalOutput(
    string Output,
    bool Truncated,
    uint? ExitCode = null,
    string? Signal = null);

public sealed record AcpTerminalExitResult(
    uint? ExitCode = null,
    string? Signal = null);

public interface IAcpExtensionProxy
{
    IReadOnlyList<string> Extensions { get; }

    bool SupportsFileRead { get; }

    bool SupportsFileWrite { get; }

    bool SupportsTerminal { get; }

    Task<string?> ReadTextFileAsync(string path, int? line = null, int? limit = null, CancellationToken ct = default);

    Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default);

    Task<string?> CreateTerminalAsync(
        string command,
        IReadOnlyList<string>? args = null,
        string? cwd = null,
        Dictionary<string, string>? env = null,
        long? outputByteLimit = null,
        CancellationToken ct = default);

    Task<AcpTerminalOutput?> GetTerminalOutputAsync(string terminalId, CancellationToken ct = default);

    Task<AcpTerminalExitResult?> WaitForTerminalExitAsync(string terminalId, CancellationToken ct = default);

    Task KillTerminalAsync(string terminalId, CancellationToken ct = default);

    Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default);

    Task<T?> SendExtensionAsync<T>(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
