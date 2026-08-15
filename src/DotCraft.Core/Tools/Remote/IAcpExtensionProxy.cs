namespace DotCraft.Tools;

/// <summary>
/// Abstraction for ACP extension method calls (IDE filesystem, terminal, custom extensions).
/// Implemented by a selected host capability and consumed by ACP-aware runtime tools.
/// </summary>
public interface IAcpExtensionProxy
{
    /// <summary>
    /// Extension method prefixes supported by the connected client (e.g. ["_unity"]).
    /// Empty when the client has not advertised any extensions.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Whether the client can read files via the IDE (unsaved buffer).</summary>
    bool SupportsFileRead { get; }

    /// <summary>Whether the client can write files via the IDE.</summary>
    bool SupportsFileWrite { get; }

    /// <summary>Whether the client can create/manage terminals.</summary>
    bool SupportsTerminal { get; }

    /// <summary>
    /// Reads a text file via the client. Returns file content including unsaved editor changes.
    /// </summary>
    Task<string?> ReadTextFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Writes a text file via the client. The editor may show a diff preview.
    /// </summary>
    Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default);

    /// <summary>
    /// Creates a terminal in the client and executes the given command.
    /// </summary>
    Task<string?> CreateTerminalAsync(string command, string? cwd = null, Dictionary<string, string>? env = null, CancellationToken ct = default);

    /// <summary>
    /// Gets the output and optional exit code from a terminal.
    /// </summary>
    Task<(string output, int? exitCode)> GetTerminalOutputAsync(string terminalId, CancellationToken ct = default);

    /// <summary>
    /// Waits for a terminal command to exit.
    /// </summary>
    Task<(string output, int? exitCode)> WaitForTerminalExitAsync(string terminalId, int? timeoutSeconds = null, CancellationToken ct = default);

    /// <summary>
    /// Kills a terminal command.
    /// </summary>
    Task KillTerminalAsync(string terminalId, CancellationToken ct = default);

    /// <summary>
    /// Releases a terminal.
    /// </summary>
    Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default);

    /// <summary>
    /// Sends an extension method request and deserializes the result.
    /// </summary>
    Task<T?> SendExtensionAsync<T>(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
