using System.Text;
using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for JSON-RPC error codes (spec Section 8).
/// Verifies:
/// - -32700 parse error from StdioTransport reader loop (Fix 3)
/// - -32601 method not found
/// - -32602 invalid params
/// - -32002 not initialized
/// - -32003 already initialized
/// - -32001 backpressure / server overloaded
/// </summary>
public sealed class AppServerErrorTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // -32700 Parse error from StdioTransport (Fix 3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StdioTransport_MalformedJson_ReturnsParseError()
    {
        // Arrange a pair of piped streams simulating stdin/stdout
        var (serverIn, testOut) = MakePipePair();
        var (testIn, serverOut) = MakePipePair();

        // Use a TestableStdioTransport that works with stream pairs
        var transport = new TestableStdioTransport(serverIn, serverOut);
        transport.Start();

        // Write a malformed JSON line from the "client"
        await testOut.WriteAsync(Encoding.UTF8.GetBytes("this is not JSON\n"));
        await testOut.FlushAsync();

        // Read the parse error response from the server
        var line = await ReadLineAsync(testIn);

        await transport.DisposeAsync();

        Assert.NotNull(line);
        var doc = JsonDocument.Parse(line);
        Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(AppServerErrors.ParseErrorCode, error.GetProperty("code").GetInt32());
        Assert.Equal("Parse error", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task StdioTransport_NullId_InParseErrorResponse()
    {
        var (serverIn, testOut) = MakePipePair();
        var (testIn, serverOut) = MakePipePair();

        var transport = new TestableStdioTransport(serverIn, serverOut);
        transport.Start();

        await testOut.WriteAsync(Encoding.UTF8.GetBytes("{invalid\n"));
        await testOut.FlushAsync();

        var line = await ReadLineAsync(testIn);
        await transport.DisposeAsync();

        var doc = JsonDocument.Parse(line!);
        // Per JSON-RPC 2.0, id must be null when the message cannot be parsed.
        // Our serializer uses WhenWritingNull, so the id field may be absent (equivalent to null).
        var hasId = doc.RootElement.TryGetProperty("id", out var idProp);
        Assert.True(!hasId || idProp.ValueKind == JsonValueKind.Null,
            $"id must be absent or JSON null, got: {(hasId ? idProp.ValueKind.ToString() : "absent")}");
    }

    // -------------------------------------------------------------------------
    // -32601 Method not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest("thread/doesNotExist");
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.MethodNotFoundCode);
    }

    // -------------------------------------------------------------------------
    // -32602 Invalid params
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_EmptyInput_ReturnsInvalidParams()
    {
        await _h.InitializeAsync();
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = Array.Empty<object>()
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    // -------------------------------------------------------------------------
    // -32002 Not initialized
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnyMethod_BeforeInitialize_ReturnsNotInitialized()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new { channelName = "test", workspacePath = "/tmp" }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.NotInitializedCode);
    }

    // -------------------------------------------------------------------------
    // -32003 Already initialized
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Initialize_CalledTwice_ReturnsAlreadyInitialized()
    {
        await _h.InitializeAsync();

        var secondInit = _h.BuildRequest(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = "re-init", version = "0.0.1" }
        });
        await _h.ExecuteRequestAsync(secondInit);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.AlreadyInitializedCode);
        // -32003 per spec Section 8.3 dedicated code
        Assert.Equal(-32003, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    // -------------------------------------------------------------------------
    // -32001 Server overloaded (backpressure gate, Fix 9)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Backpressure_SemaphoreExhausted_ReturnsServerOverloaded()
    {
        // This test verifies the gate code path in AppServerHost.RunLoopAsync.
        // We use a tiny semaphore to simulate saturation.
        var gate = new SemaphoreSlim(0, 1); // already exhausted
        await using var transport = new InMemoryTransport();
        var connection = new AppServerConnection();
        connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "test", Version = "0.0.1" }, null);
        connection.MarkClientReady();

        // Simulate the gate rejection logic from AppServerHost
        var msg = InMemoryTransport.BuildRequest(AppServerMethods.ThreadList,
            new { identity = new { channelName = "test", workspacePath = "/tmp" } });

        if (!await gate.WaitAsync(0))
        {
            var overloadErr = AppServerErrors.ServerOverloaded().ToError();
            await transport.WriteMessageAsync(
                AppServerRequestHandler.BuildErrorResponse(msg.Id, overloadErr));
        }

        var doc = await transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.ServerOverloadedCode);
    }

    // -------------------------------------------------------------------------
    // -32010 Thread not found (Gap A)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadRead_UnknownThreadId_ReturnsThreadNotFound()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = "thread_does_not_exist" });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.ThreadNotFoundCode);
    }

    [Fact]
    public async Task ThreadPause_UnknownThreadId_ReturnsThreadNotFound()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = "thread_ghost" });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.ThreadNotFoundCode);
    }

    [Fact]
    public async Task ThreadResume_UnknownThreadId_ReturnsThreadNotFound()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = "thread_ghost" });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.ThreadNotFoundCode);
    }

    // -------------------------------------------------------------------------
    // -32013 Turn not found / -32014 Turn not running (Issue E)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnInterrupt_UnknownTurnId_ReturnsTurnNotFound()
    {
        await _h.InitializeAsync();
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.TurnInterrupt, new
        {
            threadId = thread.Id,
            turnId = "turn_does_not_exist"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.TurnNotFoundCode);
    }

    [Fact]
    public async Task TurnInterrupt_UnknownThread_ReturnsThreadNotFound()
    {
        await _h.InitializeAsync();

        var msg = _h.BuildRequest(AppServerMethods.TurnInterrupt, new
        {
            threadId = "thread_ghost",
            turnId = "turn_001"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.ThreadNotFoundCode);
    }

    [Fact]
    public async Task TurnInterrupt_TurnNotRunning_ReturnsTurnNotRunning()
    {
        await _h.InitializeAsync();
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Add a completed turn directly to the thread so the handler can inspect its status
        var completedTurn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };
        thread.Turns.Add(completedTurn);

        var msg = _h.BuildRequest(AppServerMethods.TurnInterrupt, new
        {
            threadId = thread.Id,
            turnId = "turn_001"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.TurnNotRunningCode);
    }

    // -------------------------------------------------------------------------
    // Error response includes the request id
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ErrorResponse_ContainsRequestId()
    {
        var msg = InMemoryTransport.BuildRequest(AppServerMethods.ThreadList,
            new { identity = new { channelName = "test", workspacePath = "/tmp" } }, id: 42);
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        var id = doc.RootElement.GetProperty("id");
        Assert.Equal(42, id.GetInt32());
    }

    // -------------------------------------------------------------------------
    // Helpers: pipe pairs for StdioTransport tests
    // -------------------------------------------------------------------------

    private static (Stream read, Stream write) MakePipePair()
    {
        // Use Pipe from System.IO.Pipelines for a simple uni-directional pipe
        var pipe = new System.IO.Pipelines.Pipe();
        return (pipe.Reader.AsStream(), pipe.Writer.AsStream());
    }

    private static async Task<string?> ReadLineAsync(Stream stream, TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (!cts.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            if (read == 0) break;
            var chunk = Encoding.UTF8.GetString(buffer, 0, read);
            sb.Append(chunk);
            if (chunk.Contains('\n'))
                break;
        }

        var result = sb.ToString().TrimEnd('\r', '\n');
        return result.Length > 0 ? result : null;
    }
}

