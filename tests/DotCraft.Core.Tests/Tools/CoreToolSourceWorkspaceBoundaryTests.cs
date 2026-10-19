using DotCraft.Memory;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Security.ShellCommands;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class CoreToolSourceWorkspaceBoundaryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "dotcraft-coretool-boundary-tests", Guid.NewGuid().ToString("N"));
    private readonly string _workspace;
    private readonly string _outsideFile;

    public CoreToolSourceWorkspaceBoundaryTests()
    {
        _workspace = Path.Combine(_tempRoot, "workspace");
        Directory.CreateDirectory(_workspace);
        _outsideFile = Path.Combine(_tempRoot, "outside.txt");
        File.WriteAllText(_outsideFile, "TOP-PRIVATE-CONTENT");
    }

    [Fact]
    public async Task Thread_override_false_hard_rejects_outside_workspace_read()
    {
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: false);
        var readFile = Assert.Single(registrations, item => item.Definition.Name.Name == "ReadFile");

        Assert.False(readFile.Definition.PolicyHints.RequiresApproval);
        Assert.False(readFile.Definition.Annotations.ContainsKey("dotcraft/nativeApproval"));

        var result = await InvokeAsync(readFile, new JsonObject { ["path"] = _outsideFile });

        Assert.Contains("outside workspace", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP-PRIVATE-CONTENT", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thread_override_false_hard_rejects_exec_launched_outside_workspace()
    {
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: false);
        var exec = Assert.Single(registrations, item => item.Definition.Name.Name == "Exec");

        Assert.False(exec.Definition.PolicyHints.RequiresApproval);

        var result = await InvokeAsync(exec, new JsonObject
        {
            ["command"] = "echo probe",
            ["workingDir"] = _tempRoot
        });

        Assert.Contains("outside the workspace", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP-PRIVATE-CONTENT", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unset_thread_override_keeps_the_workspace_default_approval_routing()
    {
        var approvals = new RecordingApprovalService();
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: null, approvals);
        var readFile = Assert.Single(registrations, item => item.Definition.Name.Name == "ReadFile");
        var exec = Assert.Single(registrations, item => item.Definition.Name.Name == "Exec");

        Assert.True(readFile.Definition.PolicyHints.RequiresApproval);
        Assert.True(readFile.Definition.Annotations.ContainsKey("dotcraft/nativeApproval"));

        Assert.False(exec.Definition.PolicyHints.RequiresApproval);
        await InvokeAsync(exec, new JsonObject { ["command"] = "echo probe", ["workingDir"] = _tempRoot });

        var request = Assert.Single(approvals.ShellRequests);
        Assert.Equal(ShellRiskLevel.OutsideWorkspace, request.Risk);
    }

    [Fact]
    public async Task Memory_store_outside_the_workspace_needs_no_approval_while_other_outside_paths_still_ask()
    {
        var store = Path.Combine(_tempRoot, "state", "memory");
        Directory.CreateDirectory(store);
        var approvals = new RecordingApprovalService();
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: null, approvals, new ThreadMemoryStores(new MemoryStore(Path.GetDirectoryName(store)!), []));
        var writeFile = Assert.Single(registrations, item => item.Definition.Name.Name == "WriteFile");
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approvals);

        var storeDecision = await evaluator.RequestAsync(
            InvocationContext(writeFile), writeFile, new JsonObject { ["path"] = Path.Combine(store, "fact.md") });
        Assert.True(storeDecision.Allowed);
        Assert.Empty(approvals.FileRequests);

        await evaluator.RequestAsync(InvocationContext(writeFile), writeFile, new JsonObject { ["path"] = _outsideFile });
        Assert.Equal(_outsideFile, Assert.Single(approvals.FileRequests));
    }

    [Fact]
    public async Task Memory_store_outside_the_workspace_is_writable_when_outside_paths_are_rejected()
    {
        var store = Path.Combine(_tempRoot, "state", "memory");
        Directory.CreateDirectory(store);
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: false, memoryStores: new ThreadMemoryStores(new MemoryStore(Path.GetDirectoryName(store)!), []));
        var writeFile = Assert.Single(registrations, item => item.Definition.Name.Name == "WriteFile");

        var stored = await InvokeAsync(writeFile, new JsonObject
        {
            ["path"] = Path.Combine(store, "fact.md"),
            ["content"] = "A fact."
        });
        var rejected = await InvokeAsync(writeFile, new JsonObject
        {
            ["path"] = _outsideFile,
            ["content"] = "overwritten"
        });

        Assert.True(stored.Success);
        Assert.Equal("A fact.", File.ReadAllText(Path.Combine(store, "fact.md")));
        Assert.False(rejected.Success);
        Assert.Equal("TOP-PRIVATE-CONTENT", File.ReadAllText(_outsideFile));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private async Task<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
        bool? requireApprovalOutsideWorkspace,
        IApprovalService? approvalService = null,
        ThreadMemoryStores? memoryStores = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        Assert.True(config.Tools.File.RequireApprovalOutsideWorkspace);
        var skillsLoader = new SkillsLoader(_workspace);
        var source = new CoreToolSource(
            config,
            TestModelProviderRegistry.Create(),
            skillsLoader,
            approvalService ?? new AutoApproveApprovalService(),
            new StubBackgroundTerminalService());
        return await source.GetRegistrationsAsync(new ToolPlanningContext(
            "thread-analyst",
            null,
            _workspace,
            Path.Combine(_workspace, ".craft"),
            "agent",
            null,
            [],
            1,
            workspaceRoots: [_workspace],
            requireApprovalOutsideWorkspace: requireApprovalOutsideWorkspace,
            memoryStores: memoryStores));
    }

    private static async Task<ToolExecutionResult> InvokeAsync(
        ToolRegistration registration,
        JsonObject arguments) =>
        await registration.Binding.Runtime.InvokeAsync(InvocationContext(registration), arguments);

    private static ToolInvocationContext InvocationContext(ToolRegistration registration) =>
        new(
            "thread-analyst",
            null,
            "call-1",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            registration.Binding.Revision,
            DateTimeOffset.UtcNow);

    private sealed class RecordingApprovalService : IApprovalService
    {
        public List<ShellApprovalRequest> ShellRequests { get; } = [];

        public List<string> FileRequests { get; } = [];

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            FileRequests.Add(path);
            return Task.FromResult(true);
        }

        public Task<bool> RequestShellApprovalAsync(ShellApprovalRequest request, ApprovalContext? context = null)
        {
            ShellRequests.Add(request);
            return Task.FromResult(true);
        }

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null) =>
            Task.FromResult(true);
    }
}
