using DotCraft.CLI;

namespace DotCraft.Hub;

internal interface IManagedAppServerProcess : IAsyncDisposable
{
    bool IsRunning { get; }

    int? ExitCode { get; }

    string RecentStderr { get; }

    int ProcessId { get; }

    string? ServerVersion { get; }

    event Action? OnCrashed;
}

internal sealed class AppServerProcessAdapter(AppServerProcess process) : IManagedAppServerProcess
{
    public bool IsRunning => process.IsRunning;

    public int? ExitCode => process.ExitCode;

    public string RecentStderr => process.RecentStderr;

    public int ProcessId => process.ProcessId;

    public string? ServerVersion => process.ServerVersion;

    public event Action? OnCrashed
    {
        add => process.OnCrashed += value;
        remove => process.OnCrashed -= value;
    }

    public ValueTask DisposeAsync() => process.DisposeAsync();
}
