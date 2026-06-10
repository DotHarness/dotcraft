using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;


// ───── Wire protocol method name constants ─────

public static class AppServerMethods
{
    // Client → Server requests
    public const string Initialize = "initialize";

    /// <summary>
    /// Lists known origin channels for cross-channel thread visibility (Desktop settings).
    /// </summary>
    public const string ChannelList = "channel/list";

    /// <summary>
    /// Returns runtime enabled/running status for all configured social and external channels (Desktop channels panel).
    /// See spec Section 20.
    /// </summary>
    public const string ChannelStatus = "channel/status";
    public const string ProviderList = "provider/list";
    public const string ProviderCreate = "provider/create";
    public const string ProviderUpdate = "provider/update";
    public const string ProviderDelete = "provider/delete";
    public const string ProviderTest = "provider/test";
    public const string ModelList = "model/list";
    public const string AuthOpenAiStatus = "auth/openai/status";
    public const string AuthOpenAiLogin = "auth/openai/login";
    public const string AuthOpenAiLogout = "auth/openai/logout";
    public const string AuthOpenAiUsage = "auth/openai/usage";
    public const string ThreadStart = "thread/start";
    public const string ThreadFork = "thread/fork";
    public const string WorktreeCreateAndFork = "worktree/createAndFork";
    public const string WorktreeCreateAndStart = "worktree/createAndStart";
    public const string ThreadWorktreeHandoff = "thread/worktree/handoff";
    public const string WorktreeList = "worktree/list";
    public const string WorktreeStatus = "worktree/status";
    public const string ThreadResume = "thread/resume";
    public const string ThreadList = "thread/list";
    public const string ThreadRead = "thread/read";
    public const string ThreadGoalGet = "thread/goal/get";
    public const string ThreadGoalSet = "thread/goal/set";
    public const string ItemWidgetStateSet = "item/widget-state/set";
    public const string ThreadGoalClear = "thread/goal/clear";
    public const string ThreadCompactStart = "thread/compact/start";
    public const string ThreadMemoryConsolidateStart = "thread/memory/consolidate/start";
    public const string ThreadMaintenanceInterrupt = "thread/maintenance/interrupt";
    public const string ThreadRollback = "thread/rollback";
    public const string ThreadSubscribe = "thread/subscribe";
    public const string ThreadUnsubscribe = "thread/unsubscribe";
    public const string ThreadPause = "thread/pause";
    public const string ThreadArchive = "thread/archive";
    public const string ThreadUnarchive = "thread/unarchive";
    public const string ThreadDelete = "thread/delete";
    public const string ThreadRename = "thread/rename";
    public const string ThreadModeSet = "thread/mode/set";
    public const string ThreadConfigUpdate = "thread/config/update";
    public const string TurnStart = "turn/start";
    public const string TurnEnqueue = "turn/enqueue";
    public const string TurnQueueRemove = "turn/queue/remove";
    public const string TurnQueueReorder = "turn/queue/reorder";
    public const string TurnSteer = "turn/steer";
    public const string TurnInterrupt = "turn/interrupt";
    public const string TerminalList = "terminal/list";
    public const string TerminalRead = "terminal/read";
    public const string TerminalWrite = "terminal/write";
    public const string TerminalStop = "terminal/stop";
    public const string TerminalClean = "terminal/clean";

    /// <summary>Generate a suggested git commit message from thread context and diff (Desktop).</summary>
    public const string WorkspaceCommitMessageSuggest = "workspace/commitMessage/suggest";
    public const string WelcomeSuggestions = "welcome/suggestions";
    public const string WorkspaceConfigSchema = "workspace/config/schema";
    public const string WorkspaceConfigUpdate = "workspace/config/update";
    public const string WorkspaceConfigChanged = "workspace/configChanged";
    public const string DreamsStatus = "dreams/status";
    public const string DreamsRun = "dreams/run";
    public const string DreamsCreate = "dreams/create";
    public const string DreamsGet = "dreams/get";
    public const string DreamsList = "dreams/list";
    public const string DreamsCancel = "dreams/cancel";
    public const string DreamsArchive = "dreams/archive";
    public const string DreamsApply = "dreams/apply";
    public const string DreamsDiscard = "dreams/discard";
    public const string MemoryReset = "memory/reset";

    /// <summary>Aggregate usage telemetry pull (spec Section 27A). Available when tracing is enabled.</summary>
    public const string UsageSummary = "usage/summary";

