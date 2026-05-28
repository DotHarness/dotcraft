namespace DotCraft.Localization;

/// <summary>
/// Type-safe access layer for localized strings.
/// Each property / method maps to a key in the JSON language pack,
/// providing compile-time safety while keeping translations in external files.
/// </summary>
public static class Strings
{
    // ── Command descriptions ─────────────────────────────────────────
    public static string CmdExit => LanguageService.Current.T("cmd.exit");
    public static string CmdHelp => LanguageService.Current.T("cmd.help");
    public static string CmdClear => LanguageService.Current.T("cmd.clear");
    public static string CmdNew => LanguageService.Current.T("cmd.new");
    public static string CmdLoad => LanguageService.Current.T("cmd.load");
    public static string CmdDelete => LanguageService.Current.T("cmd.delete");
    public static string CmdInit => LanguageService.Current.T("cmd.init");
    public static string CmdDebug => LanguageService.Current.T("cmd.debug");
    public static string CmdSkills => LanguageService.Current.T("cmd.skills");
    public static string CmdMcp => LanguageService.Current.T("cmd.mcp");
    public static string CmdSessions => LanguageService.Current.T("cmd.sessions");
    public static string CmdMemory => LanguageService.Current.T("cmd.memory");
    public static string CmdHeartbeat => LanguageService.Current.T("cmd.heartbeat");
    public static string CmdCronList => LanguageService.Current.T("cmd.cron_list");
    public static string CmdCronRemove => LanguageService.Current.T("cmd.cron_remove");
    public static string CmdCronToggle => LanguageService.Current.T("cmd.cron_toggle");
    public static string CmdLang => LanguageService.Current.T("cmd.lang");
    public static string CmdCommands => LanguageService.Current.T("cmd.commands");
    public static string CmdAgent => LanguageService.Current.T("cmd.agent");
    public static string CmdPlan => LanguageService.Current.T("cmd.plan");
    public static string CmdModel => LanguageService.Current.T("cmd.model");

    // ── Welcome screen ───────────────────────────────────────────────
    public static string CurrentSession => LanguageService.Current.T("welcome.current_session");
    public static string QuickCommands => LanguageService.Current.T("welcome.quick_commands");
    public static string WelcomeModel => LanguageService.Current.T("welcome.model");

    // ── Session management ───────────────────────────────────────────
    public static string SessionLoaded => LanguageService.Current.T("session.loaded");
    public static string SessionLoadFailed => LanguageService.Current.T("session.load_failed");
    public static string SessionCreated => LanguageService.Current.T("session.created");
    public static string SessionCreateFailed => LanguageService.Current.T("session.create_failed");
    public static string SessionDeleted => LanguageService.Current.T("session.deleted");
    public static string SessionNotFound => LanguageService.Current.T("session.not_found");
    public static string SessionDeleteFailed => LanguageService.Current.T("session.delete_failed");

    // ── Init command ─────────────────────────────────────────────────
    public static string InitWorkspace => LanguageService.Current.T("init.workspace");
    public static string CurrentWorkspace => LanguageService.Current.T("init.current_workspace");
    public static string WorkspaceExists => LanguageService.Current.T("init.workspace_exists");
    public static string InitCancelled => LanguageService.Current.T("init.cancelled");
    public static string InitComplete => LanguageService.Current.T("init.complete");
    public static string InitFailed => LanguageService.Current.T("init.failed");
    public static string InitInitializing => LanguageService.Current.T("init.initializing");
    public static string InitFailedShort => LanguageService.Current.T("init.failed_short");
    public static string InitStatus => LanguageService.Current.T("init.status");
    public static string InitPath => LanguageService.Current.T("init.path");
    public static string InitWorkspaceInitialized => LanguageService.Current.T("init.workspace_initialized");
    public static string InitPressAnyKey => LanguageService.Current.T("init.press_any_key");
    public static string InitTrustFolderTitle => LanguageService.Current.T("init.trust_folder_title");
    public static string InitTrustFolderWorkspacePath => LanguageService.Current.T("init.trust_folder_workspace_path");
    public static string InitTrustFolderDescription => LanguageService.Current.T("init.trust_folder_description");
    public static string InitTrustFolderQuestion => LanguageService.Current.T("init.trust_folder_question");
    public static string InitTrustFolderCancelled => LanguageService.Current.T("init.trust_folder_cancelled");
    public static string InitAskYes => LanguageService.Current.T("init.ask_yes");
    public static string InitAskNo => LanguageService.Current.T("init.ask_no");

