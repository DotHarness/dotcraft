using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerWorktreeEnvironmentTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _environmentPath;
    private int _nextId = 10;

    public AppServerWorktreeEnvironmentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AppServerWorktreeEnv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        RunGit("init");
        RunGit("config", "user.email", "test@example.com");
        RunGit("config", "user.name", "Test User");
        File.WriteAllText(Path.Combine(_tempDir, ".gitignore"), ".craft/" + Environment.NewLine);
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "initial" + Environment.NewLine);
        RunGit("add", ".gitignore", "README.md");
        RunGit("commit", "-m", "init");

        var environments = Path.Combine(_tempDir, ".craft", "environments");
        Directory.CreateDirectory(environments);
        _environmentPath = Path.Combine(environments, "environment.json");
        File.WriteAllText(_environmentPath, JsonSerializer.Serialize(new
        {
            version = 1,
            setup = new { script = OperatingSystem.IsWindows() ? "Write-Output 'hello-setup'" : "echo hello-setup" },
            cleanup = new { script = "exit 4" }
        }));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task EnvironmentMethods_ReportProgressSetupAndEnvironmentErrorsOnTheWire()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(new ThreadStore(Path.Combine(_tempDir, ".craft"))),
            new SessionGate());
        var transport = new InMemoryTransport();
        var handler = new AppServerRequestHandler(
            service,
            new AppServerConnection(),
            transport,
            new ModuleRegistryChannelListContributor(new ModuleRegistry()),
            new AppServerConnectionServices
            {
                ServerVersion = "0.0.1-test",
                WorkspaceCraftPath = Path.Combine(_tempDir, ".craft"),
                HostWorkspacePath = _tempDir,
                MemoryStore = new MemoryStore(_tempDir),
                SkillsLoader = new SkillsLoader(_tempDir)
            });
        await InitializeAsync(handler, transport);

        var (created, notifications) = await ExecuteAsync(handler, transport, Contract.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test-user", workspacePath = _tempDir },
            branchName = "dotcraft/wire-env",
            copyDirtyChanges = false,
            environmentPath = _environmentPath,
            operationId = "op-1"
        });

        var result = created.RootElement.GetProperty("result");
        Assert.Equal("succeeded", result.GetProperty("setup").GetProperty("status").GetString());
        Assert.Equal(_environmentPath, result.GetProperty("worktree").GetProperty("environmentPath").GetString());
        var progress = notifications
            .Where(n => n.Method == Contract.AppServerRpc.WorktreeCreateProgress.Name)
            .Select(n => $"{n.Params.GetProperty("step").GetString()}:{n.Params.GetProperty("status").GetString()}")
            .ToList();
        Assert.Equal(
            [
                "createWorktree:started",
                "createWorktree:completed",
                "copyFiles:started",
                "copyFiles:completed",
                "setupEnvironment:started",
                "setupEnvironment:completed"
            ],
            progress);
        Assert.All(
            notifications.Where(n => n.Method.StartsWith("worktree/", StringComparison.Ordinal)),
            n => Assert.Equal("op-1", n.Params.GetProperty("operationId").GetString()));
        var output = new StringBuilder();
        foreach (var n in notifications.Where(n => n.Method == Contract.AppServerRpc.WorktreeSetupOutput.Name))
            output.Append(n.Params.GetProperty("delta").GetString());
        Assert.Contains("hello-setup", output.ToString(), StringComparison.Ordinal);

        var (cancelled, _) = await ExecuteAsync(handler, transport, Contract.AppServerRpc.WorktreeCancelCreate.Name, new { operationId = "op-unknown" });
        Assert.Equal(JsonValueKind.Object, cancelled.RootElement.GetProperty("result").ValueKind);

        var (invalid, _) = await ExecuteAsync(handler, transport, Contract.AppServerMethodNames.WorktreeCreateAndStart, new
        {
            identity = new { channelName = "appserver", userId = "test-user", workspacePath = _tempDir },
            branchName = "dotcraft/wire-invalid",
            copyDirtyChanges = false,
            environmentPath = Path.Combine(_tempDir, "environment.json")
        });
        AssertEnvironmentError(invalid, "environmentInvalid");

        var threadId = result.GetProperty("thread").GetProperty("id").GetString()!;
        var worktreePath = result.GetProperty("worktree").GetProperty("path").GetString()!;
        await service.ArchiveThreadAsync(threadId);
        Assert.True(Directory.Exists(worktreePath));

        var (removal, _) = await ExecuteAsync(handler, transport, Contract.AppServerMethodNames.WorktreeRemove, new
        {
            path = worktreePath,
            discardChanges = true
        });
        var data = AssertEnvironmentError(removal, "cleanupFailed");
        var parameters = data.GetProperty("params");
        Assert.Equal(worktreePath, parameters.GetProperty("path").GetString());
        Assert.Equal(4, parameters.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.String, parameters.GetProperty("outputTail").ValueKind);
        Assert.True(Directory.Exists(worktreePath));
    }

    private static JsonElement AssertEnvironmentError(JsonDocument response, string code)
    {
        var error = response.RootElement.GetProperty("error");
        Assert.Equal(AppServerErrors.WorktreeEnvironmentCode, error.GetProperty("code").GetInt32());
        var data = error.GetProperty("data");
        Assert.Equal(code, data.GetProperty("code").GetString());
        return data;
    }

    private async Task<(JsonDocument Response, List<(string Method, JsonElement Params)> Notifications)> ExecuteAsync(
        AppServerRequestHandler handler,
        InMemoryTransport transport,
        string method,
        object parameters)
    {
        var id = _nextId++;
        var request = InMemoryTransport.BuildRequest(method, parameters, id);
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
            if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number && idElement.GetInt32() == id)
                return (message, notifications);
            if (root.TryGetProperty("method", out var methodElement))
                notifications.Add((methodElement.GetString()!, root.GetProperty("params").Clone()));
        }
    }

    private static async Task InitializeAsync(AppServerRequestHandler handler, InMemoryTransport transport)
    {
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
    }

    private AgentFactory CreateAgentFactory() =>
        new(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git.");
        process.StandardInput.Close();
        process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}");
    }
}
