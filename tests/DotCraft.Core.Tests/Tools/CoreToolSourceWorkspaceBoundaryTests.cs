using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Tools;

/// <summary>
/// Verifies that the thread-scoped RequireApprovalOutsideWorkspace override reaches the
/// core file/shell tool assembly. A thread that disables it must hard-reject
/// outside-workspace operations instead of routing them through an (auto-approving)
/// approval service.
/// </summary>
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
    public async Task Thread_override_false_hard_rejects_exec_referencing_outside_paths()
    {
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: false);
        var exec = Assert.Single(registrations, item => item.Definition.Name.Name == "Exec");

        Assert.False(exec.Definition.PolicyHints.RequiresApproval);

        var result = await InvokeAsync(exec, new JsonObject
        {
            ["command"] = $"cat \"{_outsideFile}\""
        });

        Assert.Contains("outside workspace", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TOP-PRIVATE-CONTENT", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unset_thread_override_keeps_the_workspace_default_approval_routing()
    {
        var registrations = await GetRegistrationsAsync(requireApprovalOutsideWorkspace: null);
        var readFile = Assert.Single(registrations, item => item.Definition.Name.Name == "ReadFile");
        var exec = Assert.Single(registrations, item => item.Definition.Name.Name == "Exec");

        Assert.True(readFile.Definition.PolicyHints.RequiresApproval);
        Assert.True(readFile.Definition.Annotations.ContainsKey("dotcraft/nativeApproval"));
        Assert.True(exec.Definition.PolicyHints.RequiresApproval);
        Assert.True(exec.Definition.Annotations.ContainsKey("dotcraft/nativeApproval"));
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
        bool? requireApprovalOutsideWorkspace)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        Assert.True(config.Tools.File.RequireApprovalOutsideWorkspace);
        var skillsLoader = new SkillsLoader(_workspace);
        var source = new CoreToolSource(
            config,
            TestModelProviderRegistry.Create(),
            skillsLoader,
            new AutoApproveApprovalService(),
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
            requireApprovalOutsideWorkspace: requireApprovalOutsideWorkspace));
    }

    private static async Task<ToolExecutionResult> InvokeAsync(
        ToolRegistration registration,
        JsonObject arguments) =>
        await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "thread-analyst",
                null,
                "call-1",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                registration.Binding.Revision,
                DateTimeOffset.UtcNow),
            arguments);
}
