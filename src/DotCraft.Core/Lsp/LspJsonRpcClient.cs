using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Lsp;

internal interface ILspJsonRpcClient
{
    bool IsStarted { get; }

    Task StartAsync(
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environmentVariables,
        string? cwd,
        CancellationToken cancellationToken);

    Task StopAsync();

    Task<JsonElement> SendRequestAsync(
        string method,
        object? @params,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken);

    void OnNotification(string method, Action<JsonElement> handler);

    void OnRequest(string method, Func<JsonElement, Task<object?>> handler);
}

internal sealed class LspJsonRpcClient(string serverName, ILogger? logger = null) : ILspJsonRpcClient
{
    internal sealed class LspProtocolException(string message, int? errorCode = null) : Exception(message)
    {
        public int? ErrorCode { get; } = errorCode;
    }

    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingResponses = new();
    private readonly ConcurrentDictionary<string, List<Action<JsonElement>>> _notificationHandlers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Func<JsonElement, Task<object?>>> _requestHandlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();

    private Process? _process;
    private Task? _readLoopTask;
    private int _nextRequestId;

    public bool IsStarted => _process is { HasExited: false };

    public async Task StartAsync(
        string command,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environmentVariables,
        string? cwd,
        CancellationToken cancellationToken)
    {
        if (IsStarted)
            return;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
        var ct = linkedCts.Token;

        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = string.IsNullOrWhiteSpace(cwd)
                ? Directory.GetCurrentDirectory()
                : cwd
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        if (environmentVariables != null)
        {
            foreach (var (key, value) in environmentVariables)
                startInfo.Environment[key] = value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start LSP server process '{serverName}'.");

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            if (!_lifetimeCts.IsCancellationRequested)
            {
                var exitCode = process.ExitCode;
                FailAllPending(
                    new LspProtocolException(
                        $"LSP server '{serverName}' exited unexpectedly with code {exitCode}."));
            }
        };

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && !process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync(ct);
                    if (line == null)
                        break;
                    if (!string.IsNullOrWhiteSpace(line))
                        logger?.LogDebug("LSP[{Server}] stderr: {Message}", serverName, line);
                }
            }
            catch (OperationCanceledException)
            {
                // expected during shutdown
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Failed to read stderr for LSP server {Server}", serverName);
            }
        }, ct);

        _process = process;
        _readLoopTask = Task.Run(() => ReadLoopAsync(ct), ct);
    }

    public async Task StopAsync()
    {
        await _lifetimeCts.CancelAsync();

        if (_process != null)
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // best-effort
            }
        }

        if (_readLoopTask != null)
        {
            try
            {
                await _readLoopTask;
            }
            catch
            {
                // best-effort
            }
        }

        _process?.Dispose();
        _process = null;

        FailAllPending(new OperationCanceledException("LSP client stopped."));
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? @params,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!IsStarted || _process == null)
            throw new InvalidOperationException($"LSP server '{serverName}' is not started.");

        var idValue = Interlocked.Increment(ref _nextRequestId);
        var id = idValue.ToString();
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingResponses[id] = tcs;

        try
        {
            await SendMessageAsync(
                new
                {
                    jsonrpc = "2.0",
                    id = idValue,
                    method,
                    @params
                },
                cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            timeoutCts.CancelAfter(timeout);
            using var registration = timeoutCts.Token.Register(() => tcs.TrySetCanceled(timeoutCts.Token));
            return await tcs.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"LSP request '{method}' timed out after {timeout.TotalMilliseconds} ms for server '{serverName}'.");
        }
        finally
        {
            _pendingResponses.TryRemove(id, out _);
        }
    }

    public Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken)
    {
        return SendMessageAsync(
            new
            {
                jsonrpc = "2.0",
                method,
                @params
            },
            cancellationToken);
    }

    public void OnNotification(string method, Action<JsonElement> handler)
    {
        _notificationHandlers.AddOrUpdate(
            method,
            _ => [handler],
            (_, existing) =>
            {
                existing.Add(handler);
                return existing;
            });
    }

    public void OnRequest(string method, Func<JsonElement, Task<object?>> handler)
    {
        _requestHandlers[method] = handler;
    }

    private async Task SendMessageAsync(object payload, CancellationToken cancellationToken)
    {
        if (!IsStarted || _process == null)
            throw new InvalidOperationException($"LSP server '{serverName}' is not started.");

        var json = JsonSerializer.Serialize(payload);
        var contentBytes = Encoding.UTF8.GetBytes(json);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {contentBytes.Length}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.BaseStream.WriteAsync(headerBytes, cancellationToken);
            await _process.StandardInput.BaseStream.WriteAsync(contentBytes, cancellationToken);
            await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        if (_process == null)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested && !_process.HasExited)
            {
                var contentLength = await ReadContentLengthAsync(_process.StandardOutput.BaseStream, cancellationToken);
                if (contentLength == null)
                    return;

                var payloadBytes = await ReadExactAsync(_process.StandardOutput.BaseStream, contentLength.Value, cancellationToken);
                using var doc = JsonDocument.Parse(payloadBytes);
                await HandleIncomingMessageAsync(doc.RootElement, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected during shutdown
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "LSP read loop failed for server {Server}", serverName);
            FailAllPending(new LspProtocolException($"LSP connection failed for server '{serverName}': {ex.Message}"));
        }
    }

    private async Task HandleIncomingMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        if (message.TryGetProperty("id", out var idElement)
            && message.TryGetProperty("method", out var methodElement)
            && methodElement.ValueKind == JsonValueKind.String)
        {
            // Server request
            var method = methodElement.GetString()!;
            var @params = message.TryGetProperty("params", out var requestParams)
                ? requestParams.Clone()
                : default;

            if (_requestHandlers.TryGetValue(method, out var handler))
            {
                try
                {
                    var response = await handler(@params);
                    await SendMessageAsync(
                        new
                        {
                            jsonrpc = "2.0",
                            id = ConvertId(idElement),
                            result = response
                        },
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    await SendMessageAsync(
                        new
                        {
                            jsonrpc = "2.0",
                            id = ConvertId(idElement),
                            error = new
                            {
                                code = -32000,
                                message = ex.Message
                            }
                        },
                        cancellationToken);
                }
            }
            else
            {
                await SendMessageAsync(
                    new
                    {
                        jsonrpc = "2.0",
                        id = ConvertId(idElement),
                        result = (object?)null
                    },
                    cancellationToken);
            }

            return;
        }

        if (message.TryGetProperty("method", out var notifMethodElement)
            && notifMethodElement.ValueKind == JsonValueKind.String
            && !message.TryGetProperty("id", out _))
        {
            // Server notification
            var method = notifMethodElement.GetString()!;
            var @params = message.TryGetProperty("params", out var notificationParams)
                ? notificationParams.Clone()
                : default;
            if (_notificationHandlers.TryGetValue(method, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler(@params);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogDebug(ex, "LSP notification handler failed for {Server}:{Method}", serverName, method);
                    }
                }
            }

            return;
        }

        if (message.TryGetProperty("id", out var responseId))
        {
            // Response to previous request
            var key = responseId.ToString();
            if (_pendingResponses.TryGetValue(key, out var tcs))
            {
                if (message.TryGetProperty("error", out var errorElement))
                {
                    var code = errorElement.TryGetProperty("code", out var codeElement)
                        && codeElement.ValueKind == JsonValueKind.Number
                            ? codeElement.GetInt32()
                            : (int?)null;
                    var errorMessage = errorElement.TryGetProperty("message", out var msgElement)
                                       && msgElement.ValueKind == JsonValueKind.String
                        ? msgElement.GetString() ?? "Unknown LSP error"
                        : "Unknown LSP error";
                    tcs.TrySetException(new LspProtocolException(errorMessage, code));
                }
                else if (message.TryGetProperty("result", out var resultElement))
                {
                    tcs.TrySetResult(resultElement.Clone());
                }
                else
                {
                    tcs.TrySetResult(default);
                }
            }
        }
    }

    private static object? ConvertId(JsonElement idElement)
    {
        return idElement.ValueKind switch
        {
            JsonValueKind.Number when idElement.TryGetInt64(out var l) => l,
            JsonValueKind.String => idElement.GetString(),
            _ => null
        };
    }

    private static async Task<int?> ReadContentLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(256);
        var oneByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken);
            if (read == 0)
            {
                return headerBytes.Count == 0 ? null : throw new EndOfStreamException("Unexpected EOF while reading LSP headers.");
            }

            headerBytes.Add(oneByte[0]);
            var count = headerBytes.Count;
            if (count >= 4
                && headerBytes[count - 4] == '\r'
                && headerBytes[count - 3] == '\n'
                && headerBytes[count - 2] == '\r'
                && headerBytes[count - 1] == '\n')
            {
                break;
            }

            if (count > 16 * 1024)
                throw new InvalidDataException("LSP header block is too large.");
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();
            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value, out var contentLength)
                && contentLength >= 0)
            {
                return contentLength;
            }
        }

        throw new InvalidDataException("Missing Content-Length header in LSP message.");
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("Unexpected EOF while reading LSP payload.");
            offset += read;
        }

        return buffer;
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var tcs in _pendingResponses.Values)
            tcs.TrySetException(ex);
    }
}
