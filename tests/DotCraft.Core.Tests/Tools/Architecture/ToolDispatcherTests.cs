using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using Xunit;

namespace DotCraft.Core.Tests.Tools.Architecture;

public sealed class ToolDispatcherTests
{
    [Fact]
    public async Task DispatchProviderFlatCallAsync_UsesExactReverseMapAndPreservesCanonicalContext()
    {
        ToolInvocationContext? observed = null;
        var runtime = new DelegateRuntime((context, _) =>
        {
            observed = context;
            return ToolExecutionResult.Succeeded("done", directive: ToolExecutionDirective.TerminateTurn);
        });
        var registration = Registration(new ToolName("workspace", "read"), runtime);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 42);
        var providerName = snapshot.ProviderFlatNames[registration.Definition.Name];

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            providerName,
            new JsonObject { ["path"] = "README.md" },
            new ToolInvocationRequest("thread-1", "turn-1", "call-1", ToolInvocationAudience.Model));

        Assert.True(result.Success);
        Assert.Equal("done", result.Content);
        Assert.Equal(ToolExecutionDirective.TerminateTurn, result.Directive);
        Assert.NotNull(observed);
        Assert.Equal(new ToolName("workspace", "read"), observed.ToolName);
        Assert.Equal("call-1", observed.CallId);
        Assert.Equal(42, observed.SnapshotRevision);
    }

    [Fact]
    public async Task DispatchProviderFlatCallAsync_IsOrdinalAndDoesNotGuessCanonicalName()
    {
        var registration = Registration(new ToolName("workspace", "read"), new DelegateRuntime((_, _) =>
            ToolExecutionResult.Succeeded("unexpected")));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);

        var result = await new ToolDispatcher().DispatchProviderFlatCallAsync(
            snapshot,
            "WORKSPACE__READ",
            [],
            Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.NotFound, result.Error?.Code);
    }

    [Fact]
    public async Task DispatchAsync_RechecksLiveLeaseAndDoesNotInvokeRevokedRuntime()
    {
        var invoked = false;
        var runtime = new DelegateRuntime((_, _) =>
        {
            invoked = true;
            return ToolExecutionResult.Succeeded("unexpected");
        });
        var registration = Registration(
            new ToolName(null, "write"),
            runtime,
            new RevokedLease());
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 2);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Unavailable, result.Error?.Code);
        Assert.False(invoked);
    }

    [Fact]
    public async Task DispatchAsync_RejectsAudienceBeforeLeaseOrRuntime()
    {
        var registration = Registration(
            new ToolName(null, "host_only"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded("unexpected")),
            audiences: ToolInvocationAudience.Host);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 2);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request(ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Unauthorized, result.Error?.Code);
    }

    [Fact]
    public async Task DispatchAsync_HiddenExposureCannotBeInvokedByModelEvenIfAudienceWasMisconfigured()
    {
        var registration = Registration(
            new ToolName(null, "hidden"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded("unexpected")),
            exposure: ToolExposure.Hidden,
            audiences: ToolInvocationAudience.Model | ToolInvocationAudience.Host);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 2);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request(ToolInvocationAudience.Model));

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Unauthorized, result.Error?.Code);
    }

    [Fact]
    public async Task DispatchAsync_NormalizesEmptySuccessfulModelContentAndPreservesHostStructuredResult()
    {
        var structured = Json("""{"count":2}""");
        var registration = Registration(
            new ToolName(null, "structured"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded(null, structured)),
            audiences: ToolInvocationAudience.Model | ToolInvocationAudience.Host);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
        var dispatcher = new ToolDispatcher();

        var modelResult = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request(ToolInvocationAudience.Model));
        var hostResult = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request(ToolInvocationAudience.Host));

        Assert.True(modelResult.Success);
        Assert.Equal("(structured completed with no output)", modelResult.Content);
        Assert.Equal(2, modelResult.StructuredContent?.GetProperty("count").GetInt32());
        Assert.True(hostResult.Success);
        Assert.Equal(2, hostResult.StructuredContent?.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task DispatchAsync_FailedRuntimeResultRequiresStableError()
    {
        var registration = Registration(
            new ToolName(null, "broken"),
            new DelegateRuntime((_, _) => new ToolExecutionResult(false, null)));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.ResultInvalid, result.Error?.Code);
    }

    [Fact]
    public async Task DispatchAsync_RejectsInvalidInputBeforePolicyAndRuntime()
    {
        var invoked = false;
        var registration = Registration(
            new ToolName(null, "requires_name"),
            new DelegateRuntime((_, _) =>
            {
                invoked = true;
                return ToolExecutionResult.Succeeded("unexpected");
            }),
            inputSchema: Json("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.False(invoked);
    }

    [Fact]
    public async Task DispatchAsync_McpArgumentsAreValidatedByTheOwningServer()
    {
        JsonObject? observed = null;
        var registration = Registration(
            new ToolName("mcp__knowledge_service", "answer_query"),
            new DelegateRuntime((_, arguments) =>
            {
                observed = arguments.DeepClone().AsObject();
                return ToolExecutionResult.Succeeded("ok");
            }),
            inputSchema: Json("""
                {
                  "type": "object",
                  "properties": {
                    "target": {
                      "anyOf": [
                        { "type": "string" },
                        { "type": "array", "items": { "type": "string" } }
                      ]
                    }
                  },
                  "required": ["target"]
                }
                """),
            kind: ToolSourceKind.Mcp);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);

        var result = await new ToolDispatcher().DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["target"] = "sample/reference" },
            Request());

        Assert.True(result.Success);
        Assert.Equal("sample/reference", observed?["target"]?.GetValue<string>());
    }

    [Fact]
    public async Task DispatchAsync_OversizedSuccessfulResultRemainsSuccessfulWithBoundedPreview()
    {
        var registration = Registration(
            new ToolName("mcp__knowledge_service", "read_contents"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded($"head-{new string('x', 200)}-tail")),
            kind: ToolSourceKind.Mcp);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);

        var result = await new ToolDispatcher(
                resultNormalizer: new DefaultToolResultNormalizer(maxModelContentCharacters: 80))
            .DispatchAsync(snapshot, registration.Definition.Name, [], Request());

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(80, result.Content?.Length);
        Assert.Contains("Tool result truncated", result.Content);
        Assert.StartsWith("head-", result.Content);
        Assert.EndsWith("-tail", result.Content);
    }

    [Fact]
    public async Task DispatchAsync_PerToolLimitSpillsRichTextAndPreservesImage()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-result-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            var fullText = string.Join('\n', Enumerable.Range(0, 40).Select(index => $"line-{index:D2}-payload"));
            var imageBytes = "image"u8.ToArray();
            var registration = Registration(
                new ToolName(null, "Exec"),
                new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded(
                    fullText,
                    contentItems:
                    [
                        new TextContent(fullText),
                        new DataContent(imageBytes, "image/png")
                    ])),
                annotations: new Dictionary<string, JsonElement>
                {
                    ["dotcraft/maxResultChars"] = JsonSerializer.SerializeToElement(80)
                });
            var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
            var dispatcher = new ToolDispatcher(
                resultNormalizer: new DefaultToolResultNormalizer(
                    maxModelContentCharacters: 1_000,
                    defaultWorkspacePath: workspace,
                    dataPath: Path.Combine(workspace, ".craft"),
                    spillPreviewLines: 2));

            var result = await dispatcher.DispatchAsync(
                snapshot,
                registration.Definition.Name,
                [],
                new ToolInvocationRequest(
                    "thread_result_limit",
                    "turn",
                    "call",
                    ToolInvocationAudience.Model,
                    WorkspacePath: workspace));

            Assert.True(result.Success);
            Assert.Contains(ToolResultProcessor.SpillPreviewMarker, result.Content);
            var spillFile = Assert.Single(Directory.GetFiles(
                Path.Combine(workspace, ".craft", "tool-results", "thread_result_limit"),
                "*.txt"));
            Assert.Equal(fullText, File.ReadAllText(spillFile).TrimStart('\uFEFF'));
            var contentItems = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result.ContentItems);
            Assert.Equal(result.Content, Assert.IsType<TextContent>(contentItems[0]).Text);
            Assert.Equal(imageBytes, Assert.IsType<DataContent>(contentItems[1]).Data.ToArray());
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task DispatchAsync_InvalidInputTerminalizesTheAcceptedProjection()
    {
        var events = new List<string>();
        var probe = new PipelineProbe(events);
        var registration = Registration(
            new ToolName(null, "requires_name"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded("unexpected")),
            inputSchema: Json("""{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
        var dispatcher = new ToolDispatcher(probe, probe, probe, probe, probe, probe);

        var result = await dispatcher.DispatchAsync(snapshot, registration.Definition.Name, [], Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InputInvalid, result.Error?.Code);
        Assert.Equal(["started", "authority", "terminal", "postHook"], events);
    }

    [Fact]
    public async Task DispatchAsync_RunsCommonPipelineInFixedOrder()
    {
        var events = new List<string>();
        var probe = new PipelineProbe(events);
        var registration = Registration(
            new ToolName(null, "ordered"),
            new DelegateRuntime((_, _) =>
            {
                events.Add("runtime");
                return ToolExecutionResult.Succeeded("ok");
            }));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
        var dispatcher = new ToolDispatcher(probe, probe, probe, probe, probe, probe);

        var result = await dispatcher.DispatchAsync(snapshot, registration.Definition.Name, [], Request());

        Assert.True(result.Success);
        Assert.Equal([
            "started", "authority", "policy", "preHook", "approval", "runtime",
            "normalize", "terminal", "postHook"
        ], events);
    }

    [Fact]
    public async Task DispatchAsync_NativeOutsideWorkspaceApprovalIsCentralizedAndConditional()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-dispatch-workspace");
        var outside = Path.Combine(Path.GetTempPath(), "dotcraft-dispatch-outside.txt");
        var invoked = false;
        var approval = new RecordingApprovalService(approved: false);
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approval);
        var annotation = JsonSerializer.SerializeToElement(new
        {
            kind = "file",
            targetArgument = "path",
            operation = "write",
            workspacePath = workspace,
            outsideWorkspaceOnly = true,
            trustedReadPaths = Array.Empty<string>()
        });
        var registration = Registration(
            new ToolName(null, "WriteFile"),
            new DelegateRuntime((_, _) =>
            {
                invoked = true;
                return ToolExecutionResult.Succeeded("unexpected");
            }),
            inputSchema: Json("""{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
            annotations: new Dictionary<string, JsonElement> { ["dotcraft/nativeApproval"] = annotation },
            policyHints: new ToolPolicyHints(RequiresApproval: true));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        var dispatcher = new ToolDispatcher(approvalEvaluator: evaluator);

        var denied = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["path"] = outside },
            Request());

        Assert.False(denied.Success);
        Assert.Equal(ToolErrorCodes.ApprovalRejected, denied.Error?.Code);
        Assert.Equal(1, approval.FileApprovalCalls);
        Assert.False(invoked);

        var inside = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["path"] = Path.Combine(workspace, "notes.txt") },
            Request());
        Assert.True(inside.Success);
        Assert.Equal(1, approval.FileApprovalCalls);
        Assert.True(invoked);
    }

    [Fact]
    public async Task CommonApproval_SkillMutationOnlyPromptsForCreateAndDelete()
    {
        var approval = new RecordingApprovalService(approved: true);
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approval);
        var registration = Registration(
            new ToolName(null, "SkillManage"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded("ok")),
            inputSchema: Json("""{"type":"object","properties":{"action":{"type":"string"},"name":{"type":"string"}},"required":["action","name"]}"""),
            annotations: new Dictionary<string, JsonElement>
            {
                ["dotcraft/nativeApproval"] = JsonSerializer.SerializeToElement(new
                {
                    kind = "remoteResource",
                    targetArgument = "name",
                    operationArgument = "action",
                    whenOperationIn = new[] { "create", "delete" }
                })
            },
            policyHints: new ToolPolicyHints(RequiresApproval: true));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        var dispatcher = new ToolDispatcher(approvalEvaluator: evaluator);

        Assert.True((await dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
            new JsonObject { ["action"] = "edit", ["name"] = "demo" }, Request())).Success);
        Assert.Equal(0, approval.ResourceApprovalCalls);
        Assert.True((await dispatcher.DispatchAsync(snapshot, registration.Definition.Name,
            new JsonObject { ["action"] = "delete", ["name"] = "demo" }, Request())).Success);
        Assert.Equal(1, approval.ResourceApprovalCalls);
    }

    [Fact]
    public async Task CommonApproval_UsesPluginRoutingDescriptor()
    {
        var approval = new RecordingApprovalService(approved: true);
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approval);
        var registration = Registration(
            new ToolName("channel", "send_message"),
            new DelegateRuntime((_, _) => ToolExecutionResult.Succeeded("ok")),
            inputSchema: Json("""{"type":"object","properties":{"recipient":{"type":"string"},"operation":{"type":"string"}},"required":["recipient","operation"]}"""),
            annotations: new Dictionary<string, JsonElement>
            {
                ["dotcraft/pluginApproval"] = JsonSerializer.SerializeToElement(new
                {
                    kind = "externalChannel",
                    targetArgument = "recipient",
                    operationArgument = "operation"
                })
            },
            policyHints: new ToolPolicyHints(RequiresApproval: true));
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        var dispatcher = new ToolDispatcher(approvalEvaluator: evaluator);

        var result = await dispatcher.DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["recipient"] = "room-42", ["operation"] = "send" },
            Request());

        Assert.True(result.Success);
        Assert.Equal(1, approval.ResourceApprovalCalls);
        Assert.Equal(("externalChannel", "send", "room-42"), approval.LastResourceApproval);
    }

    [Fact]
    public async Task CommonApproval_PluginFileGuardRejectsBlacklistedPathBeforeInvocation()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-workspace");
        var blockedPath = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-blocked", "secret.txt");
        var invoked = false;
        var approval = new RecordingApprovalService(approved: true);
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approval);
        var registration = PluginFileRegistration(() => invoked = true, requiredTarget: true);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        using var scope = SetPluginScope(workspace, approval, new PathBlacklist([Path.GetDirectoryName(blockedPath)!]));

        var result = await new ToolDispatcher(approvalEvaluator: evaluator).DispatchAsync(
            snapshot,
            registration.Definition.Name,
            new JsonObject { ["path"] = blockedPath },
            Request());

        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.AccessDenied, result.Error?.Code);
        Assert.Equal(0, approval.FileApprovalCalls);
        Assert.False(invoked);
    }

    [Fact]
    public async Task CommonApproval_PluginOptionalTargetMissingSkipsApproval()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-workspace");
        var invoked = false;
        var approval = new RecordingApprovalService(approved: false);
        var evaluator = new CommonToolApprovalEvaluator();
        evaluator.Bind(approval);
        var registration = PluginFileRegistration(() => invoked = true, requiredTarget: false);
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], 1);
        using var scope = SetPluginScope(workspace, approval, new PathBlacklist([]));

        var result = await new ToolDispatcher(approvalEvaluator: evaluator).DispatchAsync(
            snapshot,
            registration.Definition.Name,
            [],
            Request());

        Assert.True(result.Success);
        Assert.Equal(0, approval.FileApprovalCalls);
        Assert.True(invoked);
    }

    private static ToolRegistration PluginFileRegistration(Action invoke, bool requiredTarget)
    {
        var inputSchema = requiredTarget
            ? Json("""{"type":"object","required":["path"],"properties":{"path":{"type":"string"}}}""")
            : Json("""{"type":"object","properties":{"path":{"type":"string"}}}""");
        return Registration(
            new ToolName("plugin", "write_file"),
            new DelegateRuntime((_, _) =>
            {
                invoke();
                return ToolExecutionResult.Succeeded("ok");
            }),
            inputSchema: inputSchema,
            annotations: new Dictionary<string, JsonElement>
            {
                ["dotcraft/pluginApproval"] = JsonSerializer.SerializeToElement(new
                {
                    kind = "file",
                    targetArgument = "path",
                    operation = "write"
                })
            },
            policyHints: new ToolPolicyHints(RequiresApproval: true),
            kind: ToolSourceKind.PluginNative);
    }

    private static IDisposable SetPluginScope(
        string workspace,
        IApprovalService approval,
        PathBlacklist blacklist) =>
        PluginFunctionExecutionScope.Set(new PluginFunctionExecutionContext
        {
            ThreadId = "thread",
            TurnId = "turn",
            OriginChannel = "test",
            WorkspacePath = workspace,
            RequireApprovalOutsideWorkspace = true,
            ApprovalService = approval,
            PathBlacklist = blacklist,
            Turn = new SessionTurn { Id = "turn", ThreadId = "thread" },
            NextItemSequence = () => 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        });

    private static ToolInvocationRequest Request(
        ToolInvocationAudience audience = ToolInvocationAudience.Model) =>
        new("thread", "turn", "call", audience);

    private static ToolRegistration Registration(
        ToolName name,
        IToolRuntime runtime,
        IToolBindingLease? lease = null,
        ToolExposure exposure = ToolExposure.Direct,
        ToolInvocationAudience audiences = ToolInvocationAudience.Model | ToolInvocationAudience.Host,
        JsonElement? inputSchema = null,
        IReadOnlyDictionary<string, JsonElement>? annotations = null,
        ToolPolicyHints? policyHints = null,
        ToolSourceKind kind = ToolSourceKind.CoreNative)
    {
        var id = new ToolDefinitionId(
            kind,
            $"source-{name}",
            new SourceToolId(name.Name));
        var definition = new ToolDefinition(
            id,
            name,
            "A test tool",
            inputSchema ?? Json("""{"type":"object"}"""),
            annotations: annotations,
            policyHints: policyHints);
        var binding = new ToolRuntimeBinding(
            new RuntimeBindingId($"binding-{name}"),
            id,
            runtime,
            lease ?? ToolBindingLeases.AlwaysAvailable,
            "authority:test",
            revision: 1);
        return new ToolRegistration(
            definition,
            binding,
            ToolProjectionShape.StandardPair,
            exposure,
            audiences);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class DelegateRuntime(
        Func<ToolInvocationContext, JsonObject, ToolExecutionResult> invoke) : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(invoke(context, arguments));
    }

    private sealed class RevokedLease : IToolBindingLease
    {
        public ValueTask<ToolBindingLeaseResult> CheckAsync(
            ToolInvocationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolBindingLeaseResult.Unavailable("The binding was revoked."));
    }

    private sealed class RecordingApprovalService(bool approved) : IApprovalService
    {
        public int FileApprovalCalls { get; private set; }
        public int ResourceApprovalCalls { get; private set; }
        public (string Kind, string Operation, string Target)? LastResourceApproval { get; private set; }

        public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        {
            FileApprovalCalls++;
            return Task.FromResult(approved);
        }

        public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null) =>
            Task.FromResult(approved);

        public Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
        {
            ResourceApprovalCalls++;
            LastResourceApproval = (kind, operation, target);
            return Task.FromResult(approved);
        }
    }

    private sealed class PipelineProbe(List<string> events) :
        IToolAuthorityEvaluator,
        IToolPolicyEvaluator,
        IToolDispatchHookRunner,
        IToolApprovalEvaluator,
        IToolInvocationRecorder,
        IToolResultNormalizer
    {
        public ValueTask<ToolDispatchDecision> CheckAsync(ToolInvocationContext context, ToolRegistration registration, CancellationToken cancellationToken = default)
        {
            events.Add("authority");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask<ToolDispatchDecision> EvaluateAsync(ToolInvocationContext context, ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            events.Add("policy");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask<ToolDispatchDecision> RunPreToolUseAsync(ToolInvocationContext context, ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            events.Add("preHook");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask RunTerminalAsync(ToolInvocationContext context, ToolRegistration registration, ToolExecutionResult result, CancellationToken cancellationToken = default)
        {
            events.Add("postHook");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ToolDispatchDecision> RequestAsync(ToolInvocationContext context, ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            events.Add("approval");
            return ValueTask.FromResult(ToolDispatchDecision.Allow);
        }

        public ValueTask RecordStartedAsync(ToolInvocationContext context, ToolRegistration registration, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            events.Add("started");
            return ValueTask.CompletedTask;
        }

        public ValueTask RecordTerminalAsync(ToolInvocationContext context, ToolRegistration registration, ToolExecutionResult result, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            events.Add("terminal");
            return ValueTask.CompletedTask;
        }

        public ValueTask<ToolExecutionResult> NormalizeAsync(ToolInvocationContext context, ToolRegistration registration, ToolExecutionResult result, CancellationToken cancellationToken = default)
        {
            events.Add("normalize");
            return ValueTask.FromResult(result);
        }
    }
}
