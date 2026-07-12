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

    // Init command
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
    public static string CronColSchedule => Format("cron.col_schedule");
    public static string CronColNextRun => Format("cron.col_next_run");
    public static string CronExecuteOnce => Format("cron.execute_once");
    public static string CronEvery => Format("cron.every");
    public static string CronEnabled => Format("cron.enabled");
    public static string CronDisabled => Format("cron.disabled");
    public static string CronRemoveUsage => Format("cron.remove_usage");
    public static string CronJobDeleted => Format("cron.job_deleted");
    public static string CronJobDeletedSuffix => Format("cron.job_deleted_suffix");
    public static string CronJobNotFound => Format("cron.job_not_found");
    public static string CronUsage => Format("cron.usage");

    // Commands
    public static string UnknownCommand => Format("command.unknown");
    public static string DidYouMean => Format("command.did_you_mean");
    public static string ViewAllCommands => Format("command.view_all");
    public static string CommandPermissionDenied => Format("command.permission_denied");
    public static string CommandServiceUnavailable => Format("command.service_unavailable");
    public static string CommandNewCleared => Format("command.new.cleared");
    public static string CommandStopNoActiveRun => Format("command.stop.no_active_run");
    public static string CommandStopStopped => Format("command.stop.stopped");
    public static string CommandHelpTitle => Format("command.help.title");
    public static string CommandHelpCustomSection => Format("command.help.custom_section");
    public static string CommandHelpAdminSuffix => Format("command.help.admin_suffix");
    public static string CommandCronListTitle => Format("command.cron.list_title");

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
  "cmd.help": "Show help information",
  "cmd.init": "Create an AGENTS.md file with instructions for DotCraft",
  "cmd.new": "Create a new session",
  "cmd.debug": "Toggle debug mode",
  "cmd.heartbeat": "Trigger heartbeat check immediately",
  "cmd.cron_list": "List cron jobs",
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
  "debug.enabled": "Debug mode enabled",
  "debug.disabled": "Debug mode disabled",
  "heartbeat.unavailable": "Heartbeat service unavailable.",
  "heartbeat.triggering": "Triggering heartbeat...",
  "heartbeat.result": "Heartbeat result",
  "heartbeat.no_response": "No heartbeat response (HEARTBEAT.md may be empty or missing).",
  "heartbeat.usage": "Usage: /heartbeat trigger",
  "cron.unavailable": "Cron service unavailable.",
  "cron.no_jobs": "No cron jobs.",
  "cron.col_schedule": "Schedule",
  "cron.col_next_run": "Next Run",
  "cron.execute_once": "At",
  "cron.every": "Every",
  "cron.enabled": "Enabled",
  "cron.disabled": "Disabled",
  "cron.remove_usage": "Usage: /cron remove <jobId>",
  "cron.job_deleted": "Job",
  "cron.job_deleted_suffix": "deleted.",
  "cron.job_not_found": "Job not found",
  "cron.usage": "Usage: /cron list | /cron remove <id> | /cron enable <id> | /cron disable <id>",
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
  "command.help.admin_suffix": "(admin)",
  "command.cron.list_title": "Cron jobs ({0}):",
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
