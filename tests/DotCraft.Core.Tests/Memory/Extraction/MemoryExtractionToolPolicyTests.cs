using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Memory.Extraction;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Memory.Extraction;

public sealed class MemoryExtractionToolPolicyTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "extraction-policy", "workspace");
    private static readonly string Store = Path.Combine(Workspace, ".craft", "memory");
    private readonly MemoryExtractionToolPolicy _policy = new(Store);

    [Theory]
    [InlineData("ReadFile", "../elsewhere/notes.txt")]
    public async Task Reads_AreAllowedAnywhere(string tool, string path)
    {
        Assert.True(await AllowsAsync(tool, path));
    }

    [Theory]
    [InlineData("WriteFile", ".craft/memory/feedback_tests.md")]
    public async Task MarkdownWritesInsideTheStore_AreAllowed(string tool, string path)
    {
        Assert.True(await AllowsAsync(tool, path));
        Assert.True(await AllowsAsync(tool, Path.Combine(Workspace, path)));
    }

    [Theory]
    [InlineData("WriteFile", "README.md")]
    [InlineData("WriteFile", ".craft/memory/../config.md")]
    [InlineData("WriteFile", ".craft/memory/notes.txt")]
    public async Task WritesOutsideTheStoreOrNotMarkdown_AreDenied(string tool, string path)
    {
        Assert.False(await AllowsAsync(tool, path));
    }

    [Theory]
    [InlineData("Exec", ToolSourceKind.CoreNative)]
    [InlineData("ReadFile", ToolSourceKind.Mcp)]
    [InlineData("WriteFile", ToolSourceKind.PluginNative)]
    public async Task OtherCalls_AreDenied(string tool, ToolSourceKind kind)
    {
        Assert.False(await AllowsAsync(tool, ".craft/memory/fact.md", kind));
    }

    [Fact]
    public async Task StoreWritesThatWouldRunOnARemoteWorkspace_AreDenied()
    {
        var remote = new ToolExecutionLocation("remote", "/remote/workspace");

        Assert.False(await AllowsAsync("WriteFile", ".craft/memory/fact.md", location: remote));
        Assert.False(await AllowsAsync("EditFile", ".craft/memory/MEMORY.md", location: remote));
        Assert.True(await AllowsAsync("ReadFile", ".craft/memory/fact.md", location: remote));
    }

    private async Task<bool> AllowsAsync(
        string tool,
        string path,
        ToolSourceKind kind = ToolSourceKind.CoreNative,
        ToolExecutionLocation? location = null)
    {
        var registration = Registration(tool, kind);
        var context = new ToolInvocationContext(
            "fork",
            "turn_001",
            "call-1",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            1,
            DateTimeOffset.UtcNow,
            WorkspacePath: Workspace,
            ExecutionLocation: location);
        var decision = await _policy.EvaluateAsync(
            context,
            registration,
            new JsonObject { ["path"] = path, ["command"] = "echo hi" });
        return decision.Allowed;
    }

    private static ToolRegistration Registration(string name, ToolSourceKind kind)
    {
        var id = new ToolDefinitionId(kind, "source", new SourceToolId(name));
        var definition = new ToolDefinition(
            id,
            new ToolName(null, name),
            name,
            JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone());
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{name}"),
            id,
            new NoopRuntime(),
            ToolBindingLeases.AlwaysAvailable,
            "authority:test",
            revision: 1);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            ToolExposure.Direct,
            ToolInvocationAudience.Model);
    }

    private sealed class NoopRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }
}
