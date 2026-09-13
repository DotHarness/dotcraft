using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.DotNetPluginTestBundle;
using static DotCraft.Tests.Runtime.Plugins.GeneratedPluginToolFixture;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class GeneratedPluginToolTests : IDisposable
{
    private readonly PluginGenerationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GeneratedTools_BindTypedArgumentsAndLiveContextThroughThePluginBoundary()
    {
        var generation = await ActivateAsync();
        try
        {
            var snapshot = await SnapshotAsync();
            var definition = snapshot.Registrations[new ToolName("probe", "exercise")].Definition;
            Assert.Equal(ToolSourceKind.PluginNative, definition.Id.Kind);
            Assert.Equal(PluginId, definition.Id.SourceId);
            Assert.Equal(ToolSourceKind.PluginNative, definition.Provenance.Kind);
            Assert.True(definition.PolicyHints.ReadOnly);
            Assert.Null(definition.Presentation);
            Assert.Null(definition.OutputSchema);
            Assert.False(definition.InputSchema.GetProperty("properties").TryGetProperty("context", out _));

            var result = await DispatchAsync(snapshot, "exercise", Input(), "live-call");

            Assert.True(result.Success, result.Error?.Message);
            Assert.Equal("typed call completed", result.Content);
            var output = AssertStructured(result);
            Assert.Equal("live-thread", output.GetProperty("threadId").GetString());
            Assert.Equal("live-turn", output.GetProperty("turnId").GetString());
            Assert.Equal("live-call", output.GetProperty("callId").GetString());
            Assert.Equal(_harness.WorkspaceRoot, output.GetProperty("workspace").GetString());
            Assert.Equal("PluginNative", output.GetProperty("sourceKind").GetString());
            Assert.Equal(PluginId, output.GetProperty("sourceId").GetString());
            Assert.Equal(37, output.GetProperty("revision").GetInt64());
            Assert.Equal("typed input", output.GetProperty("title").GetString());
            Assert.Equal("nested input", output.GetProperty("label").GetString());
            Assert.Equal("Inline", output.GetProperty("mode").GetString());
            Assert.Equal(1000, output.GetProperty("yieldTimeMs").GetInt32());
            Assert.Equal(1, output.GetProperty("calls").GetInt32());
            Assert.Null(result.Meta);
            Assert.Null(result.RawSourceResult);
            Assert.Null(result.ProviderResult);
            Assert.Null(result.ContentItems);
            Assert.Equal(ToolExecutionDirective.Continue, result.Directive);

            var explicitInput = Input();
            explicitInput["mode"] = "background";
            explicitInput["yieldTimeMs"] = 0;
            explicitInput["extra"] = new JsonObject { ["applicationValue"] = new JsonArray(1, "two") };
            var explicitResult = await DispatchAsync(snapshot, "exercise", explicitInput, "second-call");
            Assert.True(explicitResult.Success, explicitResult.Error?.Message);
            var explicitOutput = AssertStructured(explicitResult);
            Assert.Equal("Background", explicitOutput.GetProperty("mode").GetString());
            Assert.Equal(0, explicitOutput.GetProperty("yieldTimeMs").GetInt32());
            Assert.Equal("two", explicitOutput.GetProperty("extra").GetProperty("applicationValue")[1].GetString());
            Assert.Equal(2, explicitOutput.GetProperty("calls").GetInt32());

            var forgedInput = Input();
            forgedInput["context"] = new JsonObject { ["threadId"] = "forged-thread" };
            var forgedResult = await DispatchAsync(snapshot, "exercise", forgedInput, "forged-call");
            Assert.False(forgedResult.Success);
            Assert.Equal(ToolErrorCodes.InputInvalid, forgedResult.Error?.Code);
        }
        finally
        {
            await generation.BeginCleanup();
        }
    }

    [Fact]
    public async Task GeneratedTools_PreserveDomainErrorsAndExplicitUncertainCancellationResults()
    {
        var generation = await ActivateAsync();
        try
        {
            var snapshot = await SnapshotAsync();
            var failure = await DispatchAsync(snapshot, "domain_error", [], "error-call");
            Assert.False(failure.Success);
            Assert.Equal("ProbeTargetMissing", failure.Error?.Code);
            Assert.Equal("Reconnect the selected target.", failure.Content);

            foreach (var uncertain in new[] { false, true })
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var callId = uncertain ? "uncertain-call" : "cancelled-call";
                var invocation = DispatchAsync(snapshot, "wait", new JsonObject { ["uncertain"] = uncertain },
                    callId, cancellation.Token);
                await WaitForFileAsync(_harness.DataFile(PluginId, "started-" + callId));
                await cancellation.CancelAsync();

                var result = await invocation;

                Assert.False(result.Success);
                Assert.Equal(uncertain ? "ProbeOutcomeUnknown" : ToolErrorCodes.Cancelled, result.Error?.Code);
                if (uncertain)
                    Assert.Equal("Execution may have started; do not replay.", result.Content);
            }
        }
        finally
        {
            await generation.BeginCleanup();
        }
    }

    [Fact]
    public async Task GeneratedTools_ReclaimTypedPluginAfterDrainingWhileOldHostSnapshotRemainsAlive()
    {
        var retained = await ExerciseAndRetireAsync();

        var loadContext = Assert.IsType<WeakReference>(retained.Remnant.LoadContext);
        var collectionWait = Stopwatch.StartNew();
        var cacheEvictionTriggered = false;
        while (loadContext.IsAlive && collectionWait.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (!cacheEvictionTriggered && collectionWait.Elapsed >= TimeSpan.FromSeconds(1.2))
            {
                // System.Text.Json evicts stale reflection emit accessors only while creating another accessor.
                _ = JsonSerializer.Deserialize<MemberAccessorCacheProbe>("{\"value\":\"probe\"}");
                cacheEvictionTriggered = true;
            }
            OfferCollection();
            await Task.Delay(20);
        }

        Assert.False(loadContext.IsAlive,
            "Typed input/output serializer and schema caches must not retain the retired plugin load context.");
        Assert.Equal("typed input", AssertStructured(retained.TypedResult).GetProperty("title").GetString());
        using var output = JsonDocument.Parse(retained.DtoResult.Content!);
        Assert.Equal("nested output", output.RootElement.GetProperty("detail").GetProperty("label").GetString());
        var stale = await DispatchAsync(retained.Snapshot, "exercise", Input(), "stale-call");
        Assert.False(stale.Success);
        Assert.Equal(ToolErrorCodes.Unavailable, stale.Error?.Code);
        GC.KeepAlive(retained);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<RetainedHostState> ExerciseAndRetireAsync()
    {
        var generation = await ActivateAsync();
        try
        {
            var snapshot = await SnapshotAsync();
            var input = Input();
            input["mode"] = "background";
            var typed = await DispatchAsync(snapshot, "exercise", input, "typed-call");
            Assert.True(typed.Success, typed.Error?.Message);
            var dtoDefinition = snapshot.Registrations[new ToolName("probe", "describe")].Definition;
            var dtoSchema = Assert.IsType<JsonElement>(dtoDefinition.OutputSchema);
            Assert.True(dtoSchema.GetProperty("properties").TryGetProperty("detail", out _));
            var dto = await DispatchAsync(snapshot, "describe", [], "dto-call");
            Assert.True(dto.Success, dto.Error?.Message);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var activeCall = DispatchAsync(snapshot, "hold", [], "draining-call", deadline.Token);
            await WaitForFileAsync(_harness.DataFile(PluginId, "started-draining-call"));
            var cleanup = generation.BeginCleanup();
            try
            {
                Assert.False(cleanup.IsCompleted);
                Assert.False(File.Exists(_harness.DataFile(PluginId, "disposed")));
                var revoked = await DispatchAsync(snapshot, "describe", [], "revoked-call");
                Assert.Equal(ToolErrorCodes.Unavailable, revoked.Error?.Code);
            }
            finally
            {
                File.WriteAllText(_harness.DataFile(PluginId, "release"), "yes");
            }

            Assert.Equal("released", (await activeCall).Content);
            var remnant = await cleanup;
            Assert.True(File.Exists(_harness.DataFile(PluginId, "disposed")));
            Assert.Empty(remnant.CleanupErrors);
            return new(remnant, snapshot, typed, dto);
        }
        finally
        {
            await generation.BeginCleanup();
        }
    }

    private async Task<PluginGeneration> ActivateAsync()
    {
        WritePluginBundle(_harness.PluginRoot(PluginId), PluginId, "GeneratedPlugin.Plugin", Source,
            transformCompilation: GenerateTools);
        var activation = await _harness.ActivateAsync(PluginId);
        Assert.True(activation.Generation != null, activation.Error);
        return Assert.IsType<PluginGeneration>(activation.Generation);
    }

    private Task<EffectiveToolSnapshot> SnapshotAsync() => new EffectiveToolSnapshotBuilder().BuildAsync(
        [new DotNetPluginToolSource(_harness.Registry, _harness.CallGates,
            static (_, _, _) => throw new InvalidOperationException("Local test tools must not request remote export."))],
        new ToolPlanningContext("planning-thread", "planning-turn", Path.GetTempPath(), ".craft", "default",
            null, null, 37)).AsTask();

    private Task<ToolExecutionResult> DispatchAsync(EffectiveToolSnapshot snapshot, string name, JsonObject arguments,
        string callId, CancellationToken cancellationToken = default) => new ToolDispatcher().DispatchAsync(
        snapshot, new ToolName("probe", name), arguments,
        new ToolInvocationRequest("live-thread", "live-turn", callId, ToolInvocationAudience.Model,
            WorkspacePath: _harness.WorkspaceRoot), cancellationToken).AsTask();

    private static JsonObject Input() => new()
    {
        ["input"] = new JsonObject
        {
            ["title"] = "typed input",
            ["detail"] = new JsonObject { ["label"] = "nested input" }
        }
    };

    private static JsonElement AssertStructured(ToolExecutionResult result) =>
        Assert.IsType<JsonElement>(result.StructuredContent);

    private sealed record RetainedHostState(PluginGenerationRemnant Remnant, EffectiveToolSnapshot Snapshot,
        ToolExecutionResult TypedResult, ToolExecutionResult DtoResult);

    private sealed class MemberAccessorCacheProbe
    {
        public string Value { get; set; } = string.Empty;
    }
}
