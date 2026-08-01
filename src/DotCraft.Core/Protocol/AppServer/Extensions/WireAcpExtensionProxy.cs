using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tracing;
using DotCraft.Protocol.Contracts;
using Contract = DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.AppServer;

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
        var el = await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpFsReadTextFile,
            new AcpFsReadTextFileParams { Path = path, Offset = offset, Limit = limit }, ct);
        if (!el.HasValue)
            return null;
        try
        {
            return el.Value.Deserialize<AcpFsReadTextFileResult>(JsonOptions)?.Content;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default)
    {
        if (!SupportsFileWrite)
            return false;
        var el = await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpFsWriteTextFile,
            new AcpFsWriteTextFileParams { Path = path, Content = content }, ct);
        if (!el.HasValue)
            return false;
        try
        {
            return el.Value.Deserialize<AcpFsWriteTextFileResult>(JsonOptions)?.Success ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> CreateTerminalAsync(string command, string? cwd = null,
        Dictionary<string, string>? env = null, CancellationToken ct = default)
    {
        if (!SupportsTerminal)
            return null;
        var el = await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalCreate,
            new AcpTerminalCreateParams { Command = command, Cwd = cwd, Env = env }, ct);
        if (!el.HasValue)
            return null;
        try
        {
            return el.Value.Deserialize<AcpTerminalCreateResult>(JsonOptions)?.TerminalId;
        }
        catch
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(string output, int? exitCode)> GetTerminalOutputAsync(string terminalId,
        CancellationToken ct = default)
    {
        var el = await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalGetOutput,
            new AcpTerminalGetOutputParams { TerminalId = terminalId }, ct);
        if (!el.HasValue)
            return ("", null);
        try
        {
            var r = el.Value.Deserialize<AcpTerminalOutputResult>(JsonOptions);
            return (r?.Output ?? "", r?.ExitCode);
        }
        catch
        {
            return ("", null);
        }
    }

    /// <inheritdoc />
    public async Task<(string output, int? exitCode)> WaitForTerminalExitAsync(string terminalId,
        int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var el = await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalWaitForExit,
            new AcpTerminalWaitForExitParams { TerminalId = terminalId, Timeout = timeoutSeconds }, ct);
        if (!el.HasValue)
            return ("", null);
        try
        {
            var r = el.Value.Deserialize<AcpTerminalOutputResult>(JsonOptions);
            return (r?.Output ?? "", r?.ExitCode);
        }
        catch
        {
            return ("", null);
        }
    }

    /// <inheritdoc />
    public async Task KillTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalKill,
            new AcpTerminalKillParams { TerminalId = terminalId }, ct);
    }

    /// <inheritdoc />
    public async Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await SendExtRawAsync(DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalRelease,
            new AcpTerminalReleaseParams { TerminalId = terminalId }, ct);
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

        if (@params is IAcpThreadBoundParams threadBoundParams)
            threadBoundParams.ThreadId = threadId;

        var mergedParams = MergeThreadIdIntoParams(threadId, @params);
        var typedResult = wireMethod switch
        {
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpFsReadTextFile => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpFsReadTextFile, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpFsWriteTextFile => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpFsWriteTextFile, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalCreate => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpTerminalCreate, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalGetOutput => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpTerminalGetOutput, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalWaitForExit => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpTerminalWaitForExit, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalKill => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpTerminalKill, mergedParams, ct, timeout),
            DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ExtAcpTerminalRelease => await SendTypedAsync(
                binding.Transport, Contract.AppServerRpc.ExtAcpTerminalRelease, mergedParams, ct, timeout),
            _ => (Handled: false, Result: (JsonElement?)null)
        };
        if (typedResult.Handled)
            return typedResult.Result;

        var response = await binding.Transport.SendClientRequestAsync(wireMethod, mergedParams, ct,
            timeout ?? TimeSpan.FromSeconds(30));
        if (!response.Result.HasValue)
            return null;
        return response.Result.Value;
    }

    private static async Task<(bool Handled, JsonElement? Result)> SendTypedAsync<TParams, TResult>(
        IAppServerTransport transport,
        RpcRequest<TParams, TResult> descriptor,
        object parameters,
        CancellationToken ct,
        TimeSpan? timeout)
        where TParams : class
        where TResult : class
    {
        var response = await transport.RequestAsync(
            descriptor,
            AppServerContractMapper.ToContract<TParams>(parameters),
            ct,
            timeout ?? TimeSpan.FromSeconds(30));
        return response.Result is null
            ? (true, null)
            : (true, JsonSerializer.SerializeToElement(
                response.Result,
                DotCraft.Protocol.Contracts.AppServerContractJson.Options));
    }

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