    // ── Memory command ───────────────────────────────────────────────
    public static string LongTermMemory => LanguageService.Current.T("memory.long_term");
    public static string MemoryNotExists => LanguageService.Current.T("memory.not_exists");
    public static string ExpectedPath => LanguageService.Current.T("memory.expected_path");
    public static string MemoryEmpty => LanguageService.Current.T("memory.empty");

    // ── Debug command ────────────────────────────────────────────────
    public static string DebugEnabled => LanguageService.Current.T("debug.enabled");
    public static string DebugDisabled => LanguageService.Current.T("debug.disabled");

    // ── Heartbeat command ────────────────────────────────────────────
    public static string HeartbeatUnavailable => LanguageService.Current.T("heartbeat.unavailable");
    public static string TriggeringHeartbeat => LanguageService.Current.T("heartbeat.triggering");
    public static string HeartbeatResult => LanguageService.Current.T("heartbeat.result");
    public static string HeartbeatNoResponse => LanguageService.Current.T("heartbeat.no_response");
    public static string HeartbeatUsage => LanguageService.Current.T("heartbeat.usage");

    // ── Cron command ─────────────────────────────────────────────────
    public static string CronUnavailable => LanguageService.Current.T("cron.unavailable");
    public static string NoCronJobs => LanguageService.Current.T("cron.no_jobs");
    public static string CronColId => LanguageService.Current.T("cron.col_id");
    public static string CronColName => LanguageService.Current.T("cron.col_name");
    public static string CronColSchedule => LanguageService.Current.T("cron.col_schedule");
    public static string CronColStatus => LanguageService.Current.T("cron.col_status");
    public static string CronColNextRun => LanguageService.Current.T("cron.col_next_run");
    public static string CronExecuteOnce => LanguageService.Current.T("cron.execute_once");
    public static string CronExecuteOnceSuffix => LanguageService.Current.T("cron.execute_once_suffix");
    public static string CronEvery => LanguageService.Current.T("cron.every");
    public static string CronEnabled => LanguageService.Current.T("cron.enabled");
    public static string CronDisabled => LanguageService.Current.T("cron.disabled");
    public static string CronRemoveUsage => LanguageService.Current.T("cron.remove_usage");
    public static string CronJobDeleted => LanguageService.Current.T("cron.job_deleted");
    public static string CronJobDeletedSuffix => LanguageService.Current.T("cron.job_deleted_suffix");
    public static string CronJobNotFound => LanguageService.Current.T("cron.job_not_found");
    public static string CronToggleUsage => LanguageService.Current.T("cron.toggle_usage");
    public static string CronJobEnabled => LanguageService.Current.T("cron.job_enabled");
    public static string CronJobDisabled => LanguageService.Current.T("cron.job_disabled");
    public static string CronUsage => LanguageService.Current.T("cron.usage");

    // ── Context compaction ───────────────────────────────────────────
    public static string ContextLimitReached => LanguageService.Current.T("context.limit_reached");
    public static string ContextCompacted => LanguageService.Current.T("context.compacted");
    public static string ContextCompactSkipped => LanguageService.Current.T("context.compact_skipped");

    // ── Memory consolidation ─────────────────────────────────────────
    public static string MemoryConsolidating => LanguageService.Current.T("memory.consolidating");
    public static string MemoryConsolidated => LanguageService.Current.T("memory.consolidated");
    public static string MemoryConsolidationFailed => LanguageService.Current.T("memory.consolidation_failed");

    // ── Agent interrupt ──────────────────────────────────────────────
    public static string AgentInterrupted => LanguageService.Current.T("agent.interrupted");

    // ── Goodbye ──────────────────────────────────────────────────────
    public static string Goodbye => LanguageService.Current.T("common.goodbye");

    // ── Help panel ───────────────────────────────────────────────────
    public static string Commands => LanguageService.Current.T("help.commands");
    public static string UsageTips => LanguageService.Current.T("help.usage_tips");
    public static string TipDirectInput => LanguageService.Current.T("help.tip_direct_input");
    public static string TipArrowKeys => LanguageService.Current.T("help.tip_arrow_keys");
    public static string TipAutoSave => LanguageService.Current.T("help.tip_auto_save");
    public static string TipTabComplete => LanguageService.Current.T("help.tip_tab_complete");
    public static string TipShiftTabMode => LanguageService.Current.T("help.tip_shift_tab_mode");

