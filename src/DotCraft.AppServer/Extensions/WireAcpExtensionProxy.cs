using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tracing;
using DotCraft.Tools;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Routes agent-side ACP extension calls to the wire client bound to the current thread
/// (appserver-protocol.md §11.2 per-thread binding).
/// </summary>
public sealed class WireAcpExtensionProxy : IAcpExtensionProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, AcpThreadBinding> _byThread = new();

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions
    {
        get
        {
            var threadId = TracingChatClient.CurrentSessionKey;
            if (threadId == null || !_byThread.TryGetValue(threadId, out var b))
                return [];
            return b.Connection.AcpCustomExtensions;
        }
    }

    /// <inheritdoc />
    public bool SupportsFileRead => GetCurrentBinding()?.Connection.SupportsAcpFsRead == true;

    /// <inheritdoc />
    public bool SupportsFileWrite => GetCurrentBinding()?.Connection.SupportsAcpFsWrite == true;

    /// <inheritdoc />
    public bool SupportsTerminal => GetCurrentBinding()?.Connection.SupportsAcpTerminal == true;

    /// <summary>
    /// Binds a thread to the transport that created it so <c>ext/acp/*</c> calls route correctly.
    /// </summary>
    public void BindThread(string threadId, IAppServerTransport transport, AppServerConnection connection)
    {
        if (!connection.HasAcpExtensions)
            return;
        _byThread[threadId] = new AcpThreadBinding(threadId, transport, connection);
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport))
                _byThread.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Removes a single thread binding (e.g. after archive).
    /// </summary>
    public void UnbindThread(string threadId) => _byThread.TryRemove(threadId, out _);

    private AcpThreadBinding? GetCurrentBinding()
    {
        var threadId = TracingChatClient.CurrentSessionKey;
        if (threadId == null)
            return null;
        return _byThread.TryGetValue(threadId, out var b) ? b : null;
    }

    /// <inheritdoc />
    public async Task<string?> ReadTextFileAsync(string path, int? offset = null, int? limit = null,
        CancellationToken ct = default)
    {
        if (!SupportsFileRead)
            return null;
        var result = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpFsReadTextFile,
            threadId => new Contract.AcpFsReadTextFileParams
            {
                ThreadId = threadId,
                Path = path,
                Offset = offset,
                Limit = limit
            },
            ct);
        return result is null ? null : ValueOrDefault(result.Content);
    }

    /// <inheritdoc />
    public async Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default)
    {
        if (!SupportsFileWrite)
            return false;
        var result = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpFsWriteTextFile,
            threadId => new Contract.AcpFsWriteTextFileParams
            {
                ThreadId = threadId,
                Path = path,
                Content = content
            },
            ct);
        return result is not null && ValueOrDefault(result.Success);
    }

    /// <inheritdoc />
    public async Task<string?> CreateTerminalAsync(string command, string? cwd = null,
        Dictionary<string, string>? env = null, CancellationToken ct = default)
    {
        if (!SupportsTerminal)
            return null;
        var result = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpTerminalCreate,
            threadId => new Contract.AcpTerminalCreateParams
            {
                ThreadId = threadId,
                Command = command,
                Cwd = cwd,
                Env = env
            },
            ct);
        return result is null ? null : ValueOrDefault(result.TerminalId);
    }

    /// <inheritdoc />
    public async Task<(string output, int? exitCode)> GetTerminalOutputAsync(string terminalId,
        CancellationToken ct = default)
    {
        var result = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpTerminalGetOutput,
            threadId => new Contract.AcpTerminalGetOutputParams { ThreadId = threadId, TerminalId = terminalId },
            ct);
        return result is null
            ? ("", null)
            : (ValueOrDefault(result.Output) ?? "", ValueOrDefault(result.ExitCode));
    }

    /// <inheritdoc />
    public async Task<(string output, int? exitCode)> WaitForTerminalExitAsync(string terminalId,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var result = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpTerminalWaitForExit,
            threadId => new Contract.AcpTerminalWaitForExitParams
            {
                ThreadId = threadId,
                TerminalId = terminalId,
                Timeout = timeoutSeconds
            },
            ct);
        return result is null
            ? ("", null)
            : (ValueOrDefault(result.Output) ?? "", ValueOrDefault(result.ExitCode));
    }

    /// <inheritdoc />
    public async Task KillTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        _ = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpTerminalKill,
            threadId => new Contract.AcpTerminalKillParams { ThreadId = threadId, TerminalId = terminalId },
            ct);
    }

    /// <inheritdoc />
    public async Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        _ = await SendTypedAsync(
            Contract.AppServerRpc.ExtAcpTerminalRelease,
            threadId => new Contract.AcpTerminalReleaseParams { ThreadId = threadId, TerminalId = terminalId },
            ct);
    }

    /// <inheritdoc />
    public async Task<T?> SendExtensionAsync<T>(string method, object? @params,
        CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var wireMethod = MapToWireMethod(method);
        var el = await SendExtRawAsync(wireMethod, @params, ct, timeout);
        if (!el.HasValue)
            return default;
        try
        {
            return el.Value.Deserialize<T>(JsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private async Task<JsonElement?> SendExtRawAsync(string wireMethod, object? @params,
        CancellationToken ct, TimeSpan? timeout = null)
    {
        var threadId = TracingChatClient.CurrentSessionKey;
        if (threadId == null || !_byThread.TryGetValue(threadId, out var binding))
            return null;

        var mergedParams = MergeThreadIdIntoParams(threadId, @params);
        var response = await binding.Transport.SendClientRequestAsync(wireMethod, mergedParams, ct,
            timeout ?? TimeSpan.FromSeconds(30));
        if (!response.Result.HasValue)
            return null;
        return response.Result.Value;
    }

    private async Task<TResult?> SendTypedAsync<TParams, TResult>(
        RpcRequest<TParams, TResult> descriptor,
        Func<string, TParams> createParameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
        where TParams : class
        where TResult : class
    {
        var threadId = TracingChatClient.CurrentSessionKey;
        if (threadId == null || !_byThread.TryGetValue(threadId, out var binding))
            return null;
        var response = await binding.Transport.RequestAsync(
            descriptor,
            createParameters(threadId),
            ct,
            timeout ?? TimeSpan.FromSeconds(30));
        return response.Result;
    }

    private static T? ValueOrDefault<T>(Optional<T> value) =>
        value.IsSet ? value.Value : default;

    /// <summary>
    /// Ensures server→client <c>ext/acp/*</c> params include <c>threadId</c> so multi-session bridges can route.
    /// </summary>
    internal static object MergeThreadIdIntoParams(string threadId, object? @params)
    {
        if (@params == null)
            return new { threadId };

        var node = JsonSerializer.SerializeToNode(@params, JsonOptions);
        if (node is JsonObject o)
        {
            o["threadId"] = threadId;
            return o;
        }

        return new { threadId, payload = @params };
    }

    /// <summary>Maps an ACP IDE method name to the wire <c>ext/acp/...</c> form.</summary>
    public static string MapToWireMethod(string method)
    {
        if (string.IsNullOrEmpty(method))
            return method;
        if (method.StartsWith("ext/acp/", StringComparison.Ordinal))
            return method;
        return $"ext/acp/{method.TrimStart('/')}";
    }

    private sealed record AcpThreadBinding(string ThreadId, IAppServerTransport Transport, AppServerConnection Connection);

}
