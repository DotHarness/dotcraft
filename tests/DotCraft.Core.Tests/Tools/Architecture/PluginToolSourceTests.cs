using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class PluginToolSourceTests
{
    [Fact]
    public async Task Registration_SeparatesQualifiedDefinitionFromRuntimeBinding()
    {
        var invoker = new RecordingInvoker();
        var source = CreateSource(invoker, deferLoading: true);

        var registration = Assert.Single(await source.GetRegistrationsAsync(CreatePlanningContext()));

        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        Assert.Equal("example-plugin", registration.Definition.Id.SourceId);
        Assert.Equal(new ToolName("example", "lookup"), registration.Definition.Name);
        Assert.Equal(ToolExposure.Deferred, registration.Exposure);
        Assert.Equal("example", registration.Deferred?.Namespace);
        Assert.Equal(registration.Definition.Id, registration.Binding.DefinitionId);
        Assert.IsType<PluginToolRuntime>(registration.Binding.Runtime);
    }

    [Fact]
    public async Task Runtime_PreservesProviderCallId_AndDoesNotRequireAmbientPluginScope()
    {
        var invoker = new RecordingInvoker
        {
            Result = new PluginFunctionInvocationResult
            {
                ContentItems = [new PluginFunctionContentItem { Type = "text", Text = "done" }],
                StructuredResult = new JsonObject { ["private"] = true }
            }
        };
        var registration = Assert.Single(await CreateSource(invoker).GetRegistrationsAsync(CreatePlanningContext()));
        var invocation = new ToolInvocationContext(
            "thread_1",
            "turn_1",
            "provider-call-17",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            9,
            DateTimeOffset.UtcNow);

        var result = await registration.Binding.Runtime.InvokeAsync(
            invocation,
            new JsonObject { ["query"] = "value" });

        Assert.True(result.Success);
        Assert.Equal("done", result.Content);
        Assert.Equal("provider-call-17", invoker.LastContext?.Invocation.CallId);
        Assert.Equal("thread_1", invoker.LastContext?.Invocation.ThreadId);
        Assert.Equal("desktop", invoker.LastContext?.OriginChannel);
        Assert.Equal("value", invoker.LastContext?.Arguments["query"]?.GetValue<string>());
        Assert.True(result.StructuredContent?.GetProperty("private").GetBoolean());
        Assert.Null(PluginFunctionExecutionScope.Current);
    }

    [Fact]
    public async Task Runtime_MapsSourceFailureToStableCommonError()
    {
        var invoker = new RecordingInvoker
        {
            Result = PluginFunctionInvocationResult.Failed("AdapterOffline", "adapter unavailable")
        };
        var registration = Assert.Single(await CreateSource(invoker).GetRegistrationsAsync(CreatePlanningContext()));
        var result = await registration.Binding.Runtime.InvokeAsync(
            new ToolInvocationContext(
                "thread_1",
                "turn_1",
                "call_1",
                ToolInvocationAudience.Model,
                registration.Definition.Name,
                registration.Definition.Id,
                registration.Binding.Id,
                9,
                DateTimeOffset.UtcNow),
            new JsonObject());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.Error?.Code);
        Assert.Equal(
            "AdapterOffline",
            result.Error?.Parameters["sourceErrorCode"].GetString());
    }

    [Fact]
    public async Task Runtime_NonCallerCancellationPreservesLegacyTimeoutClassification()
    {
        var invoker = new RecordingInvoker { Exception = new OperationCanceledException("plugin timeout") };
        var registration = Assert.Single(await CreateSource(invoker).GetRegistrationsAsync(CreatePlanningContext()));

        var result = await registration.Binding.Runtime.InvokeAsync(CreateInvocation(registration), []);

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Timeout, result.Error?.Code);
        Assert.Equal("PluginFunctionTimeout", result.Error?.Parameters["sourceErrorCode"].GetString());
        Assert.Contains("timed out", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Runtime_RequiresChatContextRejectsBeforeInvoker()
    {
        var invoker = new RecordingInvoker();
        var source = CreateSource(
            invoker,
            requiresChatContext: true,
            invocationMetadata: new PluginToolInvocationMetadata("desktop"));
        var registration = Assert.Single(await source.GetRegistrationsAsync(CreatePlanningContext()));

        var result = await registration.Binding.Runtime.InvokeAsync(CreateInvocation(registration), []);

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ExecutionFailed, result.Error?.Code);
        Assert.Equal("MissingChatContext", result.Error?.Parameters["sourceErrorCode"].GetString());
        Assert.Null(invoker.LastContext);
    }

    [Fact]
    public async Task SameLocalNameInDifferentNamespaces_HasDistinctCanonicalAndSourceIdentity()
    {
        var invoker = new RecordingInvoker();
        var descriptorA = CreateDescriptor("alpha", "lookup");
        var descriptorB = CreateDescriptor("beta", "lookup");
        var source = new PluginToolSource(
            "example-plugin",
            [new PluginToolRegistration(descriptorA, invoker), new PluginToolRegistration(descriptorB, invoker)]);

        var snapshot = await new EffectiveToolSnapshotBuilder().BuildAsync(
            [source],
            CreatePlanningContext());

        Assert.Equal(2, snapshot.Registrations.Count);
        Assert.Contains(new ToolName("alpha", "lookup"), snapshot.Registrations.Keys);
        Assert.Contains(new ToolName("beta", "lookup"), snapshot.Registrations.Keys);
        Assert.Equal(
            2,
            snapshot.Registrations.Values.Select(item => item.Definition.Id.SourceToolId).Distinct().Count());
    }

    private static PluginToolSource CreateSource(
        RecordingInvoker invoker,
        bool deferLoading = false,
        bool requiresChatContext = false,
        PluginToolInvocationMetadata? invocationMetadata = null) =>
        new(
            "example-plugin",
            [
                new PluginToolRegistration(
                    new PluginFunctionDescriptor
                    {
                        PluginId = "example-plugin",
                        Namespace = "example",
                        Name = "lookup",
                        Description = "Looks up an example.",
                        DeferLoading = deferLoading,
                        RequiresChatContext = requiresChatContext,
                        InputSchema = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["query"] = new JsonObject { ["type"] = "string" }
                            }
                        }
                    },
                    invoker)
            ],
            invocationMetadata ?? new PluginToolInvocationMetadata("desktop", "chat:1", "user:1"));

    private static ToolInvocationContext CreateInvocation(ToolRegistration registration) =>
        new(
            "thread_1",
            "turn_1",
            "call_1",
            ToolInvocationAudience.Model,
            registration.Definition.Name,
            registration.Definition.Id,
            registration.Binding.Id,
            9,
            DateTimeOffset.UtcNow);

    private static PluginFunctionDescriptor CreateDescriptor(string toolNamespace, string name) =>
        new()
        {
            PluginId = "example-plugin",
            Namespace = toolNamespace,
            Name = name,
            Description = "Looks up an example.",
            InputSchema = new JsonObject { ["type"] = "object" }
        };

    private static ToolPlanningContext CreatePlanningContext() =>
        new("thread_1", "turn_1", Path.GetTempPath(), Path.Combine(Path.GetTempPath(), ".craft"), "default", null, [], 9);

    private sealed class RecordingInvoker : IPluginToolInvoker
    {
        public PluginFunctionInvocationResult Result { get; init; } =
            new() { ContentItems = [new PluginFunctionContentItem { Type = "text", Text = "ok" }] };

        public PluginToolInvocationContext? LastContext { get; private set; }

        public Exception? Exception { get; init; }

        public ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginToolInvocationContext context,
            CancellationToken cancellationToken)
        {
            if (Exception is not null)
                throw Exception;
            LastContext = context;
            return ValueTask.FromResult(Result);
        }
    }
}
