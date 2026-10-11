using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.GenerativeUi;
using DotCraft.Sessions;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class GenerativeUiStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-generative-ui-state-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Update_ReplacesAndClearsStatesReturnedByRead()
    {
        var table = await CreateTableAsync(CapableConnection());

        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 1, state = new { tab = "a" } });
        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 0, state = new { tab = "b" } });
        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 1, state = new { tab = "c" } });

        var states = (await ReadAsync(table)).States;
        Assert.Equal([0, 1], states.Select(state => state.BlockIndex));
        Assert.Equal("b", states[0].State.GetProperty("tab").GetString());
        Assert.Equal("c", states[1].State.GetProperty("tab").GetString());
        Assert.All(states, state => Assert.Equal(("turn_test", "item_agent"), (state.TurnId, state.ItemId)));

        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 0, state = (object?)null });
        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 1 });

        Assert.Empty((await ReadAsync(table)).States);
    }

    [Theory]
    [InlineData("thread_missing", "turn_test", "item_agent", 0, "not_found")]
    [InlineData("thread_test", "turn_test", "item_missing", 0, "not_found")]
    [InlineData("thread_test", "turn_test", "item_user", 0, "not_found")]
    [InlineData("thread_test", "turn_test", "item_agent", -1, "invalid_block")]
    public async Task Update_RejectsUnknownBlocks(string threadId, string turnId, string itemId, int blockIndex, string code)
    {
        var table = await CreateTableAsync(CapableConnection());

        var error = await Assert.ThrowsAsync<AppServerException>(() =>
            UpdateAsync(table, new { threadId, turnId, itemId, blockIndex, state = new { tab = "a" } }));

        Assert.Equal(code, Assert.IsType<AppServerErrorData>(error.ErrorData).Code);
        Assert.Empty((await ReadAsync(table)).States);
    }

    [Fact]
    public async Task Update_RejectsOversizedState()
    {
        var table = await CreateTableAsync(CapableConnection());

        var error = await Assert.ThrowsAsync<AppServerException>(() => UpdateAsync(
            table,
            new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 0, state = new string('x', 16 * 1024) }));

        Assert.Equal("state_too_large", Assert.IsType<AppServerErrorData>(error.ErrorData).Code);
        Assert.Empty((await ReadAsync(table)).States);
    }

    [Fact]
    public async Task Read_RejectsUnknownThread()
    {
        var table = await CreateTableAsync(CapableConnection());
        Assert.True(table.TryGet(Contract.AppServerMethodNames.GenerativeUiStateRead, out var read));

        var error = await Assert.ThrowsAsync<AppServerException>(() => read(
            InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.GenerativeUiStateRead, new { threadId = "thread_missing" }),
            CancellationToken.None));

        Assert.Equal("not_found", Assert.IsType<AppServerErrorData>(error.ErrorData).Code);
    }

    [Fact]
    public async Task Update_NotifiesOtherCapableSubscribersOfTheThread()
    {
        var subscribers = new AppServerThreadSubscribers();
        var sender = Register(subscribers, CapableConnection(), subscribe: true);
        var peer = Register(subscribers, CapableConnection(), subscribe: true);
        var incapable = Register(subscribers, IncapableConnection(), subscribe: true);
        var unsubscribed = Register(subscribers, CapableConnection(), subscribe: false);
        var table = await CreateTableAsync(sender.Connection, subscribers);

        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 1, state = new { tab = "a" } });
        var updated = (await peer.Transport.ReadNextSentAsync()).RootElement;
        Assert.Equal(Contract.AppServerMethodNames.GenerativeUiStateUpdated, updated.GetProperty("method").GetString());
        var parameters = updated.GetProperty("params");
        Assert.Equal("thread_test", parameters.GetProperty("threadId").GetString());
        Assert.Equal("turn_test", parameters.GetProperty("turnId").GetString());
        Assert.Equal("item_agent", parameters.GetProperty("itemId").GetString());
        Assert.Equal(1, parameters.GetProperty("blockIndex").GetInt32());
        Assert.Equal("a", parameters.GetProperty("state").GetProperty("tab").GetString());

        await UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 1 });
        var cleared = (await peer.Transport.ReadNextSentAsync()).RootElement.GetProperty("params");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("state").ValueKind);

        Assert.Empty(sender.Transport.DrainSent());
        Assert.Empty(incapable.Transport.DrainSent());
        Assert.Empty(unsubscribed.Transport.DrainSent());
    }

    [Fact]
    public async Task Methods_RequireTheClientCapability()
    {
        var table = await CreateTableAsync(IncapableConnection());

        var readError = await Assert.ThrowsAsync<AppServerException>(() => ReadAsync(table));
        var updateError = await Assert.ThrowsAsync<AppServerException>(() =>
            UpdateAsync(table, new { threadId = "thread_test", turnId = "turn_test", itemId = "item_agent", blockIndex = 0, state = 1 }));

        Assert.Equal(AppServerErrors.MethodNotFoundCode, readError.Code);
        Assert.Equal(AppServerErrors.MethodNotFoundCode, updateError.Code);
    }

    private async Task<AppServerMethodTable> CreateTableAsync(AppServerConnection connection, AppServerThreadSubscribers? subscribers = null)
    {
        Directory.CreateDirectory(_root);
        var sessions = new TestableSessionService(new ThreadStore(_root));
        await sessions.SeedThreadAsync(CreateThread());
        var table = new AppServerMethodTable();
        new GenerativeUiRequestHandler(sessions, connection, new GenerativeUiRuntimeRegistry(), subscribers).RegisterMethods(table);
        return table;
    }

    private static async Task<Contract.GenerativeUiStateReadResult> ReadAsync(AppServerMethodTable table)
    {
        Assert.True(table.TryGet(Contract.AppServerMethodNames.GenerativeUiStateRead, out var read));
        return Assert.IsType<Contract.GenerativeUiStateReadResult>(await read(
            InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.GenerativeUiStateRead, new { threadId = "thread_test" }),
            CancellationToken.None));
    }

    private static async Task UpdateAsync(AppServerMethodTable table, object parameters)
    {
        Assert.True(table.TryGet(Contract.AppServerMethodNames.GenerativeUiStateUpdate, out var update));
        await update(
            InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.GenerativeUiStateUpdate, parameters),
            CancellationToken.None);
    }

    private SessionThread CreateThread()
    {
        var now = DateTimeOffset.UtcNow;
        return new SessionThread
        {
            Id = "thread_test",
            WorkspacePath = _root,
            CreatedAt = now,
            LastActiveAt = now,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_test",
                    ThreadId = "thread_test",
                    Status = TurnStatus.Completed,
                    StartedAt = now,
                    CompletedAt = now,
                    Items =
                    [
                        new SessionItem
                        {
                            Id = "item_user",
                            TurnId = "turn_test",
                            Type = ItemType.UserMessage,
                            Status = ItemStatus.Completed,
                            CreatedAt = now,
                            CompletedAt = now,
                            Payload = new UserMessagePayload { Text = "Compare plans." }
                        },
                        new SessionItem
                        {
                            Id = "item_agent",
                            TurnId = "turn_test",
                            Type = ItemType.AgentMessage,
                            Status = ItemStatus.Completed,
                            CreatedAt = now,
                            CompletedAt = now,
                            Payload = new AgentMessagePayload { Text = "```dotcraft-ui\n<Tabs />\n```" }
                        }
                    ]
                }
            ]
        };
    }

    private static (AppServerConnection Connection, InMemoryTransport Transport) Register(
        AppServerThreadSubscribers subscribers,
        AppServerConnection connection,
        bool subscribe)
    {
        var transport = new InMemoryTransport();
        if (subscribe)
            Assert.True(connection.TryAddSubscription("thread_test", new CancellationTokenSource()));
        subscribers.Register(transport, connection);
        return (connection, transport);
    }

    private static AppServerConnection IncapableConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(new ClientConnectionInfo { Name = "desktop", Version = "test" }, new ClientConnectionCapabilities()));
        return connection;
    }

    private static AppServerConnection CapableConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new ClientConnectionInfo { Name = "desktop", Version = "test" },
            new ClientConnectionCapabilities { GenerativeUi = true }));
        return connection;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
