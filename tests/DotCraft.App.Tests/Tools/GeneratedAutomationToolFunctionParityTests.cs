using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Automations;
using DotCraft.Automations.Local;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
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
    public void CompleteLocalTask_GeneratedWrapperMatchesAIFunctionFactoryShape()
    {
        var store = CreateStore();
        var taskDir = Path.Combine(_tempRoot, "task");
        var provider = new LocalTaskCompletionToolProvider(
            store,
            NullLogger<LocalTaskCompletionToolProvider>.Instance);
        var generated = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.CreateTools(CreateContext(taskDir))));
        var factory = CreateFactoryCompleteLocalTask(store, taskDir);

        Assert.Equal(factory.Name, generated.Name);
        Assert.Equal(factory.Description, generated.Description);
        AssertJsonEqual(factory.JsonSchema, generated.JsonSchema, "CompleteLocalTask raw input schema");
        AssertNullableJsonEqual(factory.ReturnJsonSchema, generated.ReturnJsonSchema, "CompleteLocalTask return schema");
        Assert.Same(factory.JsonSerializerOptions, generated.JsonSerializerOptions);
        Assert.NotNull(factory.UnderlyingMethod);
        Assert.Null(generated.UnderlyingMethod);
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

    private ToolProviderContext CreateContext(string taskDir)
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        return new ToolProviderContext
        {
            Config = new AppConfig(),
            ChatClient = null!,
            WorkspacePath = _tempRoot,
            AutomationTaskDirectory = taskDir,
            BotPath = craftPath,
            MemoryStore = new MemoryStore(craftPath),
            SkillsLoader = new SkillsLoader(craftPath),
            ApprovalService = new AutoApproveApprovalService()
        };
    }

    private static AIFunction CreateFactoryCompleteLocalTask(LocalTaskFileStore store, string taskDir)
    {
        var methodsType = typeof(LocalTaskCompletionToolProvider).Assembly.GetType(
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
