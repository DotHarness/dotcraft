using DotCraft.Tools.Sandbox;

namespace DotCraft.Tests.Tools;

internal sealed class StubSandboxCommandClient : ISandboxCommandClient
{
    public Func<string, SandboxCommandOptions, SandboxCommandHandlers, CancellationToken, Task<SandboxCommandResult>>? RunHandler { get; init; }

    public Func<string, CancellationToken, Task>? InterruptHandler { get; init; }

    public Task<SandboxCommandResult> RunAsync(
        string command,
        SandboxCommandOptions options,
        SandboxCommandHandlers handlers,
        CancellationToken cancellationToken) =>
        RunHandler?.Invoke(command, options, handlers, cancellationToken)
        ?? throw new NotSupportedException();

    public Task InterruptAsync(string executionId, CancellationToken cancellationToken) =>
        InterruptHandler?.Invoke(executionId, cancellationToken)
        ?? Task.CompletedTask;
}
