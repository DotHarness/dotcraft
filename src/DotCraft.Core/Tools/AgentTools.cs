using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Protocol;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(
    SubAgentCoordinator? subAgentManager = null,
    IEnumerable<SubAgentRoleConfig>? subAgentRoles = null,
    int maxSubAgentDepth = 1,
    string? subAgentModel = null)
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerOptions.Web);

    [Description("Spawn a subagent as a child thread. Use this for collaborative background work when the parent agent can continue while the child thread runs." +
                 "Returned childThreadId can be passed to SendInput, WaitAgent, ResumeAgent, and CloseAgent.")]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnAgent))]
    public async Task<string> SpawnAgent(
        [Description("Task prompt for the child agent thread.")] string agentPrompt,
        [Description("Optional short name shown in UI for this child agent.")] string? agentNickname = null,
        [Description("Optional role label. Built-in roles: default, worker, explorer. Defaults to default when omitted.")] string? agentRole = null,
        [Description("Optional named subagent profile. Defaults to native when omitted.")] string? profile = null,
        [Description("Optional working directory for the child thread. Defaults to the parent thread workspace.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SpawnAgent is available only inside a Session Core turn.");

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            sessionContext,
            new SubAgentSpawnOptions
            {
                AgentPrompt = agentPrompt,
                AgentNickname = agentNickname,
                AgentRole = agentRole,
                ProfileName = profile,
                WorkingDirectory = workingDirectory,
                RoleConfigs = subAgentRoles?.ToArray(),
                SubAgentModel = subAgentModel,
                MaxDepth = maxSubAgentDepth
            },
            waitForCompletion: false,
            subAgentManager,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Send another user message to a session-backed child agent thread. " +
                 "Work for native profiles and for external CLI profiles only when the profile supports resume and workspace resume is enabled.")]
    [Tool(Icon = "💬")]
    public async Task<string> SendInput(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        [Description("Message to send to the child agent.")] string message,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SendInput is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.SendInputAsync(
            sessionContext.SessionService,
            childThreadId,
            message,
            subAgentManager,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Wait for a session-backed child agent thread to finish its current turn and return its final message.")]
    [Tool(Icon = "⏱️")]
    public async Task<string> WaitAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        [Description("Optional timeout in seconds. Omit or pass 0 to wait without a timeout.")] int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("WaitAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.WaitAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            timeoutSeconds,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Resume a paused or closed child agent thread and reopen its parent-child edge.")]
    [Tool(Icon = "▶️")]
    public async Task<string> ResumeAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("ResumeAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.ResumeAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("Close a child agent thread edge and cancel its active turn if one is running.")]
    [Tool(Icon = "⏹️")]
    public async Task<string> CloseAgent(
        [Description("Child agent thread id returned by SpawnAgent.")] string childThreadId,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("CloseAgent is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.CloseAgentAsync(
            sessionContext.SessionService,
            childThreadId,
            cancellationToken);
        return SerializeResult(result);
    }

    private static string SerializeResult(SubAgentControlResult result) =>
        JsonSerializer.Serialize(result, ResultJsonOptions);
}
