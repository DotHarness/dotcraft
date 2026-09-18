namespace DotCraft.Security.ShellCommands;

/// <summary>
/// What the gate knows about a running terminal it may write to: the shell that is running and
/// the directory the kernel last tracked it into. A session that loses its directory never regains one.
/// </summary>
public sealed class ShellStdinSession(ShellIdentity shell, string workingDirectory)
{
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
}
