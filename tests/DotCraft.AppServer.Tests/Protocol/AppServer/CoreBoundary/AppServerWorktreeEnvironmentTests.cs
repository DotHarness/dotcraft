using System.Reflection;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Modules;
using DotCraft.Persistence;
using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerWorktreeEnvironmentTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "AppServerWorktreeEnv_" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceStateDatabase _database;
    private readonly ThreadStore _store;

    public AppServerWorktreeEnvironmentTests()
    {
        _database = new WorkspaceStateDatabase(_tempDir);
        _store = new ThreadStore(_tempDir, _database);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();
        _database.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task CreateAndStart_DrainsProgressAndOutputBeforeReportingEnvironmentError()
    {
        var (handler, transport, service) = await CreateHandlerAsync();
        var environmentPath = Path.Combine(_tempDir, "environment.json");

        var (response, notifications) = await ExecuteAsync(handler, transport, Contract.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test-user", workspacePath = _tempDir },
            branchName = "dotcraft/wire-env",
            copyDirtyChanges = false,
            environmentPath,
            operationId = "op-1"
        });

        var options = Assert.IsType<WorktreeCreateAndStartOptions>(service.CreateOptions);
        Assert.Equal(environmentPath, options.EnvironmentPath);
        Assert.Equal("dotcraft/wire-env", options.BranchName);
        Assert.False(options.CopyDirtyChanges);
        Assert.Equal("op-1", options.OperationId);

        Assert.Collection(notifications,
            message =>
            {
                Assert.Equal(Contract.AppServerRpc.WorktreeCreateProgress.Name, message.Method);
                Assert.Equal("setupEnvironment", message.Params.GetProperty("step").GetString());
                Assert.Equal("started", message.Params.GetProperty("status").GetString());
            },
            message =>
            {
                Assert.Equal(Contract.AppServerRpc.WorktreeSetupOutput.Name, message.Method);
                Assert.Equal("setup-output", message.Params.GetProperty("delta").GetString());
            },
            message =>
            {
                Assert.Equal(Contract.AppServerRpc.WorktreeCreateProgress.Name, message.Method);
                Assert.Equal("setupEnvironment", message.Params.GetProperty("step").GetString());
                Assert.Equal("failed", message.Params.GetProperty("status").GetString());
            });
        Assert.All(notifications, message => Assert.Equal("op-1", message.Params.GetProperty("operationId").GetString()));
        var data = AssertEnvironmentError(response, WorktreeEnvironmentErrorCodes.EnvironmentInvalid);
        Assert.Equal("errors.worktreeEnvironment.environmentInvalid", data.GetProperty("messageKey").GetString());
        Assert.Equal("Invalid test environment.", data.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Remove_ReportsNormalizedCleanupFailureDetails()
    {
        var (handler, transport, _) = await CreateHandlerAsync();
        var worktreePath = Path.Combine(_tempDir, "worktree");

        var (response, notifications) = await ExecuteAsync(handler, transport, Contract.AppServerMethodNames.WorktreeRemove, new
        {
            path = worktreePath,
            discardChanges = true
        });

        Assert.Empty(notifications);
        var data = AssertEnvironmentError(response, WorktreeEnvironmentErrorCodes.CleanupFailed);
        Assert.Equal("errors.worktreeEnvironment.cleanupFailed", data.GetProperty("messageKey").GetString());
        var parameters = data.GetProperty("params");
        Assert.Equal(worktreePath, parameters.GetProperty("path").GetString());
        Assert.Equal(4, parameters.GetProperty("exitCode").GetInt32());
        Assert.Equal("cleanup-output", parameters.GetProperty("outputTail").GetString());
    }

    private async Task<(AppServerRequestHandler Handler, InMemoryTransport Transport, EnvironmentSessionProxy Service)> CreateHandlerAsync()
    {
        var session = DispatchProxy.Create<ISessionService, EnvironmentSessionProxy>();
        var proxy = (EnvironmentSessionProxy)(object)session;
        proxy.Inner = new TestableSessionService(_store);
        var transport = new InMemoryTransport();
        var handler = new AppServerRequestHandler(
            session,
            new AppServerConnection(),
            transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                WorkspaceCraftPath = Path.Combine(_tempDir, ".craft"),
                HostWorkspacePath = _tempDir
            });
        var init = InMemoryTransport.BuildRequest(Contract.AppServerMethodNames.Initialize, new
        {
            clientInfo = new { name = "test-client", version = "0.0.1" },
            capabilities = new { approvalSupport = true, streamingSupport = true }
        }, id: 1);
        var result = await handler.HandleRequestAsync(init, default);
        if (result != null)
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(init.Id, result));
        _ = await transport.ReadNextSentAsync();
        handler.HandleInitializedNotification();
        return (handler, transport, proxy);
    }

    private static JsonElement AssertEnvironmentError(JsonDocument response, string code)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(AppServerErrors.WorktreeEnvironmentCode, error.GetProperty("code").GetInt32());
        var data = error.GetProperty("data");
        Assert.Equal(code, data.GetProperty("code").GetString());
        return data;
    }

    private static async Task<(JsonDocument Response, List<(string Method, JsonElement Params)> Notifications)> ExecuteAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        string method,
        object parameters)
    {
        var request = InMemoryTransport.BuildRequest(method, parameters, 10);
        try
        {
            var result = await handler.HandleRequestAsync(request, default);
            if (result != null)
                await transport.WriteMessageAsync(AppServerRequestHandler.BuildResponse(request.Id, result));
        }
        catch (AppServerException ex)
        {
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(request.Id, ex.ToError()));
        }

        var notifications = new List<(string, JsonElement)>();
        while (true)
        {
            var message = await transport.ReadNextSentAsync();
            var root = message.RootElement;
            if (root.TryGetProperty("id", out var id) && id.GetInt32() == 10)
                return (message, notifications);
            if (root.TryGetProperty("method", out var notificationMethod))
                notifications.Add((notificationMethod.GetString()!, root.GetProperty("params").Clone()));
        }
    }

    public class EnvironmentSessionProxy : DispatchProxy
    {
        public ISessionService Inner { get; set; } = null!;
        public WorktreeCreateAndStartOptions? CreateOptions { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod!.Name switch
        {
            nameof(ISessionService.CreateWorktreeAndStartAsync) => FailCreation((WorktreeCreateAndStartOptions)args![0]!),
            nameof(ISessionService.RemoveWorktreeAsync) => Task.FromException(new WorktreeEnvironmentException(
                WorktreeEnvironmentErrorCodes.CleanupFailed, "Cleanup failed.")
            {
                WorktreePath = (string)args![0]!,
                ExitCode = 4,
                OutputTail = "cleanup-output"
            }),
            _ => targetMethod.Invoke(Inner, args)
        };

        private Task<WorktreeCreateAndStartResult> FailCreation(WorktreeCreateAndStartOptions options)
        {
            CreateOptions = options;
            options.Progress?.Invoke(WorktreeCreateSteps.SetupEnvironment, "started");
            options.SetupOutput?.Invoke("setup-output");
            options.Progress?.Invoke(WorktreeCreateSteps.SetupEnvironment, "failed");
            return Task.FromException<WorktreeCreateAndStartResult>(new WorktreeEnvironmentException(
                WorktreeEnvironmentErrorCodes.EnvironmentInvalid, "Invalid test environment."));
        }
    }
}
