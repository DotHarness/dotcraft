using DotCraft.Tools.Sandbox;
using OpenSandbox.Models;

namespace DotCraft.Tests.Tools;

internal sealed class StubSandboxCommandClient : ISandboxCommandClient
{
    public Func<string, RunCommandOptions, ExecutionHandlers, CancellationToken, Task<Execution>>? RunHandler { get; init; }

    public Func<string, CancellationToken, Task>? InterruptHandler { get; init; }

    public Task<Execution> RunAsync(
        string command,
        RunCommandOptions options,
        ExecutionHandlers handlers,
        CancellationToken cancellationToken) =>
        RunHandler?.Invoke(command, options, handlers, cancellationToken)
        ?? throw new NotSupportedException();

    public Task InterruptAsync(string executionId, CancellationToken cancellationToken) =>
        InterruptHandler?.Invoke(executionId, cancellationToken)
        ?? Task.CompletedTask;
}