    // ── Skills panel ─────────────────────────────────────────────────
    public static string AvailableSkills => LanguageService.Current.T("skills.available");
    public static string Skill => LanguageService.Current.T("skills.skill");
    public static string Status => LanguageService.Current.T("skills.status");
    public static string Source => LanguageService.Current.T("skills.source");
    public static string Description => LanguageService.Current.T("skills.description");
    public static string Available => LanguageService.Current.T("skills.available_status");
    public static string Unavailable => LanguageService.Current.T("skills.unavailable_status");
    public static string NoSkills => LanguageService.Current.T("skills.no_skills");
    public static string SkillsPath => LanguageService.Current.T("skills.path");
    public static string NoDescription => LanguageService.Current.T("skills.no_description");

    // ── Sessions panel ───────────────────────────────────────────────
    public static string SavedSessions => LanguageService.Current.T("sessions.saved");
    public static string Session => LanguageService.Current.T("sessions.session");
    public static string CreatedAt => LanguageService.Current.T("sessions.created_at");
    public static string UpdatedAt => LanguageService.Current.T("sessions.updated_at");
    public static string Summary => LanguageService.Current.T("sessions.summary");
    public static string NoSessions => LanguageService.Current.T("sessions.no_sessions");

    // ── MCP panel ────────────────────────────────────────────────────
    public static string McpServices => LanguageService.Current.T("mcp.services");
    public static string Server => LanguageService.Current.T("mcp.server");
    public static string Tools => LanguageService.Current.T("mcp.tools");
    public static string ToolNames => LanguageService.Current.T("mcp.tool_names");
    public static string NoMcpServers => LanguageService.Current.T("mcp.no_servers");
    public static string McpConfigTip => LanguageService.Current.T("mcp.config_tip");
    public static string Unknown => LanguageService.Current.T("mcp.unknown");

    // ── Language command ─────────────────────────────────────────────
    public static string LanguageSwitched => LanguageService.Current.T("lang.switched");
    public static string LanguageChinese => LanguageService.Current.T("lang.chinese");
    public static string LanguageEnglish => LanguageService.Current.T("lang.english");

    // ── Model command ────────────────────────────────────────────────
    public static string ModelLoading => LanguageService.Current.T("model.loading");
    public static string ModelFetchFailed => LanguageService.Current.T("model.fetch_failed");
    public static string ModelManualPrompt => LanguageService.Current.T("model.manual_prompt");
    public static string ModelSelectTitle => LanguageService.Current.T("model.select_title");
    public static string ModelUpdatedDefault => LanguageService.Current.T("model.updated_default");
    public static string ModelUpdatedTo(string model) => LanguageService.Current.T("model.updated_to", model);
    public static string ModelFeatureUnavailable => LanguageService.Current.T("model.feature_unavailable");
    public static string ModelNoOptions => LanguageService.Current.T("model.no_options");

    // ── Unknown command ──────────────────────────────────────────────
    public static string UnknownCommand => LanguageService.Current.T("command.unknown");
    public static string DidYouMean => LanguageService.Current.T("command.did_you_mean");
    public static string ViewAllCommands => LanguageService.Current.T("command.view_all");
    public static string CommandPermissionDenied => LanguageService.Current.T("command.permission_denied");
    public static string CommandServiceUnavailable => LanguageService.Current.T("command.service_unavailable");
    public static string CommandNewCleared => LanguageService.Current.T("command.new.cleared");
    public static string CommandStopDescription => LanguageService.Current.T("command.stop.description");
    public static string CommandStopNoActiveRun => LanguageService.Current.T("command.stop.no_active_run");
    public static string CommandStopStopped => LanguageService.Current.T("command.stop.stopped");
    public static string CommandHelpTitle => LanguageService.Current.T("command.help.title");
    public static string CommandHelpCustomSection => LanguageService.Current.T("command.help.custom_section");
    public static string CommandHelpNoCustom => LanguageService.Current.T("command.help.no_custom");
    public static string CommandHelpAdminSuffix => LanguageService.Current.T("command.help.admin_suffix");
    public static string CommandCronListTitle => LanguageService.Current.T("command.cron.list_title");

