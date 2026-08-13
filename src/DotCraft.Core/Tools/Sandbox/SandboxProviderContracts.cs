namespace DotCraft.Tools.Sandbox;

/// <summary>Creates isolated sandbox instances for the Core sandbox runtime.</summary>
public interface ISandboxProvider
{
    /// <summary>Creates a new isolated sandbox instance.</summary>
    Task<ISandboxInstance> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral operations required by Core sandbox tools.</summary>
public interface ISandboxInstance : IAsyncDisposable
{
    /// <summary>Gets the provider-assigned instance identifier.</summary>
    string Id { get; }

    /// <summary>Runs a command inside the sandbox.</summary>
    Task<SandboxCommandResult> RunCommandAsync(
        string command,
        SandboxCommandOptions? options = null,
        SandboxCommandHandlers? handlers = null,
        CancellationToken cancellationToken = default);

    /// <summary>Interrupts a running command by provider-assigned execution identifier.</summary>
    Task InterruptCommandAsync(string executionId, CancellationToken cancellationToken = default);

    /// <summary>Reads a UTF-8 text file from the sandbox.</summary>
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Creates directories inside the sandbox.</summary>
    Task CreateDirectoriesAsync(
        IReadOnlyList<SandboxDirectoryEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>Writes UTF-8 text files inside the sandbox.</summary>
    Task WriteFilesAsync(
        IReadOnlyList<SandboxWriteEntry> entries,
        CancellationToken cancellationToken = default);

    /// <summary>Terminates the remote sandbox instance.</summary>
    Task KillAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral command execution options.</summary>
public sealed class SandboxCommandOptions
{
    /// <summary>Gets or sets the provider-side command timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>Provider-neutral command lifecycle callbacks.</summary>
public sealed class SandboxCommandHandlers
{
    /// <summary>Gets or sets the callback invoked when the provider assigns an execution id.</summary>
    public Func<string, Task>? OnInitialized { get; set; }
}

/// <summary>One provider-neutral command log line.</summary>
public sealed record SandboxCommandLogLine(string? Text);

/// <summary>Provider-neutral command failure details.</summary>
public sealed record SandboxCommandError(string Name, string Value);

/// <summary>Provider-neutral command execution result.</summary>
public sealed record SandboxCommandResult(
    IReadOnlyList<SandboxCommandLogLine> Stdout,
    IReadOnlyList<SandboxCommandLogLine> Stderr,
    SandboxCommandError? Error = null);

/// <summary>Directory creation request for one sandbox path.</summary>
public sealed record SandboxDirectoryEntry(string Path, int Mode);

/// <summary>Text file write request for one sandbox path.</summary>
public sealed record SandboxWriteEntry(string Path, string Data, int Mode);

/// <summary>A provider failure normalized at the Core sandbox boundary.</summary>
public sealed class SandboxProviderException : Exception
{
    /// <summary>Creates a normalized provider failure.</summary>
    public SandboxProviderException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>Gets the provider-supplied stable error code when available.</summary>
    public string Code { get; }
}
