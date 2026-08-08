using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Processes;
using Microsoft.AspNetCore.Http;

namespace DotCraft.Hub;

/// <summary>
/// Supervises the small, closed set of product-owned local services registered by DotCraft.
/// </summary>
public sealed class ManagedLocalServiceRegistry : IAsyncDisposable
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(20);
    private readonly IReadOnlyDictionary<string, ManagedLocalServiceDefinition> _definitions;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    internal Func<ManagedServiceLaunch, CancellationToken, Task<IManagedLocalServiceProcess>> StartProcessAsync { get; set; } =
        StartProcessCoreAsync;

    internal Func<string, CancellationToken, Task> ProbeHealthAsync { get; set; } = ProbeHealthCoreAsync;

    /// <summary>
    /// Creates a registry for an explicit, trusted service definition set.
    /// </summary>
    public ManagedLocalServiceRegistry(IEnumerable<ManagedLocalServiceDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(item => item.ServiceId, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Starts or reuses a healthy service instance.
    /// </summary>
    public async Task<HubManagedServiceResponse> EnsureAsync(
        EnsureManagedServiceRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var definition = ResolveDefinition(request.ServiceId);
        var entry = _entries.GetOrAdd(definition.ServiceId, _ => new Entry(definition.ServiceId));

        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            RefreshExited(entry);
            if (entry.Process is { IsRunning: true } && entry.State == HubManagedServiceStates.Running)
                return entry.ToResponse();

            if (!request.StartIfMissing)
                return entry.ToResponse();

            var executable = ResolveExecutable(request.Executable);
            await StopProcessAsync(entry);
            await StartAsync(entry, definition, executable, cancellationToken);
            return entry.ToResponse();
        }
        finally
        {
            entry.Mutex.Release();
        }
    }

    /// <summary>
    /// Returns the current in-memory state without starting the service.
    /// </summary>
    public HubManagedServiceResponse Get(string serviceId)
    {
        ThrowIfDisposed();
        var definition = ResolveDefinition(serviceId);
        if (!_entries.TryGetValue(definition.ServiceId, out var entry))
            return new HubManagedServiceResponse(
                definition.ServiceId,
                HubManagedServiceStates.Stopped,
                null, null, null, null, null, null);

        RefreshExited(entry);
        return entry.ToResponse();
    }

    /// <summary>
    /// Stops a running service, if owned by this Hub.
    /// </summary>
    public async Task<HubManagedServiceResponse> StopAsync(string serviceId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var definition = ResolveDefinition(serviceId);
        var entry = _entries.GetOrAdd(definition.ServiceId, _ => new Entry(definition.ServiceId));
        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            entry.State = HubManagedServiceStates.Stopping;
            await StopProcessAsync(entry);
            entry.State = HubManagedServiceStates.Stopped;
            entry.LastError = null;
            return entry.ToResponse();
        }
        finally
        {
            entry.Mutex.Release();
        }
    }

    /// <summary>
    /// Replaces a running service instance using the supplied host-resolved executable.
    /// </summary>
    public async Task<HubManagedServiceResponse> RestartAsync(
        ManagedServiceRequest request,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var definition = ResolveDefinition(request.ServiceId);
        var executable = ResolveExecutable(request.Executable);
        var entry = _entries.GetOrAdd(definition.ServiceId, _ => new Entry(definition.ServiceId));
        await entry.Mutex.WaitAsync(cancellationToken);
        try
        {
            entry.State = HubManagedServiceStates.Stopping;
            await StopProcessAsync(entry);
            await StartAsync(entry, definition, executable, cancellationToken);
            return entry.ToResponse();
        }
        finally
        {
            entry.Mutex.Release();
        }
    }

    private async Task StartAsync(
        Entry entry,
        ManagedLocalServiceDefinition definition,
        string executable,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(definition.StateRoot);
        var endpoint = $"http://127.0.0.1:{HubPortAllocator.AllocateLoopbackPort()}";
        var token = CreateToken();
        entry.State = HubManagedServiceStates.Starting;
        entry.Endpoint = endpoint;
        entry.AccessToken = token;
        entry.Version = null;
        entry.LastError = null;
        entry.RecentStderr = null;

        IManagedLocalServiceProcess? process = null;
        try
        {
            process = await StartProcessAsync(
                new ManagedServiceLaunch(definition.ServiceId, executable, endpoint, token, definition.StateRoot),
                cancellationToken);
            entry.Process = process;
            entry.Pid = process.ProcessId;

            using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyCts.CancelAfter(ReadyTimeout);
            var ready = await process.WaitForReadyAsync(readyCts.Token);
            if (!string.Equals(ready.ServiceId, definition.ServiceId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(ready.Endpoint.TrimEnd('/'), endpoint, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Managed service returned mismatched readiness metadata.");
            }

            await ProbeHealthAsync(endpoint + definition.HealthPath, readyCts.Token);
            entry.Version = ready.Version;
            entry.State = HubManagedServiceStates.Running;
        }
        catch (Exception ex) when (ex is not HubProtocolException)
        {
            entry.State = HubManagedServiceStates.Unhealthy;
            entry.LastError = ex is OperationCanceledException
                ? "Managed service did not become ready before the startup timeout."
                : ex.Message;
            if (process is not null)
            {
                await process.DisposeAsync();
                entry.RecentStderr = process.RecentStderr;
            }
            entry.Process = null;
            entry.Pid = null;
            entry.AccessToken = null;
            throw new HubProtocolException(
                "managedServiceStartFailed",
                "Managed local service failed to start.",
                StatusCodes.Status503ServiceUnavailable,
                new { serviceId = definition.ServiceId, reason = entry.LastError, recentStderr = entry.RecentStderr });
        }
    }

    private ManagedLocalServiceDefinition ResolveDefinition(string serviceId)
    {
        var normalized = serviceId?.Trim() ?? string.Empty;
        if (!_definitions.TryGetValue(normalized, out var definition))
        {
            throw new HubProtocolException(
                "managedServiceNotRegistered",
                "The requested local service is not registered by this DotCraft build.",
                StatusCodes.Status404NotFound,
                new { serviceId = normalized });
        }

        return definition;
    }

    private static string ResolveExecutable(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new HubProtocolException(
                "managedServiceExecutableRequired",
                "A host-resolved executable is required to start the local service.",
                StatusCodes.Status400BadRequest);
        }

        var path = Path.GetFullPath(executable.Trim());
        if (!File.Exists(path))
        {
            throw new HubProtocolException(
                "managedServiceExecutableNotFound",
                "The host-resolved local service executable does not exist.",
                StatusCodes.Status400BadRequest,
                new { executable = path });
        }

        return path;
    }

    private static void RefreshExited(Entry entry)
    {
        if (entry.Process is not { } process || process.IsRunning)
            return;

        entry.State = HubManagedServiceStates.Exited;
        entry.RecentStderr = process.RecentStderr;
        entry.LastError ??= "Managed local service exited.";
        entry.AccessToken = null;
        entry.Endpoint = null;
    }

    private static async Task StopProcessAsync(Entry entry)
    {
        if (entry.Process is { } process)
        {
            await process.DisposeAsync();
            entry.RecentStderr = process.RecentStderr;
            entry.Process = null;
        }
        entry.Pid = null;
        entry.Endpoint = null;
        entry.AccessToken = null;
        entry.Version = null;
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static Task<IManagedLocalServiceProcess> StartProcessCoreAsync(
        ManagedServiceLaunch launch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.Executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : launch.Executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (launch.Executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(launch.Executable);

        startInfo.Environment["DOTCRAFT_MANAGED_SERVICE_ID"] = launch.ServiceId;
        startInfo.Environment["DOTCRAFT_MANAGED_SERVICE_URL"] = launch.Endpoint;
        startInfo.Environment["DOTCRAFT_MANAGED_SERVICE_TOKEN"] = launch.AccessToken;
        startInfo.Environment["DOTCRAFT_MANAGED_SERVICE_STATE_ROOT"] = launch.StateRoot;
        startInfo.Environment["DOTCRAFT_HUB_PID"] = Environment.ProcessId.ToString();
        return Task.FromResult<IManagedLocalServiceProcess>(new ManagedLocalServiceProcess(ManagedChildProcess.Start(startInfo)));
    }

    private static async Task ProbeHealthCoreAsync(string url, CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            await entry.Mutex.WaitAsync();
            try
            {
                await StopProcessAsync(entry);
                entry.State = HubManagedServiceStates.Stopped;
            }
            finally
            {
                entry.Mutex.Release();
                entry.Mutex.Dispose();
            }
        }
    }

    private sealed class Entry(string serviceId)
    {
        public SemaphoreSlim Mutex { get; } = new(1, 1);
        public string ServiceId { get; } = serviceId;
        public string State { get; set; } = HubManagedServiceStates.Stopped;
        public int? Pid { get; set; }
        public string? Endpoint { get; set; }
        public string? AccessToken { get; set; }
        public string? Version { get; set; }
        public string? LastError { get; set; }
        public string? RecentStderr { get; set; }
        public IManagedLocalServiceProcess? Process { get; set; }

        public HubManagedServiceResponse ToResponse() =>
            new(ServiceId, State, Pid, Endpoint, AccessToken, Version, LastError, RecentStderr);
    }
}

internal sealed record ManagedServiceLaunch(
    string ServiceId,
    string Executable,
    string Endpoint,
    string AccessToken,
    string StateRoot);

internal sealed record ManagedServiceReady(string ServiceId, string Endpoint, string? Version);

internal interface IManagedLocalServiceProcess : IAsyncDisposable
{
    int ProcessId { get; }
    bool IsRunning { get; }
    string? RecentStderr { get; }
    Task<ManagedServiceReady> WaitForReadyAsync(CancellationToken cancellationToken);
}

internal sealed class ManagedLocalServiceProcess : IManagedLocalServiceProcess
{
    private const string ReadyType = "dotcraft.managed-service.ready";
    private readonly ManagedChildProcess _child;
    private readonly Task _stderrTask;
    private readonly Queue<string> _stderr = new();

    public ManagedLocalServiceProcess(ManagedChildProcess child)
    {
        _child = child;
        _stderrTask = CaptureStderrAsync();
    }

    public int ProcessId => _child.Process.Id;
    public bool IsRunning => !_child.Process.HasExited;
    public string? RecentStderr
    {
        get
        {
            lock (_stderr)
                return _stderr.Count == 0 ? null : string.Join(Environment.NewLine, _stderr);
        }
    }

    public async Task<ManagedServiceReady> WaitForReadyAsync(CancellationToken cancellationToken)
    {
        while (await _child.Process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("type", out var type) && type.GetString() == ReadyType)
                {
                    return new ManagedServiceReady(
                        root.GetProperty("serviceId").GetString() ?? string.Empty,
                        root.GetProperty("endpoint").GetString() ?? string.Empty,
                        root.TryGetProperty("version", out var version) ? version.GetString() : null);
                }
            }
            catch (JsonException)
            {
                // Non-protocol startup output is ignored.
            }
        }

        throw new InvalidOperationException("Managed service exited before reporting readiness.");
    }

    public async ValueTask DisposeAsync()
    {
        await _child.DisposeAsync();
        try { await _stderrTask; } catch { }
    }

    private async Task CaptureStderrAsync()
    {
        while (await _child.Process.StandardError.ReadLineAsync() is { } line)
        {
            lock (_stderr)
            {
                _stderr.Enqueue(line);
                while (_stderr.Count > 20)
                    _stderr.Dequeue();
            }
        }
    }
}