    /// <summary>Per-day token usage for activity charts (spec Section 27A.3). Available when tracing is enabled.</summary>
    public const string UsageTimeseries = "usage/timeseries";

    /// <summary>Profile activity insights: most-used model/reasoning, skill counts, thread count (spec Section 27A.5). Available when tracing is enabled.</summary>
    public const string ProfileInsights = "profile/insights";

    public const string McpList = "mcp/list";
    public const string McpGet = "mcp/get";
    public const string McpUpsert = "mcp/upsert";
    public const string McpRemove = "mcp/remove";
    public const string ExternalChannelList = "externalChannel/list";
    public const string ExternalChannelGet = "externalChannel/get";
    public const string ExternalChannelUpsert = "externalChannel/upsert";
    public const string ExternalChannelRemove = "externalChannel/remove";
    public const string ExternalChannelLogs = "externalChannel/logs";
    public const string SubAgentProfileList = "subagent/profiles/list";
    public const string SubAgentSettingsUpdate = "subagent/settings/update";
    public const string SubAgentProfileSetEnabled = "subagent/profiles/setEnabled";
    public const string SubAgentProfileUpsert = "subagent/profiles/upsert";
    public const string SubAgentProfileRemove = "subagent/profiles/remove";
    public const string SubAgentChildrenList = "subagent/children/list";
    public const string SubAgentSendMessage = "subagent/sendMessage";
    public const string SubAgentFollowupTask = "subagent/followupTask";
    public const string SubAgentClose = "subagent/close";
    public const string McpStatusList = "mcp/status/list";
    public const string McpTest = "mcp/test";

    // Client → Server notification (no id)
    public const string Initialized = "initialized";

    // Server → Client notifications
    public const string ThreadStarted = "thread/started";
    public const string ThreadUpdated = "thread/updated";
    public const string ThreadDeleted = "thread/deleted";
    public const string ThreadResumed = "thread/resumed";
    public const string ThreadStatusChanged = "thread/statusChanged";
    /// <summary>Workspace-level runtime snapshot broadcast for sidebar activity indicators.</summary>
    public const string ThreadRuntimeChanged = "thread/runtimeChanged";
    public const string ThreadQueueUpdated = "thread/queue/updated";
    /// <summary>Server broadcast when a thread's display name changes (rename RPC or first-message title).</summary>
    public const string ThreadRenamed = "thread/renamed";
    public const string ThreadGoalUpdated = "thread/goal/updated";
    public const string ThreadGoalCleared = "thread/goal/cleared";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string TurnFailed = "turn/failed";
    public const string TurnCancelled = "turn/cancelled";
    public const string ItemStarted = "item/started";
    public const string ItemAgentMessageDelta = "item/agentMessage/delta";
    public const string ItemReasoningDelta = "item/reasoning/delta";
    public const string ItemCommandExecutionOutputDelta = "item/commandExecution/outputDelta";
    public const string ItemToolCallArgumentsDelta = "item/toolCall/argumentsDelta";
    public const string ItemCompleted = "item/completed";
    public const string ItemApprovalResolved = "item/approval/resolved";
    public const string ItemRequestUserInputResolved = "item/tool/requestUserInput/resolved";
    public const string TerminalStarted = "terminal/started";
    public const string TerminalOutputDelta = "terminal/outputDelta";
    public const string TerminalCompleted = "terminal/completed";
    public const string TerminalStalled = "terminal/stalled";
    public const string TerminalCleaned = "terminal/cleaned";

    // Server → Client request (bidirectional approval)
    public const string ItemApprovalRequest = "item/approval/request";
    public const string ItemRequestUserInput = "item/tool/requestUserInput";
    public const string ItemToolCall = "item/tool/call";

    /// <summary>
    /// Server → App request: read a <c>ui://</c> Interactive Tool UI resource owned by the app.
    /// Brokered from the host's <c>ui/resource/read</c>. See appserver-protocol.md §11.3.1.
    /// </summary>
    public const string ItemResourceRead = "item/resource/read";

    // Server → Client notification (SubAgent progress)
    public const string SubAgentProgress = "subagent/progress";
    public const string SubAgentGraphChanged = "subagent/graphChanged";

    // Server → Client notification (incremental token usage)
    public const string ItemUsageDelta = "item/usage/delta";

    // Server → Client notification (system maintenance events)
    public const string SystemEvent = "system/event";

    // Server → Client notification (plan/todo progress updates, spec Section 6.8)
    public const string PlanUpdated = "plan/updated";

