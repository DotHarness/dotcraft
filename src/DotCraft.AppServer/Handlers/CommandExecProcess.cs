using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace DotCraft.AppServer;

internal sealed class CommandExecProcess : IAsyncDisposable
{
    private const int TimeoutExitCode = 124;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly Stream _stderr;
    private readonly IDisposable? _ownedHandle;

    internal CommandExecProcess(Process process, Stream stdout, Stream stderr, IDisposable? ownedHandle = null)
    {
        _process = process;
        _stdout = stdout;
        _stderr = stderr;
        _ownedHandle = ownedHandle;
    }

    public static string? ResolveProgram(string command, string cwd, IReadOnlyDictionary<string, string> environment)
    {
        if (Path.IsPathRooted(command) || command.Contains('/') || command.Contains(Path.DirectorySeparatorChar))
            return FindExecutable(Path.GetFullPath(command, cwd), environment);

        environment.TryGetValue("PATH", out var searchPath);
        foreach (var directory in (searchPath ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory))
                continue;
            if (FindExecutable(Path.Combine(directory, command), environment) is { } found)
                return found;
        }

        return null;
    }

    public static CommandExecProcess Start(
        string program,
        IReadOnlyList<string> arguments,
        string cwd,
        IReadOnlyDictionary<string, string> environment)
    {
        try
        {
            return OperatingSystem.IsWindows()
                ? CommandExecWindowsLauncher.Start(program, arguments, cwd, environment)
                : StartProcess(program, arguments, cwd, environment);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            throw new FileNotFoundException(ex.Message, program, ex);
        }
    }

    public async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        TimeSpan timeout,
        int outputBytesCap,
        CancellationToken ct)
    {
        var stdout = new CappedOutput(outputBytesCap);
        var stderr = new CappedOutput(outputBytesCap);
        using var drain = new CancellationTokenSource();
        var pumps = Task.WhenAll(PumpAsync(_stdout, stdout, drain.Token), PumpAsync(_stderr, stderr, drain.Token));

        int exitCode;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeoutCts.CancelAfter(timeout);
            try
            {
                await _process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                exitCode = _process.ExitCode;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                KillTree();
                exitCode = TimeoutExitCode;
            }
        }

        try
        {
            await pumps.WaitAsync(DrainTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        drain.Cancel();
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    public ValueTask DisposeAsync()
    {
        if (!HasExited())
            KillTree();
        _stdout.Dispose();
        _stderr.Dispose();
        _process.Dispose();
        _ownedHandle?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CommandExecProcess StartProcess(
        string program,
        IReadOnlyList<string> arguments,
        string cwd,
        IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo(program)
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
            startInfo.Environment[name] = value;

        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException($"Failed to start {program}.");
        process.StandardInput.Close();
        return new CommandExecProcess(process, process.StandardOutput.BaseStream, process.StandardError.BaseStream);
    }

    private static string? FindExecutable(string path, IReadOnlyDictionary<string, string> environment)
    {
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode executable = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return File.Exists(path) && (File.GetUnixFileMode(path) & executable) != 0 ? path : null;
        }

        if (Path.HasExtension(path) && File.Exists(path))
            return path;
        environment.TryGetValue("PATHEXT", out var extensions);
        foreach (var extension in (extensions ?? ".COM;.EXE;.BAT;.CMD").Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (File.Exists(path + extension))
                return path + extension;
        }

        return null;
    }

    private static async Task PumpAsync(Stream source, CappedOutput sink, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                sink.Append(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return true;
        }
    }

    private void KillTree()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private sealed class CappedOutput(int capacity)
    {
        private readonly object _gate = new();
        private readonly MemoryStream _bytes = new();

        public void Append(ReadOnlySpan<byte> chunk)
        {
            lock (_gate)
            {
                var room = capacity - (int)_bytes.Length;
                if (room > 0)
                    _bytes.Write(chunk[..Math.Min(room, chunk.Length)]);
            }
        }

        public override string ToString()
        {
            lock (_gate)
                return Encoding.UTF8.GetString(_bytes.GetBuffer(), 0, (int)_bytes.Length);
        }
    }
}
