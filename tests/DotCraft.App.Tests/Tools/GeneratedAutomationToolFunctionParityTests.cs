using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Automations;
using DotCraft.Automations.Local;
using DotCraft.Hosting;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.Tests.Tools;

public sealed class GeneratedAutomationToolFunctionParityTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"generated_automation_tools_{Guid.NewGuid():N}");

    public GeneratedAutomationToolFunctionParityTests()
    {
        Directory.CreateDirectory(_tempRoot);
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
            // Best-effort cleanup on Windows.
        }
    }

    [Fact]
    public async Task CompleteLocalTask_SourceDefinitionMatchesAIFunctionFactoryShape()
    {
        var store = CreateStore();
        var taskDir = Path.Combine(_tempRoot, "task");
        var workspace = Path.Combine(taskDir, "workspace");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(taskDir, "task.md"), "status: running");
        var source = new LocalTaskCompletionToolSource(
            store,
            NullLogger<LocalTaskCompletionToolSource>.Instance);
        var registration = Assert.Single(await source.GetRegistrationsAsync(
            new ToolPlanningContext("thread_test", null, workspace, "agent", "local-task", [], 1)));
        var factory = CreateFactoryCompleteLocalTask(store, taskDir);

        Assert.Equal(factory.Name, registration.Definition.Name.Name);
        Assert.Equal(factory.Description, registration.Definition.Description);
        AssertJsonEqual(factory.JsonSchema, registration.Definition.InputSchema, "CompleteLocalTask raw input schema");
        AssertNullableJsonEqual(factory.ReturnJsonSchema, registration.Definition.OutputSchema, "CompleteLocalTask return schema");
    }

    private LocalTaskFileStore CreateStore()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(craftPath);
        return new LocalTaskFileStore(
            new AutomationsConfig(),
            new DotCraftPaths { WorkspacePath = _tempRoot, CraftPath = craftPath },
            NullLogger<LocalTaskFileStore>.Instance);
    }

    private static AIFunction CreateFactoryCompleteLocalTask(LocalTaskFileStore store, string taskDir)
    {
        var methodsType = typeof(LocalTaskCompletionToolSource).Assembly.GetType(
            "DotCraft.Automations.Local.LocalTaskCompletionToolMethods",
            throwOnError: true)!;
        var methods = Activator.CreateInstance(
            methodsType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [store, NullLogger.Instance, taskDir],
            culture: null)!;
        var method = methodsType.GetMethod(
            "CompleteLocalTask",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(string), typeof(CancellationToken)],
            modifiers: null)!;
        var del = method.CreateDelegate<Func<string, CancellationToken, Task<string>>>(methods);
        return AIFunctionFactory.Create(del);
    }

    private static void AssertNullableJsonEqual(JsonElement? expected, JsonElement? actual, string because)
    {
        Assert.Equal(expected.HasValue, actual.HasValue);
        if (expected.HasValue)
            AssertJsonEqual(expected.Value, actual!.Value, because);
    }

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual, string because)
    {
        var expectedNode = JsonNode.Parse(expected.GetRawText());
        var actualNode = JsonNode.Parse(actual.GetRawText());
        Assert.True(JsonNode.DeepEquals(expectedNode, actualNode), $"{because}\nExpected: {expected}\nActual:   {actual}");
    }
}
