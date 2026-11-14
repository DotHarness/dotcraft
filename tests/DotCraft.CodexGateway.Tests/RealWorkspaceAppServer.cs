using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DotCraft.Sdk;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.CodexGateway.Tests;

internal sealed class RealWorkspaceAppServer : IAsyncDisposable
{
    private readonly AgentFactory _factory;
    private readonly ThreadStore _store;
    private readonly SessionService _sessions;
    private readonly AppConfig _config;
    private readonly AppConfigMonitor _monitor;
    private readonly ConfigurationService _configuration;
    private readonly ChatClientRegistry _clients;
    private readonly ModelProviderRegistry _providers;
    private WebApplication? _server;

    public RealWorkspaceAppServer(string root, string name)
    {
        Root = root;
        Directory.CreateDirectory(CraftPath);
        Model = new GatewayTestModel(name);
        _providers = new ModelProviderRegistry([Model]);
        _clients = new ChatClientRegistry(_providers);
        _config = new AppConfig
        {
            ProviderId = "fixture",
            GlobalConfigPath = Path.Combine(root, "global-config.json"),
            WorkspaceConfigPath = Path.Combine(CraftPath, "config.json"),
            InstantInterruptEnabled = false,
            ProviderPreferences = new(StringComparer.OrdinalIgnoreCase)
            {
                ["fixture"] = new ModelPreference { Model = GatewayTestModel.ModelId }
            },
            Providers = new(StringComparer.OrdinalIgnoreCase)
            {
                ["fixture"] = new()
                {
                    Protocol = ModelProviderProtocols.OpenAI,
                    ApiKey = "unused",
                    EndPoint = "https://127.0.0.1:9/v1"
                }
            }
        };
        File.WriteAllText(Path.Combine(CraftPath, "models.json"), JsonSerializer.Serialize(new
        {
            models = new Dictionary<string, object> { [GatewayTestModel.ModelId] = new { contextWindow = 8_000_000 } }
        }));
        File.WriteAllText(_config.GlobalConfigPath, "{}");
        File.WriteAllText(_config.WorkspaceConfigPath, JsonSerializer.Serialize(_config));
        var memory = new MemoryStore(CraftPath);
        var skills = new SkillsLoader(CraftPath);
        _factory = new AgentFactory(CraftPath, Root, _config, memory, skills,
            new AutoApproveApprovalService(), blacklist: null, chatClientRegistry: _clients, toolSources: []);
        _store = new ThreadStore(CraftPath);
        _sessions = new SessionService(_factory, _factory.CreateDefaultAgent(),
            new SessionPersistenceService(_store), new SessionGate());
        _monitor = new AppConfigMonitor(_config);
        _configuration = new ConfigurationService(ConfigSchemaRegistrations.CreateDescriptorRegistry(),
            _monitor, _config.GlobalConfigPath, _config.WorkspaceConfigPath);
        ConfigurationSubsystems.Register(_configuration, _monitor, _sessions, skills, null, null, null);
    }

    public string Root { get; }
    public string CraftPath => Path.Combine(Root, ".craft");
    public GatewayTestModel Model { get; }
    public string Endpoint { get; private set; } = "";
    public System.Collections.Concurrent.ConcurrentQueue<(string Method, JsonElement Parameters)> Requests { get; } = new();

    public async Task StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        _server = builder.Build();
        _server.UseWebSockets();
        _server.Map("/ws", HandleConnectionAsync);
        _server.Urls.Add("http://127.0.0.1:0");
        await _server.StartAsync(ct);
        var address = _server.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        Endpoint = $"ws://127.0.0.1:{new Uri(address).Port}/ws";
    }

    public Task<DotCraftClient> ConnectAsync(DotCraftRemoteOptions? options, CancellationToken ct) =>
        DotCraftClient.ConnectRemoteAsync(Endpoint, options ?? new()
        {
            ClientName = "desktop-fixture", AutoReconnect = false
        }, ct);

    private async Task HandleConnectionAsync(HttpContext context)
    {
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        await using var transport = new WebSocketTransport(socket);
        transport.Start();
        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(_sessions, connection, transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()), new AppServerConnectionServices
            {
                ServerVersion = "test", HostWorkspacePath = Root, WorkspaceCraftPath = CraftPath,
                AppConfigMonitor = _monitor, Configuration = _configuration, ChatClientRegistry = _clients,
                ModelProviderRegistry = _providers, MemoryStore = new MemoryStore(CraftPath),
                SkillsLoader = new SkillsLoader(CraftPath)
            });
        try
        {
            while (await transport.ReadMessageAsync(context.RequestAborted) is { } message)
            {
                if (message.IsNotification)
                {
                    if (message.Method == Contract.AppServerMethodNames.Initialized)
                        handler.HandleInitializedNotification();
                }
                else if (message.IsRequest)
                {
                    Requests.Enqueue((message.Method!, message.Params?.Clone() ?? default));
                    try
                    {
                        var result = await handler.HandleRequestAsync(message, context.RequestAborted);
                        if (result is not null)
                            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(message.Id, result),
                                context.RequestAborted);
                    }
                    catch (AppServerException exception)
                    {
                        await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(message.Id, exception.ToError()),
                            context.RequestAborted);
                    }
                }
            }
        }
        finally
        {
            connection.MarkClosed();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sessions.DisposeAsync();
        if (_server is not null)
        {
            await _server.StopAsync();
            await _server.DisposeAsync();
        }
        await _store.DisposeAsync();
        await _factory.DisposeAsync();
    }
}
