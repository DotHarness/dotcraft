using DotCraft.Tools.Sandbox;

namespace DotCraft.Tests.Tools;

internal sealed class StubSandboxProvider(Func<CancellationToken, Task<ISandboxInstance>>? create = null)
    : ISandboxProvider
{
    public Task<ISandboxInstance> CreateAsync(CancellationToken cancellationToken = default) =>
        create?.Invoke(cancellationToken) ?? throw new NotSupportedException();
}

internal sealed class StubSandboxInstance : ISandboxInstance
{
    public string Id { get; init; } = "sandbox-test";

    public Func<string, SandboxCommandOptions?, SandboxCommandHandlers?, CancellationToken, Task<SandboxCommandResult>>?
        RunCommandHandler { get; init; }

    public Func<string, CancellationToken, Task>? InterruptCommandHandler { get; init; }

    public Func<string, CancellationToken, Task<string>>? ReadFileHandler { get; init; }

    public Func<IReadOnlyList<SandboxDirectoryEntry>, CancellationToken, Task>? CreateDirectoriesHandler { get; init; }

    public Func<IReadOnlyList<SandboxWriteEntry>, CancellationToken, Task>? WriteFilesHandler { get; init; }

    public Func<CancellationToken, Task>? KillHandler { get; init; }

    public Func<ValueTask>? DisposeHandler { get; init; }

    public Task<SandboxCommandResult> RunCommandAsync(
        string command,
        SandboxCommandOptions? options = null,
        SandboxCommandHandlers? handlers = null,
        CancellationToken cancellationToken = default) =>
        RunCommandHandler?.Invoke(command, options, handlers, cancellationToken)
        ?? Task.FromResult(new SandboxCommandResult([], []));

    public Task InterruptCommandAsync(string executionId, CancellationToken cancellationToken = default) =>
        InterruptCommandHandler?.Invoke(executionId, cancellationToken) ?? Task.CompletedTask;

    public Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
        ReadFileHandler?.Invoke(path, cancellationToken) ?? Task.FromResult(string.Empty);

    public Task CreateDirectoriesAsync(
        IReadOnlyList<SandboxDirectoryEntry> entries,
        CancellationToken cancellationToken = default) =>
        CreateDirectoriesHandler?.Invoke(entries, cancellationToken) ?? Task.CompletedTask;

    public Task WriteFilesAsync(
        IReadOnlyList<SandboxWriteEntry> entries,
        CancellationToken cancellationToken = default) =>
        WriteFilesHandler?.Invoke(entries, cancellationToken) ?? Task.CompletedTask;

    public Task KillAsync(CancellationToken cancellationToken = default) =>
        KillHandler?.Invoke(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => DisposeHandler?.Invoke() ?? ValueTask.CompletedTask;
}
