using System.ComponentModel;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Sessions;
using ModelPreference = DotCraft.Configuration.ModelPreference;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(
    SubAgentCoordinator? subAgentCoordinator = null,
    IEnumerable<SubAgentRoleConfig>? subAgentRoles = null,
    int maxSubAgentDepth = 1,
    ModelPreference? subAgentPreference = null,
    AppConfig? appConfig = null,
    int maxConcurrentSubAgents = 16,
    SubAgentModelCatalogSnapshot? modelCatalogSnapshot = null,
    string? inheritedModel = null)
{
    private static readonly JsonSerializerOptions ResultJsonOptions = new(JsonSerializerOptions.Web);

    [Description("Start a subagent in the background. You will be notified when it finishes. Omit model to use the configured SubAgent or runtime default, and set it only when an explicit override is needed.")]
    [Tool]
    [ToolPresentation("core.subagent", Operation = "spawn")]
    public async Task<string> SpawnAgent(
        [Description("Task prompt for the child agent thread.")] string message,
        [Description("Lowercase task name using only letters, digits, and underscores for this child under the current agent path.")] string taskName,
        [Description("Optional short name shown in UI for this child agent.")] string? agentNickname = null,
        [Description("Optional role label. Built-in roles: default, worker, explorer. Defaults to default when omitted.")] string? agentRole = null,
        [Description("Optional named subagent profile. Defaults to native when omitted.")] string? profile = null,
        [Description("Optional working directory for the child thread. Defaults to the parent thread workspace.")] string? workingDirectory = null,
        [Description("Parent history to fork into the child. Use all, none, or a positive integer string. Defaults to all. Full-history forks inherit the parent model and reasoning.")] string? forkTurns = null,
        [Description("Optional model override for a fresh or bounded native child. Omit unless an explicit override is needed.")] string? model = null,
        [Description("Optional reasoning effort override for a fresh or bounded native child. Omit to inherit the configured effort.")] string? reasoningEffort = null,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SpawnAgent is available only inside a Session Core turn.");

        if ((!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(reasoningEffort))
            && IsFullHistoryFork(forkTurns))
        {
            throw new InvalidOperationException(
                "Full-history native SubAgents inherit the parent model and reasoning; model overrides require forkTurns=none or a positive integer.");
        }

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
                InvocationModelOverride = SubAgentModelCatalogSnapshots.ResolveInvocationOverride(
                    modelCatalogSnapshot,
                    model,
                    reasoningEffort,
                    inheritedModel),
                InvocationModelCatalogSnapshot = modelCatalogSnapshot,
                MaxDepth = maxSubAgentDepth,
                MaxConcurrentSubAgents = maxConcurrentSubAgents,
                ForkTurns = forkTurns
            },
            waitForCompletion: false,
            subAgentCoordinator,
            cancellationToken);
        result.Message ??= "The subagent is working in the background. You will be notified when it finishes. You know nothing about its result until then, so do not predict it or repeat its work. Continue other work, or briefly tell the user what you started and end your response.";
        return SerializeResult(result);
    }

    [Description("Send a message to another agent. A running agent receives it during its current turn, and an idle or stopped agent is woken to handle it. A subagent you spawned works on it in the background, and you will be notified when it finishes. To wait for a reply, end your turn; the reply wakes you.")]
    [Tool]
    [ToolPresentation("core.subagent", Operation = "sendMessage")]
    public async Task<string> SendMessage(
        [Description("Agent path target. Relative targets resolve from the current agent path; absolute targets start with /root.")] string target,
        [Description("Message or task for the target agent.")] string message,
        CancellationToken cancellationToken = default)
    {
        var sessionContext = SubAgentSessionScope.Current
            ?? throw new InvalidOperationException("SendMessage is available only inside a Session Core turn.");
        var result = await SubAgentSessionControl.SendAgentMessageAsync(
            sessionContext,
            target,
            message,
            subAgentCoordinator,
            cancellationToken);
        return SerializeResult(result);
    }

    [Description("List root and available SubAgent paths. You will be notified when a subagent finishes, so do not use this to check its progress.")]
    [Tool]
    [ToolPresentation("core.subagent", Operation = "list")]
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

    private static string SerializeResult(object result) =>
        JsonSerializer.Serialize(result, ResultJsonOptions);

    private static bool IsFullHistoryFork(string? forkTurns) =>
        string.IsNullOrWhiteSpace(forkTurns)
        || string.Equals(forkTurns.Trim(), "all", StringComparison.OrdinalIgnoreCase);
}
