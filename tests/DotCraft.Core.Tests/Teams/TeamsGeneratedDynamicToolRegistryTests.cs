using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Teams;

namespace DotCraft.Tests.Teams;

public sealed class TeamsGeneratedDynamicToolRegistryTests
{
    [Fact]
    public void GeneratedRegistry_MatchesReflectionFallbackToolSpecs()
    {
        var generatedSpecs = new TeamsService().ToolSpecs
            .Concat(new TeamsService().GetToolSpecsForSurface(ManagedAppBindingToolSurfaces.ThreadBinding))
            .OrderBy(spec => spec.Name, StringComparer.Ordinal)
            .ToList();
        var fallbackSpecs = new ManagedDynamicToolRegistry<TeamsService>(TeamsConstants.ToolNamespace).ToolSpecs
            .OrderBy(spec => spec.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(fallbackSpecs.Select(spec => spec.Name), generatedSpecs.Select(spec => spec.Name));

        for (var i = 0; i < fallbackSpecs.Count; i++)
            AssertSpecEqual(fallbackSpecs[i], generatedSpecs[i]);
    }

    [Fact]
    public async Task GeneratedRegistry_InvocationMatchesReflectionFallbackBindingErrors()
    {
        var service = new TeamsService();
        var generated = ReadGeneratedRegistry();
        var fallback = new ManagedDynamicToolRegistry<TeamsService>(TeamsConstants.ToolNamespace);
        var context = Context("ReadMemberStatus");
        var arguments = new JsonObject();

        var generatedError = await Assert.ThrowsAsync<AppServerException>(async () =>
            await generated.InvokeAsync(service, context, arguments, CancellationToken.None));
        var fallbackError = await Assert.ThrowsAsync<AppServerException>(async () =>
            await fallback.InvokeAsync(service, context, arguments, CancellationToken.None));

        Assert.Equal(fallbackError.Code, generatedError.Code);
        Assert.Equal(ErrorDetail(fallbackError), ErrorDetail(generatedError));
    }

    private static IManagedDynamicToolRegistry<TeamsService> ReadGeneratedRegistry()
    {
        var field = typeof(TeamsService).GetField("DynamicTools", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IManagedDynamicToolRegistry<TeamsService>>(field.GetValue(null));
    }

    private static void AssertSpecEqual(DynamicToolSpec expected, DynamicToolSpec actual)
    {
        Assert.Equal(expected.Namespace, actual.Namespace);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.DeferLoading, actual.DeferLoading);
        Assert.True(
            PluginFunctionSchemaValidator.TryValidateSchema(actual.InputSchema!, out var message),
            message);
        Assert.True(
            JsonNode.DeepEquals(expected.InputSchema, actual.InputSchema),
            $"{expected.Name} schema\nExpected: {expected.InputSchema}\nActual:   {actual.InputSchema}");
    }

    private static ManagedAppBindingToolCallContext Context(string toolName) =>
        new(
            WorkspaceCraftPath: "craft",
            WorkspacePath: "workspace",
            BindingId: "binding",
            ThreadId: "thread",
            TurnId: "turn",
            CallId: "call",
            AppId: "app",
            GrantId: "grant",
            ToolName: toolName);

    private static string ErrorDetail(AppServerException ex) =>
        JsonSerializer.SerializeToNode(ex.ErrorData)!["detail"]!.GetValue<string>();
}
