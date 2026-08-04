using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Memory;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Context;

public sealed class MemoryConsolidationToolPolicyTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "MemoryPolicy_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly MemoryStore _memoryStore;

    public MemoryConsolidationToolPolicyTests()
    {
        Directory.CreateDirectory(_tempDir);
        _memoryStore = new MemoryStore(_tempDir);
        _memoryStore.EnsureHistoryFile();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Theory]
    [InlineData("ReadFile", "MEMORY.md")]
    [InlineData("WriteFile", "HISTORY.md")]
    [InlineData("EditFile", "MEMORY.md")]
    public void Evaluate_AllowsDirectFileToolsForMemoryFiles(string toolName, string fileName)
    {
        var policy = new MemoryConsolidationToolPolicy(_memoryStore, _tempDir);
        var path = Path.Combine(_memoryStore.MemoryDirectoryPath, fileName);

        var decision = policy.Evaluate(CreateContext(toolName, new Dictionary<string, object?>
        {
            ["path"] = path
        }));

        Assert.Equal(ModeToolPolicyDecisionKind.Allow, decision.Kind);
    }

    [Theory]
    [InlineData("ReadFile", "README.md")]
    [InlineData("WriteFile", "../outside.md")]
    [InlineData("EditFile", "memory/OTHER.md")]
    public void Evaluate_DeniesDirectFileToolsOutsideMemoryFiles(string toolName, string path)
    {
        var policy = new MemoryConsolidationToolPolicy(_memoryStore, _tempDir);

        var decision = policy.Evaluate(CreateContext(toolName, new Dictionary<string, object?>
        {
            ["path"] = path
        }));

        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, decision.Kind);
        Assert.Contains("MEMORY_CONSOLIDATION_TOOL_POLICY_DENIED", decision.Message);
    }

    [Fact]
    public void Evaluate_DeniesNonFileTools()
    {
        var policy = new MemoryConsolidationToolPolicy(_memoryStore, _tempDir);

        var decision = policy.Evaluate(CreateContext("Exec", new Dictionary<string, object?>
        {
            ["command"] = "dotnet test"
        }));

        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, decision.Kind);
        Assert.Contains("Tool: Exec", decision.Message);
    }

    [Fact]
    public void Evaluate_AllowsSearchOnlyWhenScopedToMemoryFiles()
    {
        var policy = new MemoryConsolidationToolPolicy(_memoryStore, _tempDir);

        var allowed = policy.Evaluate(CreateContext("GrepFiles", new Dictionary<string, object?>
        {
            ["path"] = _memoryStore.MemoryDirectoryPath,
            ["include"] = "MEMORY.md;HISTORY.md"
        }));
        var denied = policy.Evaluate(CreateContext("GrepFiles", new Dictionary<string, object?>
        {
            ["path"] = _tempDir,
            ["include"] = "*.md"
        }));

        Assert.Equal(ModeToolPolicyDecisionKind.Allow, allowed.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, denied.Kind);
    }

    private static FunctionInvocationContext CreateContext(
        string toolName,
        IDictionary<string, object?> arguments)
    {
        var function = AIFunctionFactory.Create(() => "ok", name: toolName);
        return new FunctionInvocationContext
        {
            Function = function,
            Arguments = new AIFunctionArguments(arguments),
            CallContent = new FunctionCallContent("call-1", toolName, arguments)
        };
    }
}
