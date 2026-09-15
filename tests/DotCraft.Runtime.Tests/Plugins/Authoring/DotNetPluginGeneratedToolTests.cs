using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Runtime;
using DotCraft.Tools;
using Xunit;
using static DotCraft.Tests.Runtime.Plugins.PluginRuntimeHarness;

namespace DotCraft.Tests.Runtime.Plugins.Authoring;

public sealed class DotNetPluginGeneratedToolTests :
    IClassFixture<AuthoringReferencePackFixture>,
    IDisposable
{
    private readonly PluginRuntimeHarness _harness = new();
    private readonly DotNetPluginCompiler _compiler;
    private readonly GeneratedToolAuthoringProject _project;

    public DotNetPluginGeneratedToolTests(AuthoringReferencePackFixture fixture)
    {
        _compiler = new DotNetPluginCompiler(fixture.Load());
        _project = new GeneratedToolAuthoringProject(DataRoot);
    }

    private string DataRoot => Path.Combine(_harness.Workspace, ".craft");

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Prepare_GeneratesDeterministicToolsWithoutPublishingOrWritingGeneratedSources()
    {
        _project.Write();
        var fingerprint = PluginBundleFingerprint.Compute(_project.BundleRoot);

        using (var first = Prepare())
        using (var repeated = Prepare())
        {
            Assert.Equal(first.Fingerprint, repeated.Fingerprint);
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(first.BundlePath!, "lib", "Acme.Plugin.dll")),
                File.ReadAllBytes(Path.Combine(repeated.BundlePath!, "lib", "Acme.Plugin.dll")));
            Assert.Equal(fingerprint, PluginBundleFingerprint.Compute(_project.BundleRoot));
            Assert.Equal(
                [_project.SourcePath],
                Directory.GetFiles(Path.Combine(_project.Root, "src"), "*", SearchOption.AllDirectories));
            Assert.Empty(Directory.GetFiles(first.BundlePath!, "DotCraft.*.dll", SearchOption.AllDirectories));
        }

        Assert.Empty(Directory.GetDirectories(_project.Root, ".plugin-stage-*"));
    }

    [Fact]
    public void Prepare_ReportsGenerationErrorsWithInvariantRelativeLocationsBeforeEmit()
    {
        _project.Write(WithoutToolDescription());
        var fingerprint = PluginBundleFingerprint.Compute(_project.BundleRoot);
        var culture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
            using var failed = _compiler.Prepare(DataRoot, GeneratedToolAuthoringProject.PluginId);

            Assert.False(failed.Succeeded);
            var diagnostic = Assert.Single(failed.Diagnostics, diagnostic => diagnostic.Code == "DCGEN002");
            Assert.Equal(PluginDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Equal("generate", diagnostic.Parameters["phase"].GetString());
            Assert.Equal("src/Plugin.cs", diagnostic.Path);
            Assert.True(diagnostic.Parameters["line"].GetInt32() > 0);
            Assert.True(diagnostic.Parameters["column"].GetInt32() > 0);
            Assert.Contains("DescriptionAttribute", diagnostic.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(_harness.Root, diagnostic.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(failed.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CS", StringComparison.Ordinal));
            Assert.Equal(fingerprint, PluginBundleFingerprint.Compute(_project.BundleRoot));
            Assert.Empty(Directory.GetDirectories(_project.Root, ".plugin-stage-*"));
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }
    }

    [Fact]
    public void Prepare_PreservesCompileWarningsAfterSuccessfulGeneration()
    {
        _project.Write("#warning Authored warning\n" + GeneratedToolAuthoringProject.Source);

        using var preparation = Prepare();

        var warning = Assert.Single(preparation.Diagnostics, diagnostic => diagnostic.Code == "CS1030");
        Assert.Equal(PluginDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("compile", warning.Parameters["phase"].GetString());
        Assert.Equal("src/Plugin.cs", warning.Path);
    }

    [Fact]
    public async Task AuthoredTools_DispatchTypedArgumentsWithLiveContextAndNativeResults()
    {
        _project.Write();
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var preparation = Prepare();
        var activated = await manager.ApplyAuthoringBuildAsync(GeneratedToolAuthoringProject.PluginId, preparation);
        AssertState(Assert.IsType<PluginDotnetRuntimeInfo>(activated.Runtime), PluginDotnetRuntimeState.Active);
        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 7);
        var registration = snapshot.Registrations[new ToolName("authored", "execute")];
        Assert.Equal(ToolSourceKind.PluginNative, registration.Definition.Id.Kind);
        Assert.Null(registration.Definition.OutputSchema);
        Assert.False(registration.Definition.InputSchema.GetProperty("properties").TryGetProperty("context", out _));

        using var cancellation = new CancellationTokenSource();
        var dispatcher = new ToolDispatcher();
        var requests = new[]
        {
            new ToolInvocationRequest("thread-a", "turn-a", "call-a", ToolInvocationAudience.Model,
                WorkspacePath: Path.Combine(_harness.Workspace, "a")),
            new ToolInvocationRequest("thread-b", "turn-b", "call-b", ToolInvocationAudience.Model,
                WorkspacePath: Path.Combine(_harness.Workspace, "b"))
        };
        var results = await Task.WhenAll(requests.Select(request => dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            Arguments(request.CallId),
            request,
            cancellation.Token).AsTask()));
        for (var index = 0; index < requests.Length; index++)
        {
            Assert.True(results[index].Success, results[index].Error?.Message);
            Assert.Equal(requests[index].CallId, results[index].Content);
            var content = Assert.IsType<JsonElement>(results[index].StructuredContent);
            Assert.Equal(requests[index].ThreadId, content.GetProperty("threadId").GetString());
            Assert.Equal(requests[index].TurnId, content.GetProperty("turnId").GetString());
            Assert.Equal(requests[index].CallId, content.GetProperty("callId").GetString());
            Assert.Equal(requests[index].WorkspacePath, content.GetProperty("workspace").GetString());
            Assert.Equal("Background", content.GetProperty("mode").GetString());
            Assert.Equal(1000, content.GetProperty("yieldTimeMs").GetInt32());
            Assert.True(content.GetProperty("tokenCanBeCanceled").GetBoolean());
        }

        var unknown = await dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
            Arguments("unknown"), requests[0]);
        Assert.False(unknown.Success);
        Assert.Equal("authored_outcome_unknown", unknown.Error?.Code);
        Assert.Equal("call-a", unknown.Error?.Parameters["executionId"].GetString());
        Assert.Equal("Do not repeat the operation.", unknown.Content);

        var described = await dispatcher.DispatchAsync(snapshot, new ToolName("authored", "describe"),
            Arguments("typed output"), requests[0]);
        Assert.True(described.Success, described.Error?.Message);
        using var document = JsonDocument.Parse(described.Content!);
        Assert.Equal("typed output", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task GenerationError_PreservesPublishedBytesAndTheCallableActiveGeneration()
    {
        _project.Write();
        await using var manager = _harness.CreateManager(trustInstalled: false);
        await manager.StartAsync(CancellationToken.None);
        using var valid = Prepare();
        var active = await manager.ApplyAuthoringBuildAsync(GeneratedToolAuthoringProject.PluginId, valid);
        var fingerprint = PluginBundleFingerprint.Compute(_project.BundleRoot);
        var revision = manager.Snapshot.Revision;
        var snapshot = await BuildSnapshotAsync(manager.ToolSource, revision: 1);
        File.WriteAllText(_project.SourcePath, WithoutToolDescription());

        using var failed = _compiler.Prepare(DataRoot, GeneratedToolAuthoringProject.PluginId);

        Assert.False(failed.Succeeded);
        Assert.Contains(failed.Diagnostics, diagnostic => diagnostic.Code == "DCGEN002");
        Assert.Equal(fingerprint, PluginBundleFingerprint.Compute(_project.BundleRoot));
        Assert.Equal(revision, manager.Snapshot.Revision);
        Assert.Equal(active.Runtime?.GenerationId, Plugin(manager, GeneratedToolAuthoringProject.PluginId).GenerationId);
        var result = await new ToolDispatcher().DispatchAsync(snapshot, new ToolName("authored", "execute"),
            Arguments("still active"), Request("surviving-call"));
        Assert.True(result.Success, result.Error?.Message);
        Assert.Equal("still active", result.Content);
    }

    private DotNetPluginBuildPreparation Prepare()
    {
        var preparation = _compiler.Prepare(DataRoot, GeneratedToolAuthoringProject.PluginId);
        Assert.True(preparation.Succeeded,
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return preparation;
    }

    private static JsonObject Arguments(string text) => new()
    {
        ["input"] = new JsonObject { ["text"] = text, ["mode"] = "background" }
    };

    private static string WithoutToolDescription() => GeneratedToolAuthoringProject.Source.Replace(
        "[Description(\"Execute an authored tool.\")]", "", StringComparison.Ordinal);
}