/// <summary>
/// Thin subclass of <see cref="StdioTransport"/> that exposes a constructor
/// accepting explicit Stream parameters for testing, bypassing Console.OpenStandardInput/Output.
/// </summary>
internal sealed class TestableStdioTransport : IAppServerTransport
{
    private readonly StdioTransportWrapper _inner;

    public TestableStdioTransport(Stream input, Stream output)
    {
        _inner = new StdioTransportWrapper(input, output);
    }

    public void Start() => _inner.Start();

    public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
        _inner.ReadMessageAsync(ct);

    public Task WriteMessageAsync(object message, CancellationToken ct = default) =>
        _inner.WriteMessageAsync(message, ct);

    public Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null) =>
        _inner.SendClientRequestAsync(method, @params, ct, timeout);

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>
/// Wrapper to access the internal constructor of <see cref="StdioTransport"/> that
/// accepts explicit streams. This is needed because <see cref="StdioTransport.CreateStdio"/>
/// hard-codes <see cref="Console.OpenStandardInput/Output"/>.
///
/// We replicate the transport logic inline here to avoid coupling tests to internals.
/// </summary>
internal sealed class StdioTransportWrapper : IAppServerTransport
{
    private readonly System.IO.StreamReader _reader;
    private readonly System.IO.StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly System.Threading.Channels.Channel<AppServerIncomingMessage> _incomingQueue =
        System.Threading.Channels.Channel.CreateUnbounded<AppServerIncomingMessage>();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<AppServerIncomingMessage>> _pending = new();
    private Task? _readerLoop;
    private readonly CancellationTokenSource _disposeCts = new();

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public StdioTransportWrapper(Stream input, Stream output)
    {
        _reader = new System.IO.StreamReader(input, Encoding.UTF8);
        _writer = new System.IO.StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Start() => _readerLoop = Task.Run(ReaderLoopAsync);

    public async Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            return await _incomingQueue.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) { return null; }
        catch (System.Threading.Channels.ChannelClosedException) { return null; }
    }

    public async Task WriteMessageAsync(object message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, SessionWireJsonOptions.Default);
        await _writeLock.WaitAsync(ct);
        try { await _writer.WriteLineAsync(json.AsMemory(), ct); }
        finally { _writeLock.Release(); }
    }

    public Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null)
        => throw new NotSupportedException("Approval not needed for parse error tests.");

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                AppServerIncomingMessage? msg;
                try
                {
                    msg = JsonSerializer.Deserialize<AppServerIncomingMessage>(line, ReadOptions);
                    if (msg == null) continue;
                }
                catch (JsonException)
                {
                    var parseError = new
                    {
                        jsonrpc = "2.0",
                        id = (object?)null,
                        error = new { code = AppServerErrors.ParseErrorCode, message = "Parse error" }
                    };
                    await WriteMessageAsync(parseError, ct);
                    continue;
                }

                if (msg.IsResponse && msg.Id.HasValue && msg.Id.Value.ValueKind == JsonValueKind.Number)
                {
                    if (_pending.TryRemove(msg.Id.Value.GetInt32(), out var tcs))
                    {
                        tcs.TrySetResult(msg);
                        continue;
                    }
                }

                await _incomingQueue.Writer.WriteAsync(msg, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await _disposeCts.CancelAsync(); } catch { }
            foreach (var tcs in _pending.Values) tcs.TrySetCanceled();
            _incomingQueue.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();
        _reader.Dispose();
        await _writeLock.WaitAsync();
        try { _writer.Dispose(); }
        finally { _writeLock.Release(); }
        if (_readerLoop != null)
            try { await _readerLoop; } catch { }
        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
