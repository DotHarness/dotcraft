using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppBinding;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Core.Tests.AppBinding;

public sealed class AppBindingToolCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"binding-catalog-{Guid.NewGuid():N}");
    private string CraftPath => Path.Combine(_root, ".craft");
    private ToolPlanningContext Context => new("thread-1", null, _root, CraftPath, "agent", null, null, 1);

    [Fact]
    public async Task Restart_RetainsApprovedDefinitionAndReturnsOfflineWithoutResettingStore()
    {
        var control = new AppBindingService();
        var (binding, credential) = Activate(control, "com.dotharness.oratorio", Tool("ListBoardItems"));
        var before = Assert.Single(await Registrations(control));
        var restarted = new AppBindingService();
        var after = Assert.Single(await Registrations(restarted));

        Assert.Equal(before.Definition.Name, after.Definition.Name);
        Assert.Equal(before.Definition.Id, after.Definition.Id);
        Assert.Equal(before.Definition.Description, after.Definition.Description);
        Assert.Equal(before.Definition.NamespaceDescription, after.Definition.NamespaceDescription);
        Assert.Equal(before.Definition.InputSchema.GetRawText(), after.Definition.InputSchema.GetRawText());
        Assert.Equal(before.Definition.OutputSchema?.GetRawText(), after.Definition.OutputSchema?.GetRawText());
        Assert.Equal("mcp__com_dotharness_oratorio", after.Definition.Name.Namespace);
        Assert.Equal("ListBoardItems", after.Definition.Id.SourceToolId.Value);
        Assert.Null(McpToolRouting.Generation(after));
        Assert.Equal("com.dotharness.oratorio", restarted.Authenticate(CraftPath, "com.dotharness.oratorio", credential).AppId);
        Assert.Equal(AppBindingStates.Offline, restarted.GetBinding(CraftPath, binding.BindingId).State);

        var result = await Dispatch(after);
        Assert.Equal(AppBindingErrorCodes.Offline, result.Error?.Code);
        using var persisted = JsonDocument.Parse(File.ReadAllText(Path.Combine(CraftPath, "app-bindings", "state.json")));
        Assert.Equal(1, persisted.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Single(Directory.GetFiles(Path.Combine(CraftPath, "app-bindings")));
    }

    [Fact]
    public async Task MixedCatalogs_NormalizeTogetherAndRetainNamesWhenSourcesAreReordered()
    {
        var control = new AppBindingService();
        Activate(control, "com.example.tools", Tool("read-card"), Tool("read_card"), Tool("读卡"), Tool(new string('x', 120)));
        Activate(control, "com.example.other", Tool("read-card"));
        var bindingContributor = new AppBindingToolCatalogContributor(control);
        var ordinary = new StaticCatalogContributor();
        var source = new McpToolSource(null, Config(), [ordinary, bindingContributor]);
        var forward = await source.GetRegistrationsAsync(Context);
        var reverse = await new McpToolSource(null, Config(), [bindingContributor, ordinary]).GetRegistrationsAsync(Context);

        Assert.Equal(6, forward.Count);
        Assert.Equal(6, forward.Select(tool => tool.Definition.Name).Distinct().Count());
        foreach (var registration in forward)
        {
            var name = registration.Definition.Name;
            Assert.Matches("^[a-zA-Z0-9_]+$", name.Namespace!);
            Assert.Matches("^[a-zA-Z0-9_]+$", name.Name);
            Assert.True(name.Namespace!.Length + name.Name.Length + 2 <= 64);
            Assert.Equal(name, Assert.Single(reverse, next => next.Definition.Id == registration.Definition.Id).Definition.Name);
        }
        Assert.Equal(3, forward.Select(tool => tool.Definition.Name.Namespace).Distinct().Count());
    }

    [Fact]
    public async Task PendingExpansion_ExposesOnlyPreviousApprovalUntilAccepted()
    {
        var control = new AppBindingService();
        var (binding, _) = Activate(control, "com.example.tools", Tool("read"));
        var old = Assert.Single(await Registrations(control));
        var changed = Tool("read");
        changed.Description = "Candidate description";
        changed.NamespaceDescription = "Candidate namespace";
        var pending = control.CompleteSync(CraftPath, binding.BindingId, [changed, Tool("write")]);
        Assert.Equal(AppBindingStates.NeedsConfirmation, pending.State);
        var stillApproved = Assert.Single(await Registrations(control));
        Assert.Equal(old.Definition.Description, stillApproved.Definition.Description);
        Assert.Equal(old.Definition.NamespaceDescription, stillApproved.Definition.NamespaceDescription);
        Assert.Equal(AppBindingErrorCodes.Offline, (await Dispatch(stillApproved)).Error?.Code);

        control.ConfirmCapabilities(CraftPath, new ThreadAppBindingConfirmCapabilitiesCommand
        {
            ThreadId = "thread-1", BindingId = binding.BindingId,
            CandidateRevision = pending.CandidateCapabilityRevision!.Value, Decision = "accept"
        }, "user");
        var accepted = await Registrations(control);
        Assert.Equal(2, accepted.Count);
        Assert.Equal(changed.Description, Assert.Single(accepted, tool => tool.Definition.Id.SourceToolId.Value == "read").Definition.Description);
        Assert.Equal(ToolErrorCodes.Unavailable, (await Dispatch(old)).Error?.Code);
    }

    [Fact]
    public async Task DescriptionChanges_DoNotExpandAuthorityAndRevocationInvalidatesFrozenSnapshot()
    {
        var control = new AppBindingService();
        var (binding, _) = Activate(control, "com.example.tools", Tool("read"));
        var changed = Tool("read");
        changed.Description = "Updated description";
        changed.NamespaceDescription = "Updated namespace";
        var synced = control.CompleteSync(CraftPath, binding.BindingId, [changed]);
        Assert.Equal(AppBindingStates.Active, synced.State);
        Assert.Empty(synced.PendingChanges);
        var registration = Assert.Single(await Registrations(control));
        control.RevokeBinding(CraftPath, "thread-1", binding.BindingId, "user");
        Assert.Empty(await Registrations(control));
        Assert.Equal(ToolErrorCodes.Unavailable, (await Dispatch(registration)).Error?.Code);
    }

    [Fact]
    public async Task RejectedExpansion_KeepsApprovedCatalogAndNeverPublishesCandidate()
    {
        var control = new AppBindingService();
        var (binding, _) = Activate(control, "com.example.tools", Tool("read"));
        var pending = control.CompleteSync(CraftPath, binding.BindingId, [Tool("read"), Tool("write")]);
        control.ConfirmCapabilities(CraftPath, new ThreadAppBindingConfirmCapabilitiesCommand
        {
            ThreadId = "thread-1", BindingId = binding.BindingId,
            CandidateRevision = pending.CandidateCapabilityRevision!.Value, Decision = "reject"
        }, "user");
        Assert.Equal("read", Assert.Single(await Registrations(control)).Definition.Id.SourceToolId.Value);
        Assert.Empty(control.GetBinding(CraftPath, binding.BindingId).CandidateTools);
    }

    [Fact]
    public async Task InvalidCatalog_DoesNotReplaceApprovalOrBlockOtherBindings()
    {
        var control = new AppBindingService();
        var (binding, _) = Activate(control, "com.example.tools", Tool("read"));
        Activate(control, "com.example.other", Tool("other"));
        Assert.Throws<ArgumentException>(() => control.CompleteSync(CraftPath, binding.BindingId, [Tool("duplicate"), Tool("duplicate")]));
        Assert.Equal("read", Assert.Single(control.GetBinding(CraftPath, binding.BindingId).ApprovedTools).SourceToolName);

        Assert.Equal(2, (await Registrations(control)).Count);
    }

    private (AppBindingSnapshot Binding, string Credential) Activate(AppBindingService control, string appId, params AppBindingToolCapability[] tools)
    {
        var connection = control.StartConnection(CraftPath, appId, "user");
        var principal = control.Connect(CraftPath, new AppConnectionConnectCommand
        {
            ConnectionRequestId = connection.ConnectionRequestId, RequestToken = connection.RequestToken
        });
        var enabled = control.Enable(CraftPath, "thread-1", appId, "user");
        control.BeginActivation(CraftPath, principal.Principal.PrincipalId, enabled.BindingId, null,
            "http://127.0.0.1:9000/mcp", enabled.BindingRequestId);
        return (control.CompleteSync(CraftPath, enabled.BindingId, tools), principal.Credential);
    }

    private ValueTask<IReadOnlyList<ToolRegistration>> Registrations(AppBindingService control) =>
        new McpToolSource(null, Config(), [new AppBindingToolCatalogContributor(control)]).GetRegistrationsAsync(Context);

    private static AppConfig Config()
    {
        var config = new AppConfig();
        config.Tools.DeferredLoading.Strategy = AppConfig.DeferredLoadingStrategy.Off;
        return config;
    }

    private static AppBindingToolCapability Tool(string name) => new()
    {
        SourceToolName = name, Description = $"Describe {name}", NamespaceDescription = "Approved board tools",
        InputSchema = new JsonObject { ["type"] = "object" }, Visibility = ["model", "app"],
        OutputSchema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["result"] = new JsonObject { ["type"] = "string" } } },
        Annotations = new JsonObject { ["requiresApproval"] = false, ["readOnly"] = true, ["destructive"] = false, ["openWorld"] = false }
    };

    private static ValueTask<ToolExecutionResult> Dispatch(ToolRegistration registration) =>
        new ToolDispatcher().DispatchAsync(new EffectiveToolSnapshotBuilder().Build([registration], 1),
            registration.Definition.Name, [], new ToolInvocationRequest("thread-1", "turn-1", "call-1", ToolInvocationAudience.Model));

    private sealed class StaticCatalogContributor : IMcpToolCatalogContributor, IToolRuntime
    {
        public ValueTask<IReadOnlyList<McpToolCatalog>> GetCatalogsAsync(ToolPlanningContext context, McpClientManager? manager,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<McpToolCatalog>>([
            new("ordinary", "ordinary", "com_example_tools", "workspace", [
                new McpCatalogTool("read-card", "Ordinary MCP tool", JsonSerializer.SerializeToElement(new { type = "object" }), null,
                    new McpAppToolMetadata(null, McpAppVisibility.Model), new ToolPolicyHints(RequiresApproval: false),
                    new Dictionary<string, JsonElement>(), null)
            ], (id, raw) => new ToolRuntimeBinding(new RuntimeBindingId(raw), id, this, ToolBindingLeases.AlwaysAvailable, "ordinary", 1))
        ]);

        public ValueTask<ToolExecutionResult> InvokeAsync(ToolInvocationContext context, JsonObject arguments,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