    // Server → Client notification (cron/heartbeat job result, spec Section 6.9)
    public const string SystemJobResult = "system/jobResult";

    // Server → Client notification (cron job list sync, spec Section 16.7)
    public const string CronStateChanged = "cron/stateChanged";
    public const string McpStatusUpdated = "mcp/status/updated";

    /// <summary>
    /// Server → Client notification fired during <c>auth/openai/login</c> with the authorization URL.
    /// Lets the desktop UI render a "Copy URL" affordance while the request is pending.
    /// </summary>
    public const string AuthOpenAiAuthorizeUrl = "auth/openai/authorizeUrl";

    /// <summary>
    /// Server → Client notification fired whenever the ChatGPT usage / rate-limit snapshot changes.
    /// Payload follows <see cref="AuthOpenAiUsageResult"/>; null payload means "no longer available".
    /// </summary>
    public const string AuthOpenAiUsageChanged = "auth/openai/usageChanged";

    // Server → Client requests (external channel adapter, ext-channel-adapter spec §6)
    public const string ExtChannelSend = "ext/channel/send";
    public const string ExtChannelToolCall = "ext/channel/toolCall";
    public const string ExtChannelHeartbeat = "ext/channel/heartbeat";

    // Server → Client requests (ACP tool proxy, appserver-protocol.md §11.2)
    public const string ExtAcpFsReadTextFile = "ext/acp/fs/readTextFile";
    public const string ExtAcpFsWriteTextFile = "ext/acp/fs/writeTextFile";
    public const string ExtAcpTerminalCreate = "ext/acp/terminal/create";
    public const string ExtAcpTerminalGetOutput = "ext/acp/terminal/getOutput";
    public const string ExtAcpTerminalWaitForExit = "ext/acp/terminal/waitForExit";
    public const string ExtAcpTerminalKill = "ext/acp/terminal/kill";
    public const string ExtAcpTerminalRelease = "ext/acp/terminal/release";

    // Server → Client requests (Desktop Node REPL + IAB browser runtime)
    public const string ExtNodeReplEvaluate = "ext/nodeRepl/evaluate";
    public const string ExtNodeReplCancel = "ext/nodeRepl/cancel";

    // Client → Server requests (cron management, spec Section 16)
    public const string CronList = "cron/list";
    public const string CronRemove = "cron/remove";
    public const string CronEnable = "cron/enable";
    public const string CronRun = "cron/run";

    // Client → Server requests (heartbeat management, spec Section 17)
    public const string HeartbeatTrigger = "heartbeat/trigger";

    // Client → Server requests (skills management, spec Section 18)
    public const string SkillsList = "skills/list";
    public const string SkillsRead = "skills/read";
    public const string SkillsView = "skills/view";
    public const string SkillsRestoreOriginal = "skills/restoreOriginal";
    public const string SkillsSetEnabled = "skills/setEnabled";
    public const string SkillsUninstall = "skills/uninstall";

    // Client → Server requests (plugin management)
    public const string PluginList = "plugin/list";
    public const string PluginView = "plugin/view";
    public const string PluginInstall = "plugin/install";
    public const string PluginRemove = "plugin/remove";
    public const string PluginSetEnabled = "plugin/setEnabled";

    // Client → Server requests (command management, spec Section 19)
    public const string CommandList = "command/list";
    public const string CommandExecute = "command/execute";

    // Client → Server requests (automations)
    public const string AutomationTaskList = "automation/task/list";
    public const string AutomationTaskRead = "automation/task/read";
    public const string AutomationTaskCreate = "automation/task/create";
    public const string AutomationTaskRun = "automation/task/run";
    public const string AutomationTaskDelete = "automation/task/delete";

    /// <summary>Replaces or clears a task's thread binding without rewriting other fields.</summary>
    public const string AutomationTaskUpdateBinding = "automation/task/updateBinding";

    /// <summary>Returns the catalog of built-in and user local task templates (gallery + create-dialog preset source).</summary>
    public const string AutomationTemplateList = "automation/template/list";

    /// <summary>Creates or updates a user-authored automation template (upsert by id).</summary>
    public const string AutomationTemplateSave = "automation/template/save";

    /// <summary>Deletes a user-authored automation template. Built-in ids are rejected.</summary>
    public const string AutomationTemplateDelete = "automation/template/delete";

    // Server → Client notification (automations)
    public const string AutomationTaskUpdated = "automation/task/updated";
}
