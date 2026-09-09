using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;


namespace DotCraft.Unity;

/// <summary>Local Mono Editor connection and target-side execution.</summary>
internal sealed class UnityAttachService(string cacheRoot, string nativeBootstrapPath) : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ConcurrentDictionary<TargetIdentity, byte> owned = new();
    private readonly ConcurrentDictionary<string, TargetIdentity> selected = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ExecutionHandle> executions = new(StringComparer.Ordinal);
    private readonly Dictionary<int, DateTime> uncertain = new();
    private bool disposed;

    /// <summary>Lists running local Unity Editors without attaching.</summary>
    public object List() => Process.GetProcessesByName("Unity").Select(p =>
    {
        using (p) return new { pid = p.Id, startedUtc = p.StartTime.ToUniversalTime(), editor = p.MainModule?.FileName };
    }).ToArray();

    private string Connection(int pid) => Path.Combine(cacheRoot, "sessions", pid + ".json");

    /// <summary>Attaches or restores the bridge without replaying an execution.</summary>
    public async Task<JsonObject> Connect(string threadId, int? requestedPid, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var pid = requestedPid ?? RequireTarget(threadId).Pid;
            using var target = GetUnityProcess(pid);
            var identity = new TargetIdentity(pid, target.StartTime.ToUniversalTime());
            var path = Connection(pid);
            if (File.Exists(path))
            {
                try
                {
                    var current = await BridgeClient.Call(path, "metadata");
                    if (current["result"]?["asyncExecution"]?.GetValue<bool>() == true)
                    {
                        uncertain.Remove(pid);
                        RecordSelection(threadId, identity, current);
                        return current;
                    }
                    await BridgeClient.Call(path, "stop");
                }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Sockets.SocketException or InvalidOperationException or ArgumentException) { }
            }
            if (!File.Exists(nativeBootstrapPath)) throw new FileNotFoundException("Native bootstrap is not configured.", nativeBootstrapPath);
            if (uncertain.TryGetValue(pid, out var started) && started == identity.StartedUtc)
                throw new InvalidOperationException("Bootstrap outcome is unknown. No new bootstrap was sent; wait for its handshake or restart the isolated test session.");
            var data = Path.Combine(Path.GetDirectoryName(target.MainModule!.FileName)!, "Data");
            var payloadSource = Path.Combine(cacheRoot, "payload");
            Directory.CreateDirectory(payloadSource);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var owner = typeof(UnityAttachService).Assembly;
            foreach (var name in owner.GetManifestResourceNames().Where(n => n.EndsWith(".cs", StringComparison.Ordinal)))
            {
                using var reader = new StreamReader(owner.GetManifestResourceStream(name)!);
                File.WriteAllText(Path.Combine(payloadSource, name), reader.ReadToEnd());
            }
            var references = Directory.GetFiles(Path.Combine(data, "Managed/UnityEngine"), "*.dll")
                .Concat(new[] { "mscorlib.dll", "System.dll", "System.Core.dll" }.Select(n => Path.Combine(data, "MonoBleedingEdge/lib/mono/unityjit-win32", n)))
                .Append(Path.Combine(data, "Managed/Newtonsoft.Json.dll"))
                .Concat(Directory.GetFiles(Path.Combine(data, "MonoBleedingEdge/lib/mono/unityjit-win32/Facades"), "*.dll"));
            var payload = TargetCompiler.Compile(payloadSource, references, Path.Combine(cacheRoot, "cache"));
            cancellationToken.ThrowIfCancellationRequested();
            uncertain[pid] = identity.StartedUtc;
            try { NativeInjector.Inject(pid, nativeBootstrapPath, payload, path); }
            catch (TimeoutException) { throw; }
            catch { uncertain.Remove(pid); throw; }
            owned.TryAdd(identity, 0);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var result = await BridgeClient.Call(path, "metadata");
                        uncertain.Remove(pid);
                        RecordSelection(threadId, identity, result);
                        return result;
                    }
                    catch (Exception e) when (e is IOException or InvalidDataException or System.Net.Sockets.SocketException or System.Text.Json.JsonException) { }
                }
                await Task.Delay(100, cancellationToken);
            }
            throw new TimeoutException("Bootstrap loaded but no main-thread handshake arrived.");
        }
        finally { gate.Release(); }
    }

    /// <summary>Reads live connection metadata without reconnecting.</summary>
    public Task<JsonObject> Status(string threadId)
    {
        var target = RequireTarget(threadId);
        return BridgeClient.Call(Connection(target.Pid), "metadata");
    }

    /// <summary>Compiles for the target and starts one execution; failures are never replayed.</summary>
    public async Task<JsonObject> Execute(
        string threadId,
        string code,
        JsonObject? args,
        bool runInBackground,
        int yieldTimeMs,
        CancellationToken cancellationToken = default)
    {
        var target = RequireTarget(threadId);
        var connection = Connection(target.Pid);
        var prepared = await BridgeClient.Prepare(connection, code, cacheRoot);
        var handle = new ExecutionHandle(threadId, target, prepared.Generation, connection);
        executions[prepared.ExecutionId] = handle;
        try
        {
            var started = await BridgeClient.Start(connection, prepared, args, cancellationToken);
            if (IsTerminal(started)) return started;
            if (runInBackground)
            {
                try
                {
                    return await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation,
                        NormalizeWait(yieldTimeMs), terminate: false, cancellationToken: CancellationToken.None);
                }
                catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or TimeoutException)
                {
                    return Unknown(prepared.ExecutionId, prepared.Generation);
                }
            }

            while (true)
            {
                try
                {
                    var result = await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation,
                        1000, terminate: false, cancellationToken: cancellationToken);
                    if (IsTerminal(result)) return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await BridgeClient.Wait(connection, prepared.ExecutionId, prepared.Generation,
                            0, terminate: true, cancellationToken: CancellationToken.None);
                    }
                    catch { }
                    throw;
                }
            }
        }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or TimeoutException)
        {
            return Unknown(prepared.ExecutionId, prepared.Generation);
        }
    }

    /// <summary>Waits for or cooperatively cancels an execution created by this task.</summary>
    public async Task<JsonObject> Wait(
        string threadId,
        string executionId,
        int yieldTimeMs,
        bool terminate,
        CancellationToken cancellationToken = default)
    {
        if (!executions.TryGetValue(executionId, out var handle)
            || !string.Equals(handle.ThreadId, threadId, StringComparison.Ordinal))
            throw new UnityTargetException("UnityExecutionUnavailable", "The Unity execution does not belong to this task or is no longer available.");

        try
        {
            using var process = GetUnityProcess(handle.Target.Pid);
            if (process.StartTime.ToUniversalTime() != handle.Target.StartedUtc)
                return Lost(executionId, handle.Generation);
            if (!File.Exists(handle.ConnectionPath)
                || !string.Equals(BridgeClient.ReadGeneration(handle.ConnectionPath), handle.Generation, StringComparison.Ordinal))
                return Lost(executionId, handle.Generation);
            return await BridgeClient.Wait(handle.ConnectionPath, executionId, handle.Generation,
                NormalizeWait(yieldTimeMs), terminate, cancellationToken);
        }
        catch (UnityTargetException) { return Lost(executionId, handle.Generation); }
        catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidDataException or TimeoutException)
        {
            return Lost(executionId, handle.Generation);
        }
    }

    /// <summary>Stops the target bridge without claiming to unload assemblies.</summary>
    public async Task<JsonObject> Disconnect(string threadId)
    {
        var target = RequireTarget(threadId);
        var result = await BridgeClient.Call(Connection(target.Pid), "stop");
        if (result["state"]?.GetValue<string>() == "completed")
        {
            RemoveSelections(target);
            owned.TryRemove(target, out _);
        }
        return result;
    }

    private static Process GetUnityProcess(int pid)
    {
        Process target;
        try { target = Process.GetProcessById(pid); }
        catch (ArgumentException) { throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor is no longer running. Call unity.list, then unity.connect with a PID."); }
        try
        {
            if (!target.ProcessName.Equals("Unity", StringComparison.OrdinalIgnoreCase))
                throw new UnityTargetException("UnityTargetUnavailable", "The selected process is not a Unity Editor. Call unity.list, then unity.connect with a PID.");
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    private TargetIdentity RequireTarget(string threadId)
    {
        if (!selected.TryGetValue(threadId, out var selection))
            throw new UnityTargetException("UnityTargetRequired", "No Unity Editor is connected for this task. Call unity.list, then unity.connect with a PID.");
        try
        {
            using var process = GetUnityProcess(selection.Pid);
            if (process.StartTime.ToUniversalTime() == selection.StartedUtc) return selection;
        }
        catch (UnityTargetException) { }
        RemoveSelections(selection);
        throw new UnityTargetException("UnityTargetUnavailable", "The selected Unity Editor is no longer running. Call unity.list, then unity.connect with a PID.");
    }

    private void RecordSelection(string threadId, TargetIdentity identity, JsonObject response)
    {
        if (response["state"]?.GetValue<string>() != "completed") return;
        selected[threadId] = identity;
        owned.TryAdd(identity, 0);
    }

    private void RemoveSelections(TargetIdentity identity)
    {
        foreach (var pair in selected.Where(pair => pair.Value == identity))
            selected.TryRemove(pair);
    }

    /// <summary>Stops only bridges connected by this service generation; never reconnects during cleanup.</summary>
    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            await Task.WhenAll(owned.Keys.Select(async target =>
            {
                try { await BridgeClient.Call(Connection(target.Pid), "stop"); }
                catch (Exception e)
                {
                    Directory.CreateDirectory(cacheRoot);
                    await File.WriteAllTextAsync(Path.Combine(cacheRoot, $"cleanup-{target.Pid}.txt"), e.ToString());
                }
            }));
            selected.Clear();
            executions.Clear();
        }
        finally { gate.Release(); }
    }

    private readonly record struct TargetIdentity(int Pid, DateTime StartedUtc);
    private sealed record ExecutionHandle(string ThreadId, TargetIdentity Target, string Generation, string ConnectionPath);

    private static int NormalizeWait(int waitTimeMs) => Math.Clamp(waitTimeMs, 0, 30_000);

    private static bool IsTerminal(JsonObject result) => result["state"]?.GetValue<string>() is "completed" or "failed" or "cancelled" or "lost";

    private static JsonObject Lost(string executionId, string generation) => new()
    {
        ["state"] = "lost",
        ["executionId"] = executionId,
        ["generation"] = generation
    };

    private static JsonObject Unknown(string executionId, string generation) => new()
    {
        ["state"] = "unknown",
        ["executionId"] = executionId,
        ["generation"] = generation
    };
}

internal sealed class UnityTargetException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
