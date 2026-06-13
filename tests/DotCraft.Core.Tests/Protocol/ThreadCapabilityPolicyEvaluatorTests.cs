using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

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
    public void EvaluateCall_PreservesTeamsReservedToolsUnderRestrictiveProfile()
    {
        var config = new ThreadConfiguration
        {
            ToolPolicy = new ThreadToolPolicy
            {
                Allow = ["ReadFile"]
            },
            TeamsPolicy = new ThreadTeamsPolicy
            {
                ReservedTools = "keep"
            }
        };
        var teamsPolicy = new ThreadCapabilityPolicyEvaluator(config, CreateContext(originChannel: "teams"));
        var ordinaryPolicy = new ThreadCapabilityPolicyEvaluator(config, CreateContext(originChannel: "test-channel"));

        var teamsDecision = teamsPolicy.EvaluateCall(new FunctionCallContent(
            "call-1",
            "AssignTask",
            new Dictionary<string, object?>()));
        var ordinaryDecision = ordinaryPolicy.EvaluateCall(new FunctionCallContent(
            "call-2",
            "AssignTask",
            new Dictionary<string, object?>()));

        Assert.Equal(ModeToolPolicyDecisionKind.Allow, teamsDecision.Kind);
        Assert.Equal(ModeToolPolicyDecisionKind.DenyRecoverable, ordinaryDecision.Kind);
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

    private ToolProviderContext CreateContext(string originChannel = "test-channel")
    {
        Directory.CreateDirectory(_tempRoot);
        var craftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(craftPath);
        return new ToolProviderContext
        {
            Config = new AppConfig(),
            ChatClient = new StaticChatClient(),
            WorkspacePath = _tempRoot,
            BotPath = craftPath,
            MemoryStore = new MemoryStore(craftPath),
            SkillsLoader = new SkillsLoader(craftPath),
            ApprovalService = new AutoApproveApprovalService(),
            CurrentThreadId = "thread_policy",
            CurrentThreadSource = ThreadSource.User(),
            CurrentOriginChannel = originChannel
        };
    }

    private static AITool Tool(string name) =>
        AIFunctionFactory.Create(() => "ok", name: name);

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
