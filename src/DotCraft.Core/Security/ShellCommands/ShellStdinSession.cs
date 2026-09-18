namespace DotCraft.Security.ShellCommands;

/// <summary>What the gate knows about a running terminal it may write to. A session that loses
/// its directory never regains one.</summary>
public sealed class ShellStdinSession(ShellIdentity shell, string workingDirectory)
{
    private readonly SemaphoreSlim _interaction = new(1, 1);

    private readonly object _sync = new();

    private string _workingDirectory = workingDirectory;

    private bool _isKnown = true;

    public ShellIdentity Shell { get; } = shell;

    /// <summary>Reads both parts together so a concurrent write cannot land between them.</summary>
    public (string Directory, bool IsKnown) Location
    {
        get
        {
            lock (_sync)
                return (_workingDirectory, _isKnown);
        }
    }

    public string WorkingDirectory => Location.Directory;

    public bool WorkingDirectoryIsKnown => Location.IsKnown;

    /// <summary>Takes this terminal's turn; the caller holds it across assessment, approval and the write.</summary>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _interaction.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Interaction(_interaction);
    }

    public void TrackWorkingDirectory(string? directory)
    {
        lock (_sync)
        {
            if (directory is null)
                _isKnown = false;
            else
                _workingDirectory = directory;
        }
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
