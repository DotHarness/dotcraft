using System.Reflection;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.ExternalChannel;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ChannelRunnerExternalChannelUpsertTests : IDisposable
{
    private const string ChannelName = "test-channel";
    private const string RuntimeContextValue = "Runtime context from the test adapter.";

    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        "ChannelRunnerExternalChannelUpsertTests_" + Guid.NewGuid().ToString("N")[..8]);

    [Fact]
    public async Task RuntimeUpsertedHost_SupportsAndUnbindsRuntimeAdditionalContext()
    {
        var craftPath = Path.Combine(_workspacePath, ".craft");
        Directory.CreateDirectory(craftPath);

        var config = AppConfigTestFactory.CreateOpenAI();
        config.SetSection("AppServer", new AppServerConfig { Mode = AppServerMode.WebSocket });
        var entry = new ExternalChannelEntry
        {
            Name = ChannelName,
            Enabled = true,
            Transport = ExternalChannelTransport.Websocket
        };
        config.ExternalChannels = [entry];

        var modelProviders = new ModelProviderRegistry([new OpenAIClientProvider()]);
        var chatClients = new ChatClientRegistry(modelProviders);
        var externalChannels = new ExternalChannelRegistry();
        var channelRuntimes = new ChannelRuntimeRegistry();
        var runtimeContextProvider = new WireRuntimeAdditionalContextProvider();
        var contextPages = new ContextPageManager();
        await using var services = new ServiceCollection()
            .AddSingleton(modelProviders)
            .AddSingleton(chatClients)
            .AddSingleton(new PathBlacklist([]))
            .AddSingleton(externalChannels)
            .AddSingleton<IChannelRuntimeRegistry>(channelRuntimes)
            .AddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()))
            .AddSingleton(runtimeContextProvider)
            .AddSingleton<IContextPageManager>(contextPages)
            .AddSingleton<IAppConfigMonitor>(new AppConfigMonitor(config))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var paths = new DotCraft.Workspaces.DotCraftPaths(_workspacePath, craftPath, userDataPath: null);
        var runner = ChannelRunner.TryCreateForAppServer(services, config, paths, new ModuleRegistry());
        Assert.NotNull(runner);

        var sessionService = new TestableSessionService(new ThreadStore(craftPath));
        using var cronService = new CronService(Path.Combine(craftPath, "cron-jobs.json"));

        try
        {
            runner.BuildPoolThroughBuildAll();
            runner.CompleteAfterSession(sessionService, cronService);
            await runner.ApplyExternalChannelUpsertAsync(entry, CancellationToken.None);

            Assert.True(externalChannels.TryGet(ChannelName, out var host));
            Assert.NotNull(host);

            await using var transport = new InMemoryTransport();
            var connection = new AppServerConnection();
            var factory = Assert.IsType<ExternalChannelRequestHandlerFactory>(typeof(ExternalChannelHost)
                .GetField("_requestHandlerFactory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));
            var handler = factory.Create(connection, transport, cronService);

            await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("initialize", new
            {
                clientInfo = new { name = "channel-test", version = "0.0.1" },
                capabilities = new { streamingSupport = true }
            }));
            using var initializeResponse = await ReadResponseAsync(transport, id: 1);
            Assert.True(initializeResponse.RootElement
                .GetProperty("result")
                .GetProperty("capabilities")
                .GetProperty("runtimeAdditionalContext")
                .GetBoolean());
            handler.HandleInitializedNotification();

            await ExecuteAsync(handler, transport, InMemoryTransport.BuildRequest("thread/start", new
            {
                identity = new { channelName = ChannelName, userId = "user-1", workspacePath = _workspacePath },
                additionalContext = new Dictionary<string, RuntimeAdditionalContextValue>
                {
                    ["test.runtime"] = new()
                    {
                        Kind = RuntimeAdditionalContextKinds.Application,
                        Value = RuntimeContextValue
                    }
                }
            }, id: 2));
            using var startResponse = await ReadResponseAsync(transport, id: 2);
            var threadId = startResponse.RootElement
                .GetProperty("result")
                .GetProperty("thread")
                .GetProperty("id")
                .GetString()!;
            Assert.Contains(
                RuntimeContextValue,
                runtimeContextProvider.GetSystemPromptSection(
                    new ThreadSystemPromptContext(threadId, _workspacePath, ChannelName)));

            var contextPageKey = ContextPageKeys.RuntimeAdditionalContext();
            Assert.Equal("cached-v1", contextPages.GetOrAdd(
                threadId,
                contextPageKey,
                ContextPageLifecycle.StableUntilCompaction,
                () => "cached-v1").Content);

            await transport.DisposeAsync();
            var runLoop = typeof(ExternalChannelHost)
                .GetMethod("RunMessageLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await Assert.IsAssignableFrom<Task>(runLoop.Invoke(
                host,
                [transport, connection, handler, CancellationToken.None]));

            Assert.Null(runtimeContextProvider.GetSystemPromptSection(
                new ThreadSystemPromptContext(threadId, _workspacePath, ChannelName)));
            Assert.Equal("cached-v2", contextPages.GetOrAdd(
                threadId,
                contextPageKey,
                ContextPageLifecycle.StableUntilCompaction,
                () => "cached-v2").Content);
        }
        finally
        {
            await runner.DisposeAsync();
        }
    }

    private static async Task ExecuteAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        AppServerIncomingMessage request)
    {
        var result = await handler.HandleRequestAsync(request, default);
        if (result != null)
        {
            await transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(request.Id, result));
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(InMemoryTransport transport, int id)
    {
        while (true)
        {
            var message = await transport.ReadNextSentAsync();
            if (message.RootElement.TryGetProperty("id", out var responseId)
                && responseId.GetInt32() == id)
            {
                return message;
            }
            message.Dispose();
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspacePath, recursive: true); }
        catch { }
    }
}
