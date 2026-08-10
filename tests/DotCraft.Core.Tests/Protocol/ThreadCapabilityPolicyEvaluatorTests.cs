using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Protocol;

public sealed class ThreadCapabilityPolicyEvaluatorTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-thread-policy-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void AllowsTool_ComposesLegacyAndStructuredPolicy()
    {
        var config = new ThreadConfiguration
        {
            ToolAllowList = ["ReadFile", "WriteFile"],
            ToolDenyList = ["WriteFile"],
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile", "Exec"],
                Deny = ["Exec"]
            }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        Assert.True(policy.AllowsTool(Tool("ReadFile")));
        Assert.False(policy.AllowsTool(Tool("WriteFile")));
        Assert.False(policy.AllowsTool(Tool("Exec")));
        Assert.False(policy.AllowsTool(Tool("WebSearch")));
    }

    [Fact]
    public void AllowsTool_AppliesMcpWildcardPolicyToMcpNamedTools()
    {
        var config = new ThreadConfiguration
        {
            McpPolicy = new ThreadMcpPolicy
            {
                Tools = new ThreadNamePolicy
                {
                    Deny = ["mcp__github__*write*"]
                }
            }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        Assert.False(policy.AllowsTool(Tool("mcp__github__write_issue")));
        Assert.True(policy.AllowsTool(Tool("mcp__github__get_issue")));
    }

    [Fact]
    public void EvaluateCall_DeniesStaleToolNamesBeforeResolution()
    {
        var config = new ThreadConfiguration
        {
            ToolPolicy = new ThreadToolPolicy
            {
                Deny = ["WriteFile"]
            }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        var decision = policy.EvaluateCall(new FunctionCallContent(
            "call-1",
            "WriteFile",
            new Dictionary<string, object?> { ["path"] = "a.txt" }));

        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, decision.Kind);
        Assert.Contains("PROFILE_TOOL_POLICY_DENIED", decision.Message, StringComparison.Ordinal);
        Assert.Contains("Tool: WriteFile", decision.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateCall_EnforcesSkillsPolicyBySkillName()
    {
        var config = new ThreadConfiguration
        {
            SkillsPolicy = new ThreadSkillsPolicy
            {
                Allow = ["code-review"],
                Deny = ["secret"],
                AllowManage = false
            }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        var allowed = policy.EvaluateCall(new FunctionCallContent(
            "call-1",
            "SkillView",
            new Dictionary<string, object?> { ["name"] = "code-review" }));
        var deniedByName = policy.EvaluateCall(new FunctionCallContent(
            "call-2",
            "SkillView",
            new Dictionary<string, object?> { ["name"] = "secret" }));
        var deniedManage = policy.EvaluateCall(new FunctionCallContent(
            "call-3",
            "SkillManage",
            new Dictionary<string, object?> { ["action"] = "patch", ["name"] = "code-review" }));

        Assert.Equal(ModeToolPolicyDecisionKind.Allow, allowed.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, deniedByName.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, deniedManage.Kind);
    }

    [Fact]
    public void RuntimeManagedTools_BypassProfilePolicyOnlyWhenRegisteredByTheRuntime()
    {
        var config = new ThreadConfiguration
        {
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile"],
                Deny = ["AssignTask"]
            }
        };
        var runtimeManaged = Registration(
            new ToolName("teams", "AssignTask"),
            ToolSourceKind.PluginNative,
            "agent-teams",
            ToolPolicyScope.RuntimeManaged);
        var profileManaged = Registration(
            new ToolName("teams", "AssignTask"),
            ToolSourceKind.PluginNative,
            "agent-teams");
        var runtimeSnapshot = new EffectiveToolSnapshotBuilder().Build([runtimeManaged], revision: 1);
        var profileSnapshot = new EffectiveToolSnapshotBuilder().Build([profileManaged], revision: 1);
        var runtimeProviderName = runtimeSnapshot.ProviderFlatNames[runtimeManaged.Definition.Name];
        var profileProviderName = profileSnapshot.ProviderFlatNames[profileManaged.Definition.Name];
        var runtimePolicy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());
        runtimePolicy.SetRuntimeManagedTools(runtimeSnapshot);
        var ordinaryPolicy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        var runtimeDecision = runtimePolicy.EvaluateCall(new FunctionCallContent(
            "call-1",
            runtimeProviderName,
            new Dictionary<string, object?>()));
        var unqualifiedDecision = runtimePolicy.EvaluateCall(new FunctionCallContent(
            "call-2",
            "AssignTask",
            new Dictionary<string, object?>()));
        var ordinaryDecision = ordinaryPolicy.EvaluateCall(new FunctionCallContent(
            "call-3",
            profileProviderName,
            new Dictionary<string, object?>()));

        Assert.Equal("teams__AssignTask", runtimeProviderName);
        Assert.Equal(ModeToolPolicyDecisionKind.Allow, runtimeDecision.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, unqualifiedDecision.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, ordinaryDecision.Kind);
        Assert.True(runtimePolicy.AllowsTool(AgentFactory.ProjectSnapshotDefinition(
            runtimeSnapshot,
            runtimeManaged.Definition)));
        Assert.False(ordinaryPolicy.AllowsTool(AgentFactory.ProjectSnapshotDefinition(
            profileSnapshot,
            profileManaged.Definition)));
        Assert.True(runtimePolicy.EvaluateRegistration(runtimeManaged, []).Allowed);
        Assert.False(ordinaryPolicy.EvaluateRegistration(profileManaged, []).Allowed);
    }

    [Fact]
    public void RuntimeManagedTools_UseProviderFlatNameOverrides()
    {
        var config = new ThreadConfiguration
        {
            ToolPolicy = new ThreadToolPolicy { Allow = ["ReadFile"] }
        };
        var registration = Registration(
            new ToolName("teams", "AssignTask"),
            ToolSourceKind.PluginNative,
            "agent-teams",
            ToolPolicyScope.RuntimeManaged,
            providerFlatNameOverride: "teams_runtime_assign");
        var snapshot = new EffectiveToolSnapshotBuilder().Build([registration], revision: 1);
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());
        policy.SetRuntimeManagedTools(snapshot);

        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "teams_runtime_assign",
                new Dictionary<string, object?>())).Kind);
        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-2",
                "teams__AssignTask",
                new Dictionary<string, object?>())).Kind);
    }

    [Fact]
    public void EvaluateRegistration_EnforcesQualifiedMcpAndPlanPolicyAtDispatcherBoundary()
    {
        var config = new ThreadConfiguration
        {
            Mode = "plan",
            McpPolicy = new ThreadMcpPolicy
            {
                Servers = ["catalog-service"],
                Tools = new ThreadNamePolicy { Deny = ["mcp__catalog_service/write_*"] }
            }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());

        Assert.False(policy.EvaluateRegistration(
            Registration(new ToolName("mcp__catalog_service", "write_record"), ToolSourceKind.Mcp, "catalog-service"),
            []).Allowed);
        Assert.False(policy.EvaluateRegistration(
            Registration(new ToolName(null, "WriteFile"), ToolSourceKind.CoreNative, "core"),
            new JsonObject { ["path"] = "notes.txt" }).Allowed);
        Assert.True(policy.EvaluateRegistration(
            Registration(new ToolName("mcp__catalog_service", "get_record"), ToolSourceKind.Mcp, "catalog-service"),
            []).Allowed);
    }

    [Fact]
    public void AllowsRegistrationExposure_UsesRuntimeServerIdentityInsteadOfProviderAlias()
    {
        var config = new ThreadConfiguration
        {
            McpPolicy = new ThreadMcpPolicy { Servers = ["allowed-server"] }
        };
        var policy = new ThreadCapabilityPolicyEvaluator(config, CreateContext());
        var registration = Registration(
            new ToolName("mcp__code_host_apps", "get_me"),
            ToolSourceKind.Mcp,
            "plugin:code-host-apps");

        Assert.False(policy.AllowsRegistrationExposure(registration));
    }

    [Fact]
    public void SubAgentExplorer_KeepsToolVisibleAndDeniesInvocation()
    {
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.True(policy.AllowsTool(Tool("WriteFile")));
        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent("call-1", "WriteFile", new Dictionary<string, object?>())).Kind);
        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent("call-2", "ReadFile", new Dictionary<string, object?>())).Kind);
    }

    [Theory]
    [InlineData("git diff --stat")]
    [InlineData("git --no-pager diff")]
    [InlineData("git diff --stat; git diff --find-renames")]
    [InlineData("git log -p -1")]
    [InlineData("rg SubAgentShellAccess")]
    [InlineData("rg --json ShellAccess")]
    [InlineData("find . -name *.cs")]
    [InlineData("sed -n 1,5p README.md")]
    [InlineData("git status\ngit diff --stat")]
    // A single-quoted literal neutralizes substitution and redirection in both shells, so
    // these stay searchable patterns rather than operators.
    [InlineData("grep 'a > b' README.md")]
    [InlineData("grep '$(rm -f victim)' README.md")]
    // Quoted parentheses are pattern syntax, not grouping.
    [InlineData("rg \"foo(bar)\" README.md")]
    [InlineData("grep 'a(b)' README.md")]
    public void SubAgentExplorer_AllowsReadOnlyShellCommands(string command)
    {
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = command })).Kind);
    }

    [Theory]
    [InlineData("git push origin main")]
    [InlineData("git diff --stat && rm -rf build")]
    [InlineData("git -C ../other status")]
    [InlineData("dotnet test > out.txt")]
    // An unquoted newline separates commands, so the trailing command must be classified too.
    [InlineData("git status\nrm -rf build")]
    [InlineData("git status\r\nrm -rf build")]
    // Allow-listed utilities still carry options that delete files or run external programs.
    [InlineData("find . -delete")]
    [InlineData("find . -exec rm -f {} ;")]
    [InlineData("rg --pre sh pattern")]
    [InlineData("rg --pre=sh pattern")]
    [InlineData("rg -z pattern")]
    [InlineData("sed -n -i s/a/b/ README.md")]
    [InlineData("sed -i s/a/b/ README.md")]
    // Substitution runs inside double quotes, so an allow-listed executable is not enough.
    [InlineData("grep \"$(rm -f victim)\" README.md")]
    [InlineData("grep \"`rm -f victim`\" README.md")]
    [InlineData("Select-String \"$(Remove-Item victim)\" README.md")]
    // Escaped quotes are literal characters, so separators between them are real separators.
    [InlineData("grep \\\"x; rm -rf build; echo \\\" README.md")]
    [InlineData("grep \\'x; rm -rf build; echo \\' README.md")]
    // PowerShell evaluates a parenthesized argument; Bash runs one as a subshell.
    [InlineData("Get-Content (Remove-Item victim)")]
    [InlineData("grep foo README.md && (rm -rf build)")]
    // Expansion synthesizes options the raw tokens never spell out.
    [InlineData(@"find . $'\x2ddelete'")]
    [InlineData("find . -{delete,print}")]
    [InlineData(@"find . \-delete")]
    [InlineData("find . ${IFS}-delete")]
    public void SubAgentExplorer_DeniesMutatingShellCommands(string command)
    {
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = command })).Kind);
    }

    [Theory]
    [InlineData("rg pattern ~/notes")]
    [InlineData("rg [a-z]+ README.md")]
    [InlineData("git log --format=%H | wc -l")]
    public void SubAgentExplorer_DeniesUnquotedSyntaxItCannotClassify(string command)
    {
        // The unquoted character set is closed, so a construct this classifier does not model is
        // refused even when it would not have mutated anything. Quoting is the way through.
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = command })).Kind);
    }

    [Fact]
    public void SubAgentExplorer_DeniesShellOverride()
    {
        // The override becomes the launched executable, so a read-only classification of the
        // command text says nothing about what actually runs.
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?>
                {
                    ["command"] = "git status",
                    ["shell"] = "./workspace-mutating-script"
                })).Kind);
    }

    [Fact]
    public void SubAgentExplorer_DeniesWriteStdin()
    {
        var context = CreateContext(source: SubAgentSource("explorer", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "WriteStdin",
                new Dictionary<string, object?> { ["input"] = "y" })).Kind);
    }

    [Fact]
    public void SubAgentWorker_KeepsShellUnrestricted()
    {
        var context = CreateContext(source: SubAgentSource("worker", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = "git push origin main" })).Kind);
    }

    [Fact]
    public void SubAgentRole_WithAllowListOmittingExec_StillDeniesShell()
    {
        // A workspace role that bounded shell through its allow-list must not gain shell
        // access from the default shell level.
        var appConfig = new AppConfig();
        appConfig.SubAgent.Roles =
        [
            new SubAgentRoleConfig
            {
                Name = "docs-explorer",
                ToolAllowList = ["ReadFile", "GrepFiles", "FindFiles"]
            }
        ];
        var context = CreateContext(source: SubAgentSource("docs-explorer", depth: 1), appConfig: appConfig);
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = "git diff" })).Kind);
        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent(
                "call-2",
                "ReadFile",
                new Dictionary<string, object?>())).Kind);
    }

    [Fact]
    public void SubAgentRole_WithNoneShellAccess_DeniesShellRegardlessOfAllowList()
    {
        var appConfig = new AppConfig();
        appConfig.SubAgent.Roles =
        [
            new SubAgentRoleConfig
            {
                Name = "no-shell",
                ShellAccess = SubAgentShellAccess.None,
                ToolAllowList = ["ReadFile", "Exec"]
            }
        ];
        var context = CreateContext(source: SubAgentSource("no-shell", depth: 1), appConfig: appConfig);
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                "Exec",
                new Dictionary<string, object?> { ["command"] = "git diff" })).Kind);
    }

    [Fact]
    public void SubAgentDefault_KeepsAgentControlVisibleAndDeniesInvocation()
    {
        var context = CreateContext(source: SubAgentSource("default", depth: 1));
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.True(policy.AllowsTool(Tool(nameof(AgentTools.SpawnAgent))));
        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                nameof(AgentTools.SpawnAgent),
                new Dictionary<string, object?>())).Kind);
    }

    [Fact]
    public void SubAgentWorker_DepthPolicyDeniesSpawnAndAllowsOtherAgentControl()
    {
        var config = new AppConfig();
        config.SubAgent.MaxDepth = 1;
        var context = CreateContext(source: SubAgentSource("worker", depth: 1), appConfig: config);
        var policy = new ThreadCapabilityPolicyEvaluator(new ThreadConfiguration(), context);

        Assert.Equal(
            ModeToolPolicyDecisionKind.DenyRecoverable,
            policy.EvaluateCall(new FunctionCallContent(
                "call-1",
                nameof(AgentTools.SpawnAgent),
                new Dictionary<string, object?>())).Kind);
        Assert.Equal(
            ModeToolPolicyDecisionKind.Allow,
            policy.EvaluateCall(new FunctionCallContent(
                "call-2",
                nameof(AgentTools.WaitAgent),
                new Dictionary<string, object?>())).Kind);
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
            // Best-effort cleanup for temp test directories.
        }
    }

    private AgentRuntimeContext CreateContext(
        string originChannel = "test-channel",
        ThreadSource? source = null,
        AppConfig? appConfig = null)
    {
        Directory.CreateDirectory(_tempRoot);
        var craftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(craftPath);
        return new AgentRuntimeContext
        {
            Config = appConfig ?? new AppConfig(),
            ChatClient = new StaticChatClient(),
            WorkspacePath = _tempRoot,
            BotPath = craftPath,
            MemoryStore = new MemoryStore(craftPath),
            SkillsLoader = new SkillsLoader(craftPath),
            ApprovalService = new AutoApproveApprovalService(),
            CurrentThreadId = "thread_policy",
            CurrentThreadSource = source ?? ThreadSource.User(),
            CurrentOriginChannel = originChannel
        };
    }

    private static ThreadSource SubAgentSource(string role, int depth) =>
        ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            ParentThreadId = "thread_parent",
            RootThreadId = "thread_root",
            Depth = depth,
            AgentRole = role,
            RuntimeType = NativeSubAgentRuntime.RuntimeTypeName
        });

    private static AITool Tool(string name) =>
        AIFunctionFactory.Create(() => "ok", name: name);

    private static ToolRegistration Registration(
        ToolName name,
        ToolSourceKind kind,
        string sourceId,
        ToolPolicyScope policyScope = ToolPolicyScope.ProfileManaged,
        string? providerFlatNameOverride = null)
    {
        var id = new ToolDefinitionId(kind, sourceId, new SourceToolId(name.Name));
        return new ToolRegistration(
            new ToolDefinition(
                id,
                name,
                "test",
                JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(),
                provenance: new ToolProvenance(kind, sourceId),
                policyScope: policyScope),
            new ToolRuntimeBinding(
                new RuntimeBindingId($"test:{sourceId}:{name}"),
                id,
                new NoopRuntime(),
                ToolBindingLeases.AlwaysAvailable,
                "test",
                1),
            ToolProjectionShape.StandardPair,
            providerFlatNameOverride: providerFlatNameOverride);
    }

    private sealed class NoopRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class StaticChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
