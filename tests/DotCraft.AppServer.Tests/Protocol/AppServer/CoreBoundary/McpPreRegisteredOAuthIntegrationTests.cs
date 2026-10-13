using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServerTestClient;
using DotCraft.Mcp;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class McpPreRegisteredOAuthIntegrationTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mcp-oauth-integration-" + Guid.NewGuid().ToString("N"));
    private readonly McpOAuthSmokeServer _server = new();
    private string _callback = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _callback = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/registered/callback";
        _server.RegisterClient("client-a", "secret-a", _callback);
        _server.RegisterClient("client-b", "secret-b", _callback);
        await _server.StartAsync();
    }

    [Fact]
    public async Task AppServerLogin_ToolCall_ColdRefresh_SecretRotation_AndClientSwitch()
    {
        var config = Config();
        var craft = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(craft);
        await using (var manager = Manager())
        {
            using var harness = new AppServerTestHarness(mcpClientManager: manager, workspaceCraftPath: craft);
            await harness.InitializeAsync();
            using var upsert = await RpcAsync(harness, "mcp/upsert", new { scope = "workspace", server = new
            {
                name = config.Name, enabled = true, transport = "streamableHttp", url = config.Url,
                oauth = new { clientId = "client-a", clientSecret = "secret-a", callbackUrl = _callback }
            } });
            Assert.Equal("********", upsert.RootElement.GetProperty("result").GetProperty("server").GetProperty("oauth").GetProperty("clientSecret").GetString());
            await manager.WaitForStartupCompletionAsync();
            harness.Transport.DrainSent().ForEach(document => document.Dispose());
            using var login = await RpcAsync(harness, "mcpServer/oauth/login", new { name = config.Name });
            var authorizationUrl = login.RootElement.GetProperty("result").GetProperty("authorizationUrl").GetString()!;
            using var browser = new HttpClient();
            using var callback = await browser.GetAsync(authorizationUrl);
            callback.EnsureSuccessStatusCode();
            while (true)
            {
                using var message = await harness.Transport.ReadNextSentAsync();
                if (!message.RootElement.TryGetProperty("method", out var method) || method.GetString() != "mcpServer/oauthLogin/completed") continue;
                Assert.True(message.RootElement.GetProperty("params").GetProperty("success").GetBoolean(), message.RootElement.ToString());
                break;
            }
            await manager.WaitForStartupCompletionAsync();
            await EchoAsync(manager);
        }
        Assert.Equal(0, _server.RegistrationRequests);
        Assert.Equal(1, _server.AuthorizationRequests);
        Assert.DoesNotContain("secret-a", await File.ReadAllTextAsync(Path.Combine(_root, "user-data", "mcp-auth.json")));

        _server.ExpireAccessTokens();
        await using (var manager = Manager())
        {
            await ConnectAsync(manager, config);
            await EchoAsync(manager);
        }
        Assert.Equal(1, _server.RefreshRequests);
        Assert.Equal(1, _server.AuthorizationRequests);

        _server.RotateSecret("client-a", "rotated-secret");
        config.OAuth!.ClientSecret = "rotated-secret";
        _server.ExpireAccessTokens();
        await using (var manager = Manager())
        {
            await ConnectAsync(manager, config);
            await EchoAsync(manager);
        }
        Assert.Equal(2, _server.RefreshRequests);

        config.OAuth.ClientId = "client-b";
        config.OAuth.ClientSecret = "secret-b";
        await using (var manager = Manager())
        {
            await ConnectAsync(manager, config);
            Assert.Equal("notLoggedIn", (await manager.ListStatusesAsync()).Single().AuthStatus);
        }
        await LoginAsync(config);
        await using (var manager = Manager())
        {
            await ConnectAsync(manager, config);
            await EchoAsync(manager);
        }
        Assert.Equal(2, _server.AuthorizationRequests);
        Assert.Equal(0, _server.OldClientRequests);
    }

    [Fact]
    public async Task ThreadConfiguration_ProjectsMaskedSecret_AndPreservesItOnUpdate()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        using var started = await RpcAsync(harness, "thread/start", new
        {
            identity = new { channelName = "appserver", userId = "oauth-test", workspacePath = harness.Identity.WorkspacePath },
            config = new { mcpServers = new[] { new
            {
                name = "thread-oauth", enabled = false, transport = "http", url = _server.McpUrl,
                oauth = new { clientId = "client-a", clientSecret = "secret-a", callbackUrl = _callback }
            } } }
        });
        Assert.DoesNotContain("secret-a", started.RootElement.ToString());
        var threadId = started.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
        using var updated = await RpcAsync(harness, "thread/config/update", new
        {
            threadId, config = new { mcpServers = new[] { new
            {
                name = "thread-oauth", enabled = false, transport = "http", url = _server.McpUrl,
                oauth = new { clientId = "client-a", callbackUrl = _callback }
            } } }
        });
        var thread = await harness.Service.GetThreadAsync(threadId);
        Assert.Equal("secret-a", thread.Configuration!.McpServers!.Single().OAuth!.ClientSecret);
    }

    [Fact]
    public async Task ThreadConfiguration_WritingBackTheReadProjection_PreservesSecret()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        using var started = await RpcAsync(harness, "thread/start", new
        {
            identity = new { channelName = "appserver", userId = "oauth-test", workspacePath = harness.Identity.WorkspacePath },
            config = new { mcpServers = new[] { new
            {
                name = "thread-oauth", enabled = false, transport = "http", url = _server.McpUrl,
                oauth = new { clientId = "client-a", clientSecret = "secret-a", callbackUrl = _callback }
            } } }
        });
        var threadId = started.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;
        using var read = await RpcAsync(harness, "thread/read", new { threadId });
        var config = JsonNode.Parse(read.RootElement.GetProperty("result").GetProperty("thread").GetProperty("configuration").GetRawText())!.AsObject();
        Assert.Equal("********", config["mcpServers"]![0]!["oauth"]!["clientSecret"]!.GetValue<string>());
        config["developerInstructions"] = "Answer briefly.";

        using var updated = await RpcAsync(harness, "thread/config/update", new { threadId, config });

        var thread = await harness.Service.GetThreadAsync(threadId);
        Assert.Equal("secret-a", thread.Configuration!.McpServers!.Single().OAuth!.ClientSecret);
    }

    [Fact]
    public async Task UnconfiguredClient_UsesDynamicRegistration_AndRestoresItsCredentials()
    {
        await using var dynamicServer = new McpOAuthSmokeServer { AllowDynamicRegistration = true };
        await dynamicServer.StartAsync();
        var config = Config();
        config.Url = dynamicServer.McpUrl;
        config.OAuth = null;
        await LoginAsync(config);
        dynamicServer.ExpireAccessTokens();
        await using var manager = Manager();
        await ConnectAsync(manager, config);
        await EchoAsync(manager);
        Assert.Equal(1, dynamicServer.RegistrationRequests);
        Assert.Equal(1, dynamicServer.AuthorizationRequests);
        Assert.Equal(1, dynamicServer.RefreshRequests);
    }

    [Fact]
    public async Task EnvironmentSecret_LogsIn_AndWrongSecretFails()
    {
        var key = "DOTCRAFT_MCP_SMOKE_" + Guid.NewGuid().ToString("N");
        var config = Config();
        config.OAuth!.ClientSecretEnvVar = key;
        config.OAuth.ClientSecret = "wrong-fallback";
        Environment.SetEnvironmentVariable(key, "secret-a");
        try { await LoginAsync(config); }
        finally { Environment.SetEnvironmentVariable(key, null); }
        config.OAuth.ClientSecretEnvVar = null;
        config.OAuth.ClientSecret = "wrong-secret";
        Assert.False(await LoginResultAsync(config));
    }

    [Fact]
    public async Task OccupiedCallbackPort_FailsWithoutSubstitutingAnotherPort()
    {
        var config = Config();
        using var occupied = new TcpListener(IPAddress.Loopback, new Uri(_callback).Port);
        occupied.Start();
        await Assert.ThrowsAnyAsync<Exception>(() => McpOAuthLoginCoordinator.BeginAsync(config, null, 5,
            Path.Combine(_root, "user-data"), (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal(0, _server.AuthorizationRequests);
    }

    [Fact]
    public async Task ScopeEditing_PreservesOmittedOrMaskedSecrets_AndAllowsExplicitRemoval()
    {
        var craft = Path.Combine(_root, ".craft");
        Directory.CreateDirectory(craft);
        var config = Config();
        McpScopeStore.Upsert(Path.Combine(craft, "config.json"), config);
        await using var manager = Manager();
        using var harness = new AppServerTestHarness(mcpClientManager: manager, workspaceCraftPath: craft);
        await harness.InitializeAsync();
        using var omitted = await RpcAsync(harness, "mcp/upsert", new { scope = "workspace", server = new
        { name = config.Name, enabled = false, transport = "http", url = config.Url } });
        Assert.Equal("secret-a", McpScopeStore.Read(Path.Combine(craft, "config.json"), "workspace").Single().OAuth!.ClientSecret);
        using var masked = await RpcAsync(harness, "mcp/upsert", new { scope = "workspace", server = new
        { name = config.Name, enabled = false, transport = "http", url = config.Url, oauth = new { clientId = "client-a", clientSecret = "********", hasClientSecret = true } } });
        Assert.Equal("secret-a", McpScopeStore.Read(Path.Combine(craft, "config.json"), "workspace").Single().OAuth!.ClientSecret);
        using var secretRemoved = await RpcAsync(harness, "mcp/upsert", new { scope = "workspace", server = new
        { name = config.Name, enabled = false, transport = "http", url = config.Url, oauth = new { clientId = "client-a", clientSecret = (string?)null } } });
        Assert.Null(McpScopeStore.Read(Path.Combine(craft, "config.json"), "workspace").Single().OAuth!.ClientSecret);
        using var removed = await RpcAsync(harness, "mcp/upsert", new { scope = "workspace", server = new
        { name = config.Name, enabled = false, transport = "http", url = config.Url, oauth = (object?)null } });
        Assert.Null(McpScopeStore.Read(Path.Combine(craft, "config.json"), "workspace").Single().OAuth);
    }

    private async Task LoginAsync(McpServerConfig config) => Assert.True(await LoginResultAsync(config));

    private async Task<bool> LoginResultAsync(McpServerConfig config)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var url = await McpOAuthLoginCoordinator.BeginAsync(config, null, 10, Path.Combine(_root, "user-data"),
            (success, _) => { completed.TrySetResult(success); return Task.CompletedTask; }, CancellationToken.None);
        using var browser = new HttpClient();
        using var result = await browser.GetAsync(url);
        result.EnsureSuccessStatusCode();
        return await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private McpClientManager Manager() => new(DotCraftPaths.CreateForExecutionHost(_root, Path.Combine(_root, ".craft"), Path.Combine(_root, "user-data")));

    private McpServerConfig Config() => new()
    {
        Name = "oauth-smoke", Enabled = true, Transport = "http", Url = _server.McpUrl,
        OAuth = new() { ClientId = "client-a", ClientSecret = "secret-a", CallbackUrl = _callback }
    };

    private static async Task ConnectAsync(McpClientManager manager, McpServerConfig config)
    {
        await manager.ConnectAsync([config]);
        await manager.WaitForStartupCompletionAsync();
    }

    private static async Task EchoAsync(McpClientManager manager)
    {
        var result = await manager.CallToolAsync("oauth-smoke", "echo", new Dictionary<string, object?> { ["message"] = "OAUTH_SMOKE_OK" });
        Assert.Contains("OAUTH_SMOKE_OK", JsonSerializer.Serialize(result));
    }

    private static async Task<JsonDocument> RpcAsync(AppServerTestHarness harness, string method, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, JsonSerializer.SerializeToElement(parameters)));
        while (true)
        {
            var message = await harness.Transport.ReadNextSentAsync();
            if (message.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.ToString());
            if (message.RootElement.TryGetProperty("result", out _)) return message;
            message.Dispose();
        }
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
