using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Common;
using DotCraft.Processes;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CLI;

/// <summary>
/// Manages the lifecycle of a <c>dotcraft app-server</c> subprocess.
/// Spawns the subprocess, wraps its stdio with <see cref="AppServerWireClient"/>, and
/// performs the <c>initialize</c>/<c>initialized</c> handshake on startup.
///
/// Graceful shutdown: closes the subprocess's stdin so the server can detect EOF and
/// exit cleanly, then falls back to <see cref="Process.Kill"/> if it does not exit
/// within a short timeout.
/// </summary>
public sealed class AppServerProcess : IAsyncDisposable
{
    private const int MaxStderrCaptureChars = 16384;

    private readonly Process _process;
    private readonly ManagedChildProcess? _managedChild;
    private readonly int _processId;
    private readonly Task _stderrForwarderTask;
    private readonly StringBuilder _stderrBuffer = new();
    private readonly Lock _stderrLock = new();
    private int? _exitCode;

    /// <summary>
    /// The underlying JSON-RPC 2.0 wire client connected to the subprocess's stdio streams.
    /// </summary>
    public AppServerWireClient Wire { get; }

    /// <summary>
    /// Whether the subprocess is still running.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            if (_disposed)
                return false;

            try
            {
                return !_process.HasExited;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Exit code after the subprocess has exited; null while still running.
    /// </summary>
    public int? ExitCode
    {
        get
        {
            if (_exitCode.HasValue)
                return _exitCode;

            try
            {
                return _process.HasExited ? _process.ExitCode : null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Recent stderr lines from the subprocess (capped), for diagnostics when the process crashes.
    /// </summary>
    public string RecentStderr
    {
        get
        {
            lock (_stderrLock)
            {
                return _stderrBuffer.ToString();
            }
        }
    }

    /// <summary>
    /// OS process ID of the AppServer subprocess.
    /// </summary>
    public int ProcessId => _processId;

    /// <summary>
    /// Server version string reported by the AppServer during the <c>initialize</c> handshake,
    /// e.g. "0.0.1.0+1d845e83". Null if the server did not include <c>serverInfo.version</c>.
    /// </summary>
    public string? ServerVersion { get; private set; }

    /// <summary>
    /// DashBoard URL from the <c>initialize</c> response when the AppServer hosts DashBoard.
    /// </summary>
    public string? DashboardUrl { get; private set; }

    /// <summary>
    /// Whether the server advertises support for model catalog management (<c>model/list</c>).
    /// </summary>
    public bool ModelCatalogManagement { get; private set; }

    /// <summary>
    /// Whether the server advertises support for workspace config updates (<c>workspace/config/update</c>).
    /// </summary>
    public bool WorkspaceConfigManagement { get; private set; }

    /// <summary>
    /// Invoked when the subprocess exits unexpectedly (i.e., not as part of a normal dispose).
    /// </summary>
    public event Action? OnCrashed;

    private bool _disposed;

    private AppServerProcess(Process process, ManagedChildProcess? managedChild, AppServerWireClient wire)
    {
        _process = process;
        _managedChild = managedChild;
        _processId = process.Id;
        Wire = wire;
        _stderrForwarderTask = ForwardStderrAsync();

        process.Exited += (_, _) =>
        {
            if (!_disposed)
                OnCrashed?.Invoke();
        };
        process.EnableRaisingEvents = true;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns <c>&lt;dotcraftBin&gt; app-server [--listen URL]</c> as a child process, starts
    /// the wire client, and performs the initialization handshake.
    /// </summary>
    /// <param name="dotcraftBin">
    /// Path to the <c>dotcraft</c> executable.
    /// When null, defaults to the current process's executable path
    /// (<see cref="Environment.ProcessPath"/>).
    /// </param>
    /// <param name="workspacePath">
    /// Working directory for the subprocess. Null means the current directory.
    /// </param>
    /// <param name="listenUrl">
    /// Optional <c>--listen</c> URL to forward to the subprocess. When set, the
    /// subprocess starts the corresponding transport in addition to (or instead of) stdio.
    /// Only the <c>ws+stdio://</c> scheme is meaningful here since the CLI always
    /// connects via the subprocess's stdio streams.
    /// </param>
    /// <param name="ct">Cancellation token for the startup sequence.</param>
    /// <param name="createNoWindow">Whether to request hidden console-window creation for the subprocess.</param>
    /// <param name="attachWindowsJob">Whether to attach the subprocess to a kill-on-close Windows Job Object.</param>
    public static async Task<AppServerProcess> StartAsync(
        string? dotcraftBin = null,
        string? workspacePath = null,
        string? listenUrl = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken ct = default,
        bool createNoWindow = false,
        bool attachWindowsJob = false)
    {
        var psi = CreateStartInfo(dotcraftBin, workspacePath, listenUrl, environmentVariables, createNoWindow);

        var (process, managedChild) = StartProcess(psi, attachWindowsJob);

        var wire = new AppServerWireClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);

        wire.Start();

        var appServer = new AppServerProcess(process, managedChild, wire);

        // Handshake: send initialize → wait for response → send initialized
        try
        {
            var initResponse = await wire.InitializeAsync(
                clientName: "dotcraft-cli",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true,
                toolExecutionLifecycle: true);

            // Extract server version from the initialize response for display in the welcome screen.
            // Response shape: { "result": { "serverInfo": { "version": "..." } } }
            appServer.ServerVersion = TryGetServerVersion(initResponse);
            appServer.DashboardUrl = TryGetDashboardUrl(initResponse);
            appServer.ModelCatalogManagement = TryGetModelCatalogManagement(initResponse);
            appServer.WorkspaceConfigManagement = TryGetWorkspaceConfigManagement(initResponse);
        }
        catch
        {
            await appServer.DisposeAsync();
            throw;
        }

        return appServer;
    }

    /// <summary>
    /// Spawns <c>dotcraft app-server</c> and returns a wire client without running
    /// <c>initialize</c>. Used by the ACP bridge, which forwards IDE capabilities on first
    /// <c>initialize</c> from the editor.
    /// </summary>
    public static async Task<AppServerProcess> StartWithoutHandshakeAsync(
        string? dotcraftBin = null,
        string? workspacePath = null,
        string? listenUrl = null,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken ct = default,
        bool createNoWindow = false,
        bool attachWindowsJob = false)
    {
        var psi = CreateStartInfo(dotcraftBin, workspacePath, listenUrl, environmentVariables, createNoWindow);

        var (process, managedChild) = StartProcess(psi, attachWindowsJob);

        var wire = new AppServerWireClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);

        wire.Start();

        return new AppServerProcess(process, managedChild, wire);
    }

    // -------------------------------------------------------------------------
    // Shutdown
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose the wire client first, then close the owned stdin stream so the
        // subprocess observes EOF and can clean up its workspace lock.
        await Wire.DisposeAsync();
        try
        {
            _process.StandardInput.Close();
        }
        catch
        {
            // ignored
        }

        // Wait up to 3 seconds for the server to exit cleanly on EOF
        var deadline = Task.Delay(TimeSpan.FromSeconds(3));
        var exited = _process.WaitForExitAsync();
        if (await Task.WhenAny(exited, deadline) != exited)
        {
            // Graceful exit timed out — force-kill the process tree
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignored
            }

            try
            {
                await _process.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // ignored
            }
        }

        try
        {
            _exitCode = _process.HasExited ? _process.ExitCode : null;
        }
        catch
        {
            _exitCode = null;
        }

        // Drain stderr forwarder
        try
        {
            await _stderrForwarderTask;
        }
        catch
        {
            // ignored
        }

        if (_managedChild is not null)
        {
            await _managedChild.DisposeAsync();
        }
        else
        {
            _process.Dispose();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void AppendStderrLine(string line)
    {
        lock (_stderrLock)
        {
            _stderrBuffer.AppendLine(line);
            if (_stderrBuffer.Length > MaxStderrCaptureChars)
            {
                var remove = _stderrBuffer.Length - MaxStderrCaptureChars;
                _stderrBuffer.Remove(0, remove);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string? dotcraftBin,
        string? workspacePath,
        string? listenUrl,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        bool createNoWindow)
    {
        dotcraftBin ??= ResolveCurrentDotCraftBinary();

        var psi = new ProcessStartInfo
        {
            FileName = dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? "dotnet"
                : dotcraftBin,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = createNoWindow,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (dotcraftBin.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            psi.ArgumentList.Add(dotcraftBin);

        psi.ArgumentList.Add("app-server");
        if (!string.IsNullOrWhiteSpace(listenUrl))
        {
            psi.ArgumentList.Add("--listen");
            psi.ArgumentList.Add(listenUrl);
        }

        if (workspacePath != null)
            psi.WorkingDirectory = workspacePath;

        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
                psi.Environment[key] = value ?? string.Empty;
        }

        return psi;
    }

    private static (Process Process, ManagedChildProcess? ManagedChild) StartProcess(
        ProcessStartInfo psi,
        bool attachWindowsJob)
    {
        if (attachWindowsJob && OperatingSystem.IsWindows())
        {
            var managedChild = ManagedChildProcess.Start(psi);
            return (managedChild.Process, managedChild);
        }

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start AppServer subprocess: {DescribeStartInfo(psi)}");
        return (process, null);
    }

    private static string ResolveCurrentDotCraftBinary()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && Path.GetFileNameWithoutExtension(processPath).Equals("dotcraft", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var assemblyPath = typeof(AppServerProcess).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(assemblyPath) && File.Exists(assemblyPath))
            return assemblyPath;

        return processPath ?? throw new InvalidOperationException("Cannot determine dotcraft executable path.");
    }

    private static string DescribeStartInfo(ProcessStartInfo psi)
        => psi.ArgumentList.Count == 0
            ? psi.FileName
            : psi.FileName + " " + string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

    private async Task ForwardStderrAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync() is { } line)
            {
                AppendStderrLine(line);
                await Console.Error.WriteLineAsync($"[AppServer] {line}");
            }
        }
        catch
        {
            // Subprocess exited — stop forwarding
        }
    }

    private static string? TryGetServerVersion(JsonDocument response)
    {
        try
        {
            return response.RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("version")
                .GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetDashboardUrl(JsonDocument response)
    {
        try
        {
            var result = response.RootElement.GetProperty("result");
            if (!result.TryGetProperty("dashboardUrl", out var el))
                return null;
            return el.GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryGetModelCatalogManagement(JsonDocument response)
    {
        try
        {
            return response.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("modelCatalogManagement")
                .GetBoolean();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryGetWorkspaceConfigManagement(JsonDocument response)
    {
        try
        {
            return response.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("workspaceConfigManagement")
                .GetBoolean();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
