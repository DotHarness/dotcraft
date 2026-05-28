using System.Reflection;
using System.Text.Json;
using DotCraft.Lsp;

namespace DotCraft.Tests.Lsp;

public class LspServerInstanceTests
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task StartAsync_WhenRecoveringFromError_StopsExistingClientBeforeCreatingNewOne()
    {
        var events = new List<string>();
        var firstClient = new FakeLspJsonRpcClient("first", events);
        var secondClient = new FakeLspJsonRpcClient("second", events);
        var clients = new Queue<ILspJsonRpcClient>([firstClient, secondClient]);
        var instance = CreateInstance(() => clients.Dequeue());

        await instance.StartAsync();

        firstClient.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : throw new InvalidOperationException("connection lost");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => instance.SendRequestAsync("workspace/symbol", null, RequestTimeout));

        Assert.Equal("connection lost", ex.Message);
        Assert.Equal(LspServerState.Error, instance.State);
        Assert.Equal(0, firstClient.StopCallCount);

        await instance.StartAsync();

        Assert.Equal(
            ["first:start", "first:request:initialize", "first:notification:initialized", "first:request:workspace/symbol", "first:stop", "second:start", "second:request:initialize", "second:notification:initialized"],
            events);
        Assert.Equal(1, firstClient.StopCallCount);
        Assert.Equal(1, secondClient.StartCallCount);
        Assert.Equal(LspServerState.Running, instance.State);
    }

    [Fact]
    public async Task SendRequestAsync_WhenTimeoutOccurs_KeepsServerRunning()
    {
        var client = new FakeLspJsonRpcClient("timeout");
        var instance = CreateStartedInstance(client);

        client.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : throw new TimeoutException("request timed out");

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => instance.SendRequestAsync("textDocument/hover", null, RequestTimeout));

        Assert.Equal("request timed out", ex.Message);
        Assert.Equal(LspServerState.Running, instance.State);
        Assert.Null(instance.LastError);
    }

    [Fact]
    public async Task SendRequestAsync_WhenProtocolErrorHasCode_KeepsServerRunning()
    {
        var client = new FakeLspJsonRpcClient("protocol");
        var instance = CreateStartedInstance(client);

        client.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : throw new LspJsonRpcClient.LspProtocolException("Method not found", -32601);

        var ex = await Assert.ThrowsAsync<LspJsonRpcClient.LspProtocolException>(
            () => instance.SendRequestAsync("workspace/unknownMethod", null, RequestTimeout));

        Assert.Equal(-32601, ex.ErrorCode);
        Assert.Equal(LspServerState.Running, instance.State);
        Assert.Null(instance.LastError);
    }

    [Fact]
    public async Task SendRequestAsync_WhenConnectionBreaks_TransitionsToErrorAndRecordsLastError()
    {
        var client = new FakeLspJsonRpcClient("fatal");
        var instance = CreateStartedInstance(client);

        client.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : throw new LspJsonRpcClient.LspProtocolException("LSP connection failed", null);

        var ex = await Assert.ThrowsAsync<LspJsonRpcClient.LspProtocolException>(
            () => instance.SendRequestAsync("textDocument/references", null, RequestTimeout));

        Assert.Equal(LspServerState.Error, instance.State);
        Assert.Same(ex, instance.LastError);
    }

    [Fact]
    public async Task SendRequestAsync_WhenContentModifiedRetriesExhausted_KeepsServerRunning()
    {
        var client = new FakeLspJsonRpcClient("content-modified");
        var attempts = 0;
        var instance = CreateStartedInstance(client, (_, _) => Task.CompletedTask);

        client.RequestHandler = method =>
        {
            if (method == "initialize")
                return JsonSerializer.SerializeToElement(new { });

            attempts++;
            throw new LspJsonRpcClient.LspProtocolException("content modified", -32801);
        };

        var ex = await Assert.ThrowsAsync<LspJsonRpcClient.LspProtocolException>(
            () => instance.SendRequestAsync("textDocument/definition", null, RequestTimeout));

        Assert.Equal(-32801, ex.ErrorCode);
        Assert.Equal(4, attempts);
        Assert.Equal(LspServerState.Running, instance.State);
        Assert.Null(instance.LastError);
    }

    [Fact]
    public async Task SendRequestAsync_WaitsForStateLockBeforeTransitioningToError()
    {
        var client = new FakeLspJsonRpcClient("sync");
        var instance = CreateStartedInstance(client);
        var stateLock = GetStateLock(instance);

        client.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : throw new InvalidOperationException("transport closed");

        Task<JsonElement> sendTask;
        await stateLock.WaitAsync();
        try
        {
            sendTask = instance.SendRequestAsync("textDocument/hover", null, RequestTimeout);

            await Task.Delay(100);

            Assert.Equal(LspServerState.Running, instance.State);
            Assert.False(sendTask.IsCompleted);
        }
        finally
        {
            stateLock.Release();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sendTask);

        Assert.Equal("transport closed", ex.Message);
        Assert.Equal(LspServerState.Error, instance.State);
    }

    private static LspServerInstance CreateStartedInstance(
        FakeLspJsonRpcClient client,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        client.RequestHandler = method => method == "initialize"
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(new { ok = true });

        var instance = CreateInstance(() => client, delayAsync);
        instance.StartAsync().GetAwaiter().GetResult();
        return instance;
    }

    private static LspServerInstance CreateInstance(
        Func<ILspJsonRpcClient> clientFactory,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        return new LspServerInstance(
            "test",
            new LspServerConfig
            {
                Name = "test",
                Command = "test-lsp",
                Arguments = [],
                ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
            },
            Path.GetTempPath(),
            logger: null,
            clientFactory,
            delayAsync);
    }

    private static SemaphoreSlim GetStateLock(LspServerInstance instance)
    {
        return (SemaphoreSlim)(typeof(LspServerInstance)
            .GetField("_stateLock", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!);
    }

    private sealed class FakeLspJsonRpcClient(string name, List<string>? events = null) : ILspJsonRpcClient
    {
        public Func<string, JsonElement>? RequestHandler { get; set; }

        public bool IsStarted { get; private set; }

        public int StartCallCount { get; private set; }

        public int StopCallCount { get; private set; }

        public Task StartAsync(
            string command,
            IReadOnlyList<string> args,
            IReadOnlyDictionary<string, string>? environmentVariables,
            string? cwd,
            CancellationToken cancellationToken)
        {
            IsStarted = true;
            StartCallCount++;
            events?.Add($"{name}:start");
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsStarted = false;
            StopCallCount++;
            events?.Add($"{name}:stop");
            return Task.CompletedTask;
        }

        public Task<JsonElement> SendRequestAsync(
            string method,
            object? @params,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            events?.Add($"{name}:request:{method}");
            if (RequestHandler == null)
                return Task.FromResult(JsonSerializer.SerializeToElement(new { }));

            return Task.FromResult(RequestHandler(method));
        }

        public Task SendNotificationAsync(string method, object? @params, CancellationToken cancellationToken)
        {
            events?.Add($"{name}:notification:{method}");
            return Task.CompletedTask;
        }

        public void OnNotification(string method, Action<JsonElement> handler)
        {
        }

        public void OnRequest(string method, Func<JsonElement, Task<object?>> handler)
        {
        }
    }
}
