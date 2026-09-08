using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Modules;
using DotCraft.Commands.Core;
using DotCraft.Sessions;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using DotCraft.Workspaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class AutomationWireIntegrationTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "automation-rpc-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryTransport _transport = new();
    private readonly AppServerRequestHandler _handler;
    private int _requestId;

    public AutomationWireIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        var craftPath = Path.Combine(_root, ".craft");
        var service = new AutomationService(new AutomationsConfig(),
            new DotCraftPaths(_root, craftPath, null), NullLogger<AutomationService>.Instance);
        var commands = CommandRegistry.CreateDefault(".craft");
        commands.RegisterHandler(new AutomationCommandHandler(service));
        _handler = new AppServerRequestHandler(
            new TestableSessionService(new ThreadStore(craftPath)), new AppServerConnection(), _transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()),
            new AppServerConnectionServices
            {
                WorkspaceCraftPath = craftPath,
                HostWorkspacePath = _root,
                CommandRegistry = commands,
                AutomationsHandler = new AutomationsRequestHandler(service)
            });
    }

    [Fact]
    public async Task JsonRpcLifecycle_PreservesVersions()
    {
        var initialized = await RequestAsync("initialize", new
        {
            clientInfo = new { name = "automation-test", version = "1" }
        });
        var capabilities = Result(initialized).GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("automations").GetBoolean());
        _handler.HandleInitializedNotification();

        var automationCommand = Result(await RequestAsync("command/list", new { })).GetProperty("commands")
            .EnumerateArray().Single(command => command.GetProperty("name").GetString() == "/automate");
        Assert.Equal("command.automate.description", automationCommand.GetProperty("descriptionKey").GetString());
        Assert.Equal("Manage scheduled automations", automationCommand.GetProperty("description").GetString());
        Assert.Equal("Manage scheduled automations", automationCommand.GetProperty("fallbackDescription").GetString());

        var created = Result(await RequestAsync("automation/create", new { automation = Input("paused") }))
            .GetProperty("automation");
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal("paused", created.GetProperty("status").GetString());
        Assert.Equal(1, created.GetProperty("version").GetInt32());
        Assert.False(created.TryGetProperty("nextRunAt", out var pausedNext) && pausedNext.ValueKind != JsonValueKind.Null);

        var listed = Result(await RequestAsync("automation/list", new { })).GetProperty("automations");
        Assert.Equal(id, Assert.Single(listed.EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(id, Result(await RequestAsync("automation/read", new { automationId = id }))
            .GetProperty("automation").GetProperty("id").GetString());

        var edited = Result(await RequestAsync("automation/update", new
        {
            automationId = id, expectedVersion = 1, automation = Input("paused", "Edited report")
        })).GetProperty("automation");
        Assert.Equal(2, edited.GetProperty("version").GetInt32());
        Assert.Equal("Edited report", edited.GetProperty("name").GetString());

        var conflict = await RequestAsync("automation/update", new
        {
            automationId = id, expectedVersion = 1, automation = Input("active", "Stale report")
        });
        Assert.True(conflict.TryGetProperty("error", out var conflictError));
        Assert.Contains("automation.versionConflict", conflictError.GetRawText());
        var afterConflict = Result(await RequestAsync("automation/read", new { automationId = id })).GetProperty("automation");
        Assert.Equal(2, afterConflict.GetProperty("version").GetInt32());
        Assert.Equal("Edited report", afterConflict.GetProperty("name").GetString());

        var resumed = Result(await RequestAsync("automation/update", new
        {
            automationId = id, expectedVersion = 2, automation = Input("active", "Edited report")
        })).GetProperty("automation");
        Assert.Equal("active", resumed.GetProperty("status").GetString());
        Assert.Equal(3, resumed.GetProperty("version").GetInt32());
        Assert.True(DateTimeOffset.Parse(resumed.GetProperty("nextRunAt").GetString()!) > DateTimeOffset.UtcNow);

        var paused = Result(await RequestAsync("automation/update", new
        {
            automationId = id, expectedVersion = 3, automation = Input("paused", "Edited report")
        })).GetProperty("automation");
        Assert.Equal("paused", paused.GetProperty("status").GetString());
        Assert.Equal(4, paused.GetProperty("version").GetInt32());

        Assert.Empty(Result(await RequestAsync("automation/runs/list", new { automationId = id }))
            .GetProperty("runs").EnumerateArray());
        Assert.True(Result(await RequestAsync("automation/delete", new { automationId = id })).GetProperty("ok").GetBoolean());
        Assert.Empty(Result(await RequestAsync("automation/list", new { })).GetProperty("automations").EnumerateArray());

    }

    [Fact]
    public async Task InvalidDefinition_ReturnsStableValidationCodeAndEnglishFallback()
    {
        await RequestAsync("initialize", new { clientInfo = new { name = "automation-test", version = "1" } });
        _handler.HandleInitializedNotification();
        var invalid = await RequestAsync("automation/create", new { automation = (object?)null });
        var error = invalid.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal("automation.definitionRequired", error.GetProperty("data").GetProperty("code").GetString());
        Assert.Equal("The automation definition is invalid.", error.GetProperty("data").GetProperty("fallbackText").GetString());
    }

    private static object Input(string status, string name = "Weekly report") => new
    {
        name, prompt = "Summarize changes", status, executionMode = "independent", workspaceMode = "project",
        notificationPolicy = "all", approvalPolicy = "workspaceScope",
        schedule = new { kind = "every", everyMs = 86_400_000 }
    };

    private static JsonElement Result(JsonElement response)
    {
        Assert.False(response.TryGetProperty("error", out _), response.GetRawText());
        return response.GetProperty("result");
    }

    private async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        var id = ++_requestId;
        var request = InMemoryTransport.BuildRequest(method, parameters, id);
        try
        {
            var result = await _handler.HandleRequestAsync(request, CancellationToken.None);
            if (result != null)
                await _transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
        }
        catch (AppServerException error)
        {
            await _transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(request.Id, error.ToError()));
        }

        using var response = await _transport.ReadNextSentAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("2.0", response.RootElement.GetProperty("jsonrpc").GetString());
        Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());
        return response.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        await _transport.DisposeAsync();
        Directory.Delete(_root, recursive: true);
    }
}
