using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(
    SubAgentCoordinator? subAgentManager = null,
    IEnumerable<SubAgentRoleConfig>? subAgentRoles = null,
    int maxSubAgentDepth = 1,
    ModelPreference? subAgentPreference = null,
    AppConfig? appConfig = null,
    SubAgentWaitAgentTimeoutOptions? waitAgentTimeoutOptions = null,
    int maxConcurrentSubAgents = 16)
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerOptions.Web);

    [Description("Spawn a subagent as a child thread. Use this for collaborative background work when the parent agent can continue while the child thread runs.")]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnAgent))]
    public async Task<string> SpawnAgent(
        [Description("Task prompt for the child agent thread.")] string message,
        [Description("Lowercase task name using only letters, digits, and underscores for this child under the current agent path.")] string taskName,
        [Description("Optional short name shown in UI for this child agent.")] string? agentNickname = null,
        [Description("Optional role label. Built-in roles: default, worker, explorer. Defaults to default when omitted.")] string? agentRole = null,
        [Description("Optional named subagent profile. Defaults to native when omitted.")] string? profile = null,
        [Description("Optional working directory for the child thread. Defaults to the parent thread workspace.")] string? workingDirectory = null,
        [Description("Parent history to fork into the child. Use all, none, or a positive integer string. Defaults to all.")] string? forkTurns = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SpawnAgent is available only inside a Session Core turn.");

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            sessionContext,
            new SubAgentSpawnOptions
            {
                AgentPrompt = message,
                TaskName = taskName,
                AgentNickname = agentNickname,
                AgentRole = agentRole,
                ProfileName = profile,
                WorkingDirectory = workingDirectory,
                RoleConfigs = subAgentRoles?.ToArray(),
                SubAgentPreference = subAgentPreference == null
                    ? null
                    : ModelPreferenceRules.Clone(subAgentPreference),
                RuntimeConfig = appConfig,
                MaxDepth = maxSubAgentDepth,
                MaxConcurrentSubAgents = maxConcurrentSubAgents,
                ForkTurns = forkTurns
            },
            waitForCompletion: false,
            subAgentManager,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Send a mailbox message to another agent path without starting a target turn.")]
    [Tool(Icon = "💬")]
    public async Task<string> SendMessage(
        [Description("Agent path target. Relative targets resolve from the current agent path; absolute targets start with /root.")] string target,
        [Description("Message to place in the target agent mailbox.")] string message,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SendMessage is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.SendMessageAsync(
            sessionContext,
            target,
            message,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Start a follow-up task for an agent path. Pending mailbox messages for the target are delivered as context.")]
    [Tool(Icon = "🧭")]
    public async Task<string> FollowupTask(
        [Description("Agent path target. Relative targets resolve from the current agent path; absolute targets start with /root.")] string target,
        [Description("Task prompt for the target agent turn.")] string message,
        [Description("How to handle a target that is already running: queue starts the task after the active turn, steer injects same-turn guidance for running native SubAgents. Defaults to queue.")] SubAgentFollowupDeliveryMode deliveryMode = SubAgentFollowupDeliveryMode.Queue,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("FollowupTask is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.FollowupTaskAsync(
            sessionContext,
            target,
            message,
            subAgentManager,
            cancellationToken,
            deliveryMode);
        return SerializeResult(result);
    }

    [Description("Wait for a mailbox or SubAgent graph status change.")]
    [Tool(Icon = "⏱️")]
    public async Task<string> WaitAgent(
        [Description("Optional timeout in milliseconds. Uses the configured default and must be within the configured range.")] int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("WaitAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.WaitAgentAsync(
            sessionContext,
            timeoutMs,
            cancellationToken,
            waitAgentTimeoutOptions);
        return SerializeResult(result);
    }

    [Description("List root and available SubAgent paths.")]
    [Tool(Icon = "📋")]
    public async Task<string> ListAgents(
        [Description("Optional path prefix. Relative prefixes resolve from the current agent path; absolute prefixes start with /root.")] string? pathPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("ListAgents is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.ListAgentsAsync(
            sessionContext,
            pathPrefix,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Close a SubAgent path and cancel its active turn if one is running.")]
    [Tool(Icon = "⏹️")]
    public async Task<string> CloseAgent(
        [Description("Agent path target. Relative targets resolve from the current agent path; absolute targets start with /root.")] string target,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("CloseAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.CloseAgentAsync(
            sessionContext,
            target,
            cancellationToken);
        return SerializeResult(result);
    }

    private static string SerializeResult(object result) =>
        JsonSerializer.Serialize(result, ResultJsonOptions);
}
