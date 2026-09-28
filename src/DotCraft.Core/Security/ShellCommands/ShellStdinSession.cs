namespace DotCraft.Security.ShellCommands;

/// <summary>The shell and launch directory used as approval context for terminal input.</summary>
public sealed class ShellStdinSession(ShellIdentity shell, string workingDirectory)
{
    private readonly SemaphoreSlim _interaction = new(1, 1);

    public ShellIdentity Shell { get; } = shell;

    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>Takes this terminal's turn; the caller holds it across assessment, approval and the write.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _interaction.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Interaction(_interaction);
    }

    private sealed class Interaction(SemaphoreSlim interaction) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                interaction.Release();
        }
    }
}