    // ── Session prompt ───────────────────────────────────────────────
    public static string NoSessionsAvailable => LanguageService.Current.T("session_prompt.no_available");
    public static string NoSessionsToDelete => LanguageService.Current.T("session_prompt.no_deletable");
    public static string SelectSessionToLoadTitle => LanguageService.Current.T("session_prompt.select_load");
    public static string SelectSessionToDeleteTitle => LanguageService.Current.T("session_prompt.select_delete");
    public static string SessionSelected => LanguageService.Current.T("session_prompt.selected");
    public static string Cancelled => LanguageService.Current.T("session_prompt.cancelled");
    public static string Cancel => LanguageService.Current.T("session_prompt.cancel");
    public static string ConfirmDeleteCurrentWarning(string sessionId)
        => LanguageService.Current.T("session_prompt.confirm_delete_current", sessionId);
    public static string ConfirmDeleteCurrentSuffix => LanguageService.Current.T("session_prompt.confirm_delete_current_suffix");
    public static string ConfirmDeleteOther(string sessionId)
        => LanguageService.Current.T("session_prompt.confirm_delete_other", sessionId);
    public static string ConfirmDeleteQuestion => LanguageService.Current.T("session_prompt.confirm_delete_question");

    // ── Time formatting ──────────────────────────────────────────────
    public static string TimeUnknown => LanguageService.Current.T("time.unknown");
    public static string TimeJustNow => LanguageService.Current.T("time.just_now");
    public static string TimeMinutesAgo(int n) => LanguageService.Current.T("time.minutes_ago", n);
    public static string TimeHoursAgo(int n) => LanguageService.Current.T("time.hours_ago", n);
    public static string TimeDaysAgo(int n) => LanguageService.Current.T("time.days_ago", n);

    // ── Setup mode ──────────────────────────────────────────────────
    public static string SetupMode => LanguageService.Current.T("setup.mode");
    public static string SetupOpenBrowser(string url) => LanguageService.Current.T("setup.open_browser", url);
    public static string SetupAfterSave => LanguageService.Current.T("setup.after_save");

    // ── Auth commands ───────────────────────────────────────────────
    public static string AuthUsage => LanguageService.Current.T("auth.usage");
    public static string AuthOpenAiUnsupported => LanguageService.Current.T("auth.openai.unsupported");
    public static string AuthOpenAiLoginStarting => LanguageService.Current.T("auth.openai.login.starting");
    public static string AuthOpenAiLoginUrl(string url) => LanguageService.Current.T("auth.openai.login.url", url);
    public static string AuthOpenAiLoginWaiting(int port) => LanguageService.Current.T("auth.openai.login.waiting", port);
    public static string AuthOpenAiLoginSuccess(string account, string plan) => LanguageService.Current.T("auth.openai.login.success", account, plan);
    public static string AuthOpenAiLoginCancelled => LanguageService.Current.T("auth.openai.login.cancelled");
    public static string AuthOpenAiLoginFailed(string reason) => LanguageService.Current.T("auth.openai.login.failed", reason);
    public static string AuthOpenAiLoginBound(string providerId) => LanguageService.Current.T("auth.openai.login.bound", providerId);
    public static string AuthOpenAiLogoutSuccess => LanguageService.Current.T("auth.openai.logout.success");
    public static string AuthOpenAiLogoutUnbound(string providerId) => LanguageService.Current.T("auth.openai.logout.unbound", providerId);
    public static string AuthOpenAiStatusSignedIn(string account, string plan, string lastRefresh)
        => LanguageService.Current.T("auth.openai.status.signed_in", account, plan, lastRefresh);
    public static string AuthOpenAiStatusSignedOut => LanguageService.Current.T("auth.openai.status.signed_out");
    public static string AuthOpenAiUsageHeader => LanguageService.Current.T("auth.openai.usage.header");
    public static string AuthOpenAiUsageWindowFiveHour => LanguageService.Current.T("auth.openai.usage.window.fiveHour");
    public static string AuthOpenAiUsageWindowWeekly => LanguageService.Current.T("auth.openai.usage.window.weekly");
    public static string AuthOpenAiUsageUnavailable(string reason) => LanguageService.Current.T("auth.openai.usage.unavailable", reason);
    public static string AuthOpenAiUsageCreditsBalance(string balance) => LanguageService.Current.T("auth.openai.usage.credits.balance", balance);
    public static string AuthOpenAiUsageCreditsUnlimited => LanguageService.Current.T("auth.openai.usage.credits.unlimited");
    public static string AuthOpenAiUsageLimitReached(string kind) => LanguageService.Current.T("auth.openai.usage.limitReached", kind);
}
