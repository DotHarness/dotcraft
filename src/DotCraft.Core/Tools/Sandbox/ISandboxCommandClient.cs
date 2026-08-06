using OpenSandbox.Models;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Runs and interrupts commands in the sandbox owned by the current workspace runtime.
/// </summary>
public interface ISandboxCommandClient
{
    /// <summary>Runs a command and streams its execution lifecycle to the supplied handlers.</summary>
    Task<Execution> RunAsync(
        string command,
        RunCommandOptions options,
        ExecutionHandlers handlers,
        CancellationToken cancellationToken);

    /// <summary>Interrupts a running sandbox command by execution identifier.</summary>
    Task InterruptAsync(string executionId, CancellationToken cancellationToken);
}

internal sealed class SandboxCommandClient(SandboxSessionManager sandboxManager) : ISandboxCommandClient
{
    public async Task<Execution> RunAsync(
        string command,
        RunCommandOptions options,
        ExecutionHandlers handlers,
        CancellationToken cancellationToken)
    {
        var sandbox = await sandboxManager.GetOrCreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return await sandbox.Commands.RunAsync(command, options, handlers, cancellationToken).ConfigureAwait(false);
    }

    public async Task InterruptAsync(string executionId, CancellationToken cancellationToken)
    {
        var sandbox = await sandboxManager.GetOrCreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        await sandbox.Commands.InterruptAsync(executionId, cancellationToken).ConfigureAwait(false);
    }
}
