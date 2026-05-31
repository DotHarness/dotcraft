using System.Globalization;
using System.Text.Json;

namespace DotCraft.Text;

/// <summary>
/// English fallback text emitted by the C# runtime.
/// </summary>
public static class FallbackText
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        JsonSerializer.Deserialize<Dictionary<string, string>>(EnglishJson)
        ?? new Dictionary<string, string>();

    public static string Format(string key, params object?[] args)
    {
        var template = Values.TryGetValue(key, out var value) ? value : key;
        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    // Command descriptions
    public static string CmdExit => Format("cmd.exit");
    public static string CmdHelp => Format("cmd.help");
    public static string CmdClear => Format("cmd.clear");
    public static string CmdNew => Format("cmd.new");
    public static string CmdLoad => Format("cmd.load");
    public static string CmdDelete => Format("cmd.delete");
    public static string CmdInit => Format("cmd.init");
    public static string CmdDebug => Format("cmd.debug");
    public static string CmdSkills => Format("cmd.skills");
    public static string CmdMcp => Format("cmd.mcp");
    public static string CmdSessions => Format("cmd.sessions");
    public static string CmdMemory => Format("cmd.memory");
    public static string CmdHeartbeat => Format("cmd.heartbeat");
    public static string CmdCronList => Format("cmd.cron_list");
    public static string CmdCronRemove => Format("cmd.cron_remove");
    public static string CmdCronToggle => Format("cmd.cron_toggle");
    public static string CmdCommands => Format("cmd.commands");
    public static string CmdAgent => Format("cmd.agent");
    public static string CmdPlan => Format("cmd.plan");
    public static string CmdModel => Format("cmd.model");

    // Welcome screen
    public static string CurrentSession => Format("welcome.current_session");
    public static string QuickCommands => Format("welcome.quick_commands");
    public static string WelcomeModel => Format("welcome.model");

    // Session management
    public static string SessionLoaded => Format("session.loaded");
    public static string SessionLoadFailed => Format("session.load_failed");
    public static string SessionCreated => Format("session.created");
    public static string SessionCreateFailed => Format("session.create_failed");
    public static string SessionDeleted => Format("session.deleted");
    public static string SessionNotFound => Format("session.not_found");
    public static string SessionDeleteFailed => Format("session.delete_failed");

    // Init command
    public static string InitWorkspace => Format("init.workspace");
    public static string CurrentWorkspace => Format("init.current_workspace");
    public static string WorkspaceExists => Format("init.workspace_exists");
    public static string InitCancelled => Format("init.cancelled");
    public static string InitComplete => Format("init.complete");
    public static string InitFailed => Format("init.failed");
    public static string InitInitializing => Format("init.initializing");
    public static string InitFailedShort => Format("init.failed_short");
    public static string InitStatus => Format("init.status");
    public static string InitPath => Format("init.path");
    public static string InitWorkspaceInitialized => Format("init.workspace_initialized");
    public static string InitPressAnyKey => Format("init.press_any_key");
    public static string InitTrustFolderTitle => Format("init.trust_folder_title");
    public static string InitTrustFolderWorkspacePath => Format("init.trust_folder_workspace_path");
    public static string InitTrustFolderDescription => Format("init.trust_folder_description");
    public static string InitTrustFolderQuestion => Format("init.trust_folder_question");
    public static string InitTrustFolderCancelled => Format("init.trust_folder_cancelled");
    public static string InitAskYes => Format("init.ask_yes");
    public static string InitAskNo => Format("init.ask_no");

    // Memory command
    public static string LongTermMemory => Format("memory.long_term");
    public static string MemoryNotExists => Format("memory.not_exists");
    public static string ExpectedPath => Format("memory.expected_path");
    public static string MemoryEmpty => Format("memory.empty");

    // Debug command
    public static string DebugEnabled => Format("debug.enabled");
    public static string DebugDisabled => Format("debug.disabled");

    // Heartbeat command
    public static string HeartbeatUnavailable => Format("heartbeat.unavailable");
    public static string TriggeringHeartbeat => Format("heartbeat.triggering");
    public static string HeartbeatResult => Format("heartbeat.result");
    public static string HeartbeatNoResponse => Format("heartbeat.no_response");
    public static string HeartbeatUsage => Format("heartbeat.usage");

    // Cron command
    public static string CronUnavailable => Format("cron.unavailable");
    public static string NoCronJobs => Format("cron.no_jobs");
    public static string CronColId => Format("cron.col_id");
    public static string CronColName => Format("cron.col_name");
    public static string CronColSchedule => Format("cron.col_schedule");
    public static string CronColStatus => Format("cron.col_status");
    public static string CronColNextRun => Format("cron.col_next_run");
    public static string CronExecuteOnce => Format("cron.execute_once");
    public static string CronExecuteOnceSuffix => Format("cron.execute_once_suffix");
    public static string CronEvery => Format("cron.every");
    public static string CronEnabled => Format("cron.enabled");
    public static string CronDisabled => Format("cron.disabled");
    public static string CronRemoveUsage => Format("cron.remove_usage");
    public static string CronJobDeleted => Format("cron.job_deleted");
    public static string CronJobDeletedSuffix => Format("cron.job_deleted_suffix");
    public static string CronJobNotFound => Format("cron.job_not_found");
    public static string CronToggleUsage => Format("cron.toggle_usage");
    public static string CronJobEnabled => Format("cron.job_enabled");
    public static string CronJobDisabled => Format("cron.job_disabled");
    public static string CronUsage => Format("cron.usage");

    // Context compaction
    public static string ContextLimitReached => Format("context.limit_reached");
    public static string ContextCompacted => Format("context.compacted");
    public static string ContextCompactSkipped => Format("context.compact_skipped");

    // Memory consolidation
    public static string MemoryConsolidating => Format("memory.consolidating");
    public static string MemoryConsolidated => Format("memory.consolidated");
    public static string MemoryConsolidationFailed => Format("memory.consolidation_failed");

    // Agent interrupt
    public static string AgentInterrupted => Format("agent.interrupted");

    // Goodbye
    public static string Goodbye => Format("common.goodbye");

    // Help panel
    public static string Commands => Format("help.commands");
    public static string UsageTips => Format("help.usage_tips");
    public static string TipDirectInput => Format("help.tip_direct_input");
    public static string TipArrowKeys => Format("help.tip_arrow_keys");
    public static string TipAutoSave => Format("help.tip_auto_save");
    public static string TipTabComplete => Format("help.tip_tab_complete");
    public static string TipShiftTabMode => Format("help.tip_shift_tab_mode");

    // Skills panel
    public static string AvailableSkills => Format("skills.available");
    public static string Skill => Format("skills.skill");
    public static string Status => Format("skills.status");
    public static string Source => Format("skills.source");
    public static string Description => Format("skills.description");
    public static string Available => Format("skills.available_status");
    public static string Unavailable => Format("skills.unavailable_status");
    public static string NoSkills => Format("skills.no_skills");
    public static string SkillsPath => Format("skills.path");
    public static string NoDescription => Format("skills.no_description");

    // Sessions panel
    public static string SavedSessions => Format("sessions.saved");
    public static string Session => Format("sessions.session");
    public static string CreatedAt => Format("sessions.created_at");
    public static string UpdatedAt => Format("sessions.updated_at");
    public static string Summary => Format("sessions.summary");
    public static string NoSessions => Format("sessions.no_sessions");

    // MCP panel
    public static string McpServices => Format("mcp.services");
    public static string Server => Format("mcp.server");
    public static string Tools => Format("mcp.tools");
    public static string ToolNames => Format("mcp.tool_names");
    public static string NoMcpServers => Format("mcp.no_servers");
    public static string McpConfigTip => Format("mcp.config_tip");
    public static string Unknown => Format("mcp.unknown");

    // Model command
    public static string ModelLoading => Format("model.loading");
    public static string ModelFetchFailed => Format("model.fetch_failed");
    public static string ModelManualPrompt => Format("model.manual_prompt");
    public static string ModelSelectTitle => Format("model.select_title");
    public static string ModelUpdatedDefault => Format("model.updated_default");
    public static string ModelUpdatedTo(string model) => Format("model.updated_to", model);
    public static string ModelFeatureUnavailable => Format("model.feature_unavailable");
    public static string ModelNoOptions => Format("model.no_options");

    // Commands
    public static string UnknownCommand => Format("command.unknown");
    public static string DidYouMean => Format("command.did_you_mean");
    public static string ViewAllCommands => Format("command.view_all");
    public static string CommandPermissionDenied => Format("command.permission_denied");
    public static string CommandServiceUnavailable => Format("command.service_unavailable");
    public static string CommandNewCleared => Format("command.new.cleared");
    public static string CommandStopDescription => Format("command.stop.description");
    public static string CommandStopNoActiveRun => Format("command.stop.no_active_run");
    public static string CommandStopStopped => Format("command.stop.stopped");
    public static string CommandHelpTitle => Format("command.help.title");
    public static string CommandHelpCustomSection => Format("command.help.custom_section");
    public static string CommandHelpNoCustom => Format("command.help.no_custom");
    public static string CommandHelpAdminSuffix => Format("command.help.admin_suffix");
    public static string CommandCronListTitle => Format("command.cron.list_title");

    // Session prompt
    public static string NoSessionsAvailable => Format("session_prompt.no_available");
    public static string NoSessionsToDelete => Format("session_prompt.no_deletable");
    public static string SelectSessionToLoadTitle => Format("session_prompt.select_load");
    public static string SelectSessionToDeleteTitle => Format("session_prompt.select_delete");
    public static string SessionSelected => Format("session_prompt.selected");
    public static string Cancelled => Format("session_prompt.cancelled");
    public static string Cancel => Format("session_prompt.cancel");
    public static string ConfirmDeleteCurrentWarning(string sessionId) => Format("session_prompt.confirm_delete_current", sessionId);
    public static string ConfirmDeleteCurrentSuffix => Format("session_prompt.confirm_delete_current_suffix");
    public static string ConfirmDeleteOther(string sessionId) => Format("session_prompt.confirm_delete_other", sessionId);
    public static string ConfirmDeleteQuestion => Format("session_prompt.confirm_delete_question");

    // Time formatting
    public static string TimeUnknown => Format("time.unknown");
    public static string TimeJustNow => Format("time.just_now");
    public static string TimeMinutesAgo(int n) => Format("time.minutes_ago", n);
    public static string TimeHoursAgo(int n) => Format("time.hours_ago", n);
    public static string TimeDaysAgo(int n) => Format("time.days_ago", n);

    // Setup mode
    public static string SetupMode => Format("setup.mode");
    public static string SetupOpenBrowser(string url) => Format("setup.open_browser", url);
    public static string SetupAfterSave => Format("setup.after_save");

    // Auth commands
    public static string AuthUsage => Format("auth.usage");
    public static string AuthOpenAiUnsupported => Format("auth.openai.unsupported");
    public static string AuthOpenAiLoginStarting => Format("auth.openai.login.starting");
    public static string AuthOpenAiLoginUrl(string url) => Format("auth.openai.login.url", url);
    public static string AuthOpenAiLoginWaiting(int port) => Format("auth.openai.login.waiting", port);
    public static string AuthOpenAiLoginSuccess(string account, string plan) => Format("auth.openai.login.success", account, plan);
    public static string AuthOpenAiLoginCancelled => Format("auth.openai.login.cancelled");
    public static string AuthOpenAiLoginFailed(string reason) => Format("auth.openai.login.failed", reason);
    public static string AuthOpenAiLoginBound(string providerId) => Format("auth.openai.login.bound", providerId);
    public static string AuthOpenAiLogoutSuccess => Format("auth.openai.logout.success");
    public static string AuthOpenAiLogoutUnbound(string providerId) => Format("auth.openai.logout.unbound", providerId);
    public static string AuthOpenAiStatusSignedIn(string account, string plan, string lastRefresh)
        => Format("auth.openai.status.signed_in", account, plan, lastRefresh);
    public static string AuthOpenAiStatusSignedOut => Format("auth.openai.status.signed_out");
    public static string AuthOpenAiUsageHeader => Format("auth.openai.usage.header");
    public static string AuthOpenAiUsageWindowFiveHour => Format("auth.openai.usage.window.fiveHour");
    public static string AuthOpenAiUsageWindowWeekly => Format("auth.openai.usage.window.weekly");
    public static string AuthOpenAiUsageUnavailable(string reason) => Format("auth.openai.usage.unavailable", reason);
    public static string AuthOpenAiUsageCreditsBalance(string balance) => Format("auth.openai.usage.credits.balance", balance);
    public static string AuthOpenAiUsageCreditsUnlimited => Format("auth.openai.usage.credits.unlimited");
    public static string AuthOpenAiUsageLimitReached(string kind) => Format("auth.openai.usage.limitReached", kind);

    private const string EnglishJson = """
{
  "cmd.exit": "Exit the program",
  "cmd.help": "Show help information",
  "cmd.clear": "Clear screen",
  "cmd.new": "Create a new session",
  "cmd.load": "Select and switch to another session",
  "cmd.delete": "Select and delete a session",
  "cmd.init": "Initialize workspace",
  "cmd.debug": "Toggle debug mode",
  "cmd.skills": "Show available skills",
  "cmd.mcp": "Show MCP service status",
  "cmd.sessions": "Show saved sessions",
  "cmd.memory": "Show long-term memory",
  "cmd.heartbeat": "Trigger heartbeat check immediately",
  "cmd.cron_list": "List cron jobs",
  "cmd.cron_remove": "Remove a cron job",
  "cmd.cron_toggle": "Enable/disable a cron job",
  "cmd.commands": "Show custom commands",
  "cmd.agent": "Switch to Agent mode",
  "cmd.plan": "Switch to Plan mode",
  "cmd.model": "Select or set model",
  "welcome.current_session": "Current session",
  "welcome.quick_commands": "Quick commands",
  "welcome.model": "Model",
  "session.loaded": "Session loaded",
  "session.load_failed": "Failed to load session",
  "session.created": "New session created",
  "session.create_failed": "Failed to create new session",
  "session.deleted": "Session deleted",
  "session.not_found": "Session not found",
  "session.delete_failed": "Failed to delete session",
  "init.workspace": "Re-initialize workspace",
  "init.current_workspace": "Current workspace",
  "init.workspace_exists": "Workspace already exists. Re-initialize? This will overwrite existing configuration",
  "init.cancelled": "Initialization cancelled",
  "init.complete": "Initialization complete!",
  "init.failed": "Initialization failed, error code",
  "init.initializing": "Initializing DotCraft workspace...",
  "init.failed_short": "Initialization failed",
  "init.status": "Status",
  "init.path": "Path",
  "init.workspace_initialized": "Workspace initialized.",
  "init.press_any_key": "Press any key to exit...",
  "init.trust_folder_title": "Trust Folder Confirmation",
  "init.trust_folder_workspace_path": "Current workspace path:",
  "init.trust_folder_description": "DotCraft will create a workspace (.craft folder) in this directory to store sessions, memory, and configuration.",
  "init.trust_folder_question": "Do you trust this folder?",
  "init.trust_folder_cancelled": "Cancelled. Please switch to a trusted directory and try again.",
  "init.ask_yes": "Yes",
  "init.ask_no": "No",
  "memory.long_term": "Long-term Memory (MEMORY.md)",
  "memory.not_exists": "Long-term memory file does not exist",
  "memory.expected_path": "Expected path",
  "memory.empty": "Long-term memory is empty",
  "debug.enabled": "Debug mode enabled",
  "debug.disabled": "Debug mode disabled",
  "heartbeat.unavailable": "Heartbeat service unavailable.",
  "heartbeat.triggering": "Triggering heartbeat...",
  "heartbeat.result": "Heartbeat result",
  "heartbeat.no_response": "No heartbeat response (HEARTBEAT.md may be empty or missing).",
  "heartbeat.usage": "Usage: /heartbeat trigger",
  "cron.unavailable": "Cron service unavailable.",
  "cron.no_jobs": "No cron jobs.",
  "cron.col_id": "ID",
  "cron.col_name": "Name",
  "cron.col_schedule": "Schedule",
  "cron.col_status": "Status",
  "cron.col_next_run": "Next Run",
  "cron.execute_once": "At",
  "cron.execute_once_suffix": "execute once",
  "cron.every": "Every",
  "cron.enabled": "Enabled",
  "cron.disabled": "Disabled",
  "cron.remove_usage": "Usage: /cron remove <jobId>",
  "cron.job_deleted": "Job",
  "cron.job_deleted_suffix": "deleted.",
  "cron.job_not_found": "Job not found",
  "cron.toggle_usage": "Usage",
  "cron.job_enabled": "enabled.",
  "cron.job_disabled": "disabled.",
  "cron.usage": "Usage: /cron list | /cron remove <id> | /cron enable <id> | /cron disable <id>",
  "context.limit_reached": "Context token limit reached, compacting conversation...",
  "context.compacted": "Context compacted successfully.",
  "context.compact_skipped": "Context compaction skipped (insufficient history).",
  "memory.consolidating": "Consolidating memory...",
  "memory.consolidated": "Memory consolidation complete.",
  "memory.consolidation_failed": "Memory consolidation failed.",
  "agent.interrupted": "Agent interrupted",
  "common.goodbye": "Goodbye!",
  "help.commands": "Commands",
  "help.usage_tips": "Usage Tips",
  "help.tip_direct_input": "Directly input questions to chat with DotCraft",
  "help.tip_arrow_keys": "Use arrow keys ↑↓ to browse history",
  "help.tip_auto_save": "Sessions are saved automatically",
  "help.tip_tab_complete": "Press Tab to auto-complete commands",
  "help.tip_shift_tab_mode": "Press Shift+Tab to switch Plan/Agent mode",
  "skills.available": "Available skills",
  "skills.skill": "Skill",
  "skills.status": "Status",
  "skills.source": "Source",
  "skills.description": "Description",
  "skills.available_status": "Available",
  "skills.unavailable_status": "Unavailable",
  "skills.no_skills": "No available skills.",
  "skills.path": "Skills path",
  "skills.no_description": "No description",
  "sessions.saved": "Saved sessions",
  "sessions.session": "Session",
  "sessions.created_at": "Created",
  "sessions.updated_at": "Updated",
  "sessions.summary": "Summary",
  "sessions.no_sessions": "No sessions found.",
  "mcp.services": "MCP Services",
  "mcp.server": "Server",
  "mcp.tools": "Tools",
  "mcp.tool_names": "Tool Names",
  "mcp.no_servers": "No MCP servers connected.",
  "mcp.config_tip": "Configure MCP servers in \"McpServers\" section of config.json.",
  "mcp.unknown": "Unknown",
  "model.loading": "Loading models...",
  "model.fetch_failed": "Failed to load model catalog",
  "model.manual_prompt": "Enter model name (empty to cancel)",
  "model.select_title": "Select model",
  "model.updated_default": "Model reset to Default",
  "model.updated_to": "Model updated to {0}",
  "model.feature_unavailable": "Model catalog is not available on this server.",
  "model.no_options": "No models returned by the server.",
  "command.unknown": "Unknown command",
  "command.did_you_mean": "Did you mean",
  "command.view_all": "Type /help to see all available commands.",
  "command.permission_denied": "Permission denied. This command is available to administrators only.",
  "command.service_unavailable": "The service required by this command is unavailable.",
  "command.new.cleared": "Session cleared. Starting a new conversation.",
  "command.stop.description": "Stop the current agent run",
  "command.stop.no_active_run": "There is no active agent run.",
  "command.stop.stopped": "Agent stopped.",
  "command.help.title": "Available commands:",
  "command.help.custom_section": "Custom commands:",
  "command.help.no_custom": "(no custom commands)",
  "command.help.admin_suffix": "(admin)",
  "command.cron.list_title": "Cron jobs ({0}):",
  "session_prompt.no_available": "No sessions available.",
  "session_prompt.no_deletable": "No sessions to delete.",
  "session_prompt.select_load": "Select a session to load:",
  "session_prompt.select_delete": "Select a session to delete:",
  "session_prompt.selected": "Session selected",
  "session_prompt.cancelled": "Cancelled.",
  "session_prompt.cancel": "Cancel",
  "session_prompt.confirm_delete_current": "⚠️  You are about to delete the [cyan]current[/] session '[cyan]{0}[/]'.",
  "session_prompt.confirm_delete_current_suffix": "A new session will be created after deletion.",
  "session_prompt.confirm_delete_other": "Are you sure you want to delete session [cyan]{0}[/]?",
  "session_prompt.confirm_delete_question": "Delete this session?",
  "time.unknown": "unknown",
  "time.just_now": "just now",
  "time.minutes_ago": "{0} min ago",
  "time.hours_ago": "{0}h ago",
  "time.days_ago": "{0}d ago",
  "approval.file.operation": "Operation:",
  "approval.file.path": "Path:",
  "approval.file.title": "⚠️  Approval Required: File operation outside workspace",
  "approval.file.approve_question": "Approve this operation?",
  "approval.shell.command": "Command:",
  "approval.shell.working_dir": "Working directory:",
  "approval.shell.title": "⚠️  Approval Required: Shell command outside workspace",
  "approval.shell.approve_question": "Approve this command?",
  "approval.option.once": "✅  Approve (this time only)",
  "approval.option.session": "✅  Approve (for this session)",
  "approval.option.always": "✅  Approve (permanently)",
  "approval.option.reject": "❌  Reject",
  "approval.result.once": "✓ Approved (this time only)",
  "approval.result.session": "✓ Approved (for this session)",
  "approval.result.always": "✓ Approved and saved permanently",
  "approval.result.reject": "✗ Rejected",
  "hub.notification.turn_completed.title": "DotCraft task completed",
  "hub.notification.turn_completed.body": "\"{0}\" finished.",
  "hub.notification.turn_failed.title": "DotCraft task failed",
  "hub.notification.turn_failed.body": "\"{0}\" failed.",
  "hub.notification.thread.default": "Current chat",
  "setup.mode": "DotCraft is running in setup mode.",
  "setup.open_browser": "Open {0} in your browser to finish global and workspace configuration.",
  "setup.after_save": "After saving, press Ctrl+C to stop this process, then run `dotcraft` again.",
  "auth.usage": "Usage: dotcraft auth openai <login|logout|status> [--provider-id <id>] [--no-browser]",
  "auth.openai.unsupported": "Only 'openai' is supported. Run `dotcraft auth openai login`.",
  "auth.openai.login.starting": "Starting Sign in with ChatGPT...",
  "auth.openai.login.url": "If your browser does not open automatically, visit:\n  {0}",
  "auth.openai.login.waiting": "Waiting for browser authorization on http://localhost:{0}/auth/callback ...",
  "auth.openai.login.success": "Signed in as {0} (plan: {1}).",
  "auth.openai.login.cancelled": "Sign-in was cancelled.",
  "auth.openai.login.failed": "Sign-in failed: {0}",
  "auth.openai.login.bound": "Provider '{0}' is now using ChatGPT subscription auth.",
  "auth.openai.logout.success": "Signed out of ChatGPT.",
  "auth.openai.logout.unbound": "Provider '{0}' switched back to API key auth.",
  "auth.openai.status.signed_in": "Signed in as {0} (plan: {1}). Last refreshed {2}.",
  "auth.openai.status.signed_out": "Not signed in. Run `dotcraft auth openai login` to start.",
  "auth.openai.usage.header": "Usage:",
  "auth.openai.usage.window.fiveHour": "5h window ",
  "auth.openai.usage.window.weekly": "Weekly    ",
  "auth.openai.usage.unavailable": "Couldn't fetch usage: {0}",
  "auth.openai.usage.credits.balance": "{0}",
  "auth.openai.usage.credits.unlimited": "unlimited",
  "auth.openai.usage.limitReached": "Limit reached ({0}); requests may fail until reset."
}
""";
}
