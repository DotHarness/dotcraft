using System.Text.Json;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppBindingProtocolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"appbinding-v2-wire-{Guid.NewGuid():N}");

    [Fact]
    public async Task Initialize_ReportsOnlyAppBindingVersion2()
    {
        using var harness = CreateHarness();
        using var initialized = await harness.InitializeAsync();
        var capabilities = initialized.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.Equal(2, capabilities.GetProperty("appBindingVersion").GetInt32());
        Assert.False(capabilities.TryGetProperty("appBinding", out _));
        Assert.False(capabilities.TryGetProperty("appContextBlocks", out _));
    }

    [Fact]
    public async Task UndeclaredAppBindingMethod_ReturnsMethodNotFound()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        await harness.ExecuteRequestAsync(harness.BuildRequest("app/binding/request/create", new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(-32601, error.GetProperty("code").GetInt32());
        Assert.Equal("MethodNotFound", error.GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task AppPrincipalCannotCallThreadControlPlane()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        harness.Connection.BindAppPrincipal("apppr_test", "com.example.test");
        await harness.ExecuteRequestAsync(harness.BuildRequest("thread/list", new { }));
        using var response = await harness.Transport.ReadNextSentAsync();
        Assert.Equal("AppPrincipalUnauthorized",
            response.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ConnectionStart_FillsAppServerEndpointInHandoff()
    {
        WriteHandoffAppPlugin();
        WriteAppServerLock();
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest("app/connection/start", new
        {
            appId = "com.example.handoff"
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        AssertHandoff(response.RootElement.GetProperty("result"), "connect");
    }

    [Fact]
    public async Task BindingEnable_FillsAppServerEndpointInHandoff()
    {
        WriteHandoffAppPlugin();
        WriteAppServerLock();
        using var harness = CreateHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);

        await harness.ExecuteRequestAsync(harness.BuildRequest("thread/appBindings/enable", new
        {
            threadId = thread.Id,
            appId = "com.example.handoff"
        }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        AssertHandoff(response.RootElement.GetProperty("result"), "bind");
    }

    [Fact]
    public async Task SurfacePublishAndResolve_UsePrincipalAndTrustedClientRoles()
    {
        WriteHandoffAppPlugin();
        var control = new AppBindingService();
        var start = control.StartConnection(Path.Combine(_root, ".craft"), "com.example.handoff", "user");
        var connected = control.Connect(Path.Combine(_root, ".craft"), new AppConnectionConnectParams
        {
            ConnectionRequestId = start.ConnectionRequestId,
            RequestToken = start.RequestToken
        });

        using (var publisher = CreateHarness(control))
        {
            await publisher.InitializeAsync();
            publisher.Connection.BindAppPrincipal(connected.Principal.PrincipalId, connected.Principal.AppId);
            await publisher.ExecuteRequestAsync(publisher.BuildRequest("app/surface/publish", new
            {
                surfaceId = "board",
                endpoint = "http://127.0.0.1:5199/dotcraft/board/api/v1",
                bearer = "surface-secret"
            }));
            using var response = await publisher.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);
            Assert.Equal("board", response.RootElement.GetProperty("result").GetProperty("surfaceId").GetString());
        }

        using var resolver = CreateHarness(control);
        await resolver.InitializeAsync();
        await resolver.ExecuteRequestAsync(resolver.BuildRequest("app/surface/resolve", new
        {
            appId = "com.example.handoff",
            surfaceId = "board"
        }));
        using var resolved = await resolver.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(resolved);
        var result = resolved.RootElement.GetProperty("result");
        Assert.Equal("http://127.0.0.1:5199/dotcraft/board/api/v1", result.GetProperty("endpoint").GetString());
        Assert.Equal("surface-secret", result.GetProperty("bearer").GetString());
    }

    private static void AssertHandoff(JsonElement result, string operation)
    {
        var handoff = result.GetProperty("handoff");
        Assert.Equal("customProtocol", handoff.GetProperty("mode").GetString());
        var value = handoff.GetProperty("uri").GetString();
        Assert.NotNull(value);
        Assert.DoesNotContain("{endpoint}", value, StringComparison.Ordinal);

        var uri = new Uri(value);
        Assert.Equal(operation, uri.AbsolutePath.Trim('/'));
        var query = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ws://127.0.0.1:4567/ws?token=a/b+c=", query["endpoint"]);
        Assert.Equal("com.example.handoff", query["app"]);
        Assert.False(string.IsNullOrWhiteSpace(query["request"]));
        Assert.False(string.IsNullOrWhiteSpace(query["token"]));
    }

    private void WriteAppServerLock()
    {
        File.WriteAllText(Path.Combine(_root, ".craft", "appserver.lock"), """
        {
          "endpoints": {
            "appServerWebSocket": "ws://127.0.0.1:4567/ws?token=a/b+c="
          }
        }
        """);
    }

    private void WriteHandoffAppPlugin()
    {
        var pluginRoot = Path.Combine(_root, ".craft", "plugins", "handoff-test");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), """
        {
          "schemaVersion": 1,
          "id": "handoff-test",
          "version": "1.0.0",
          "displayName": "Handoff Test",
          "description": "Tests App Binding handoffs.",
          "capabilities": ["app"],
          "apps": "./apps.json"
        }
        """);
        File.WriteAllText(Path.Combine(pluginRoot, "apps.json"), """
        {
          "apps": [
            {
              "appId": "com.example.handoff",
              "displayName": "Handoff Test",
              "developerName": "DotCraft",
              "description": "Tests App Binding handoffs.",
              "nativeApplication": {
                "displayName": "Handoff Test",
                "protocol": "handofftest"
              },
              "connection": {
                "handoffModes": [
                  {
                    "mode": "customProtocol",
                    "uriTemplate": "handofftest://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}"
                  }
                ]
              }
            }
          ]
        }
        """);
    }

    private AppServerTestHarness CreateHarness(AppBindingService? control = null)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".craft"));
        var monitor = new AppConfigMonitor(AppConfigTestFactory.CreateOpenAI());
        control ??= new AppBindingService();
        var extension = new AppBindingProtocolExtension(control, new AppBindingCoordinator(control), monitor);
        return new AppServerTestHarness(protocolExtensions: [extension], workspaceCraftPath: Path.Combine(_root, ".craft"), appConfigMonitor: monitor);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
