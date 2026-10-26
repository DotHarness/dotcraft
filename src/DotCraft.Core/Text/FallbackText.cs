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


    // Commands
    public static string UnknownCommand => Format("command.unknown");
    public static string DidYouMean => Format("command.did_you_mean");
    public static string ViewAllCommands => Format("command.view_all");
    public static string CommandPermissionDenied => Format("command.permission_denied");
    public static string CommandServiceUnavailable => Format("command.service_unavailable");
    public static string CommandBusy => Format("command.busy");
    public static string CommandConversationBound => Format("command.conversation_bound");
    public static string CommandNewStarted => Format("command.new.started");
    public static string CommandStopNoActiveRun => Format("command.stop.no_active_run");
    public static string CommandStopStopped => Format("command.stop.stopped");
    public static string CommandStopAllStopped(int queued, int tasks) => Format("command.stop.all_stopped", queued, tasks);
    public static string CommandTasksTitle => Format("command.tasks.title");
    public static string CommandTasksEmpty => Format("command.tasks.empty");
    public static string CommandTasksUsage => Format("command.tasks.usage");
    public static string CommandTasksNotFound(string number) => Format("command.tasks.not_found", number);
    public static string CommandTasksStopChoice(string label) => Format("command.tasks.stop_choice", label);
    public static string CommandTasksStopped(string label, string status) => Format("command.tasks.stopped", label, status);
    public static string CommandStatusTitle(string title) => Format("command.status.title", title);
    public static string CommandStatusState(string state) => Format("command.status.state", state);
    public static string CommandStatusStateIdle => Format("command.status.state.idle");
    public static string CommandStatusStateRunning => Format("command.status.state.running");
    public static string CommandStatusStateWaitingApproval => Format("command.status.state.waiting_approval");
    public static string CommandStatusStateWaitingInput => Format("command.status.state.waiting_input");
    public static string CommandStatusModel(string model) => Format("command.status.model", model);
    public static string CommandStatusMode(string mode) => Format("command.status.mode", mode);
    public static string CommandStatusContext(string tokens, string window) => Format("command.status.context", tokens, window);
    public static string CommandStatusQueued(int count) => Format("command.status.queued", count);
    public static string CommandStatusTasks(int count) => Format("command.status.tasks", count);
    public static string CommandStatusApprovals(int count) => Format("command.status.approvals", count);
    public static string CommandStatusUnknown => Format("command.status.unknown");
    public static string CommandModelCurrent(string model) => Format("command.model.current", model);
    public static string CommandModelUnavailable(string reason) => Format("command.model.unavailable", reason);
    public static string CommandModelUnknown(string model) => Format("command.model.unknown", model);
    public static string CommandModelSet(string model) => Format("command.model.set", model);
    public static string CommandModeCurrent(string mode) => Format("command.mode.current", mode);
    public static string CommandModeSet(string mode) => Format("command.mode.set", mode);
    public static string CommandModeUsage => Format("command.mode.usage");
    public static string CommandSessionsTitle => Format("command.sessions.title");
    public static string CommandSessionsCurrent => Format("command.sessions.current");
    public static string CommandSessionsArchived => Format("command.sessions.archived");
    public static string CommandSessionsAge(string elapsed) => Format("command.sessions.age", elapsed);
    public static string CommandResumeUsage => Format("command.resume.usage");
    public static string CommandResumeAlreadyCurrent => Format("command.resume.already_current");
    public static string CommandResumeResumed(string title) => Format("command.resume.resumed", title);
    public static string CommandCompactResult(string outcome) => Format("command.compact.result", outcome);
    public static string CommandReloadSkillsResult => Format("command.reloadSkills.result");
    public static string CommandHelpTitle => Format("command.help.title");
    public static string CommandHelpCustomSection => Format("command.help.custom_section");
    public static string CommandHelpAdminSuffix => Format("command.help.admin_suffix");

    // Auth commands
    public static string AuthLoginStarting(string providerId) => Format("auth.login.starting", providerId);
    public static string AuthLoginUrl(string url) => Format("auth.login.url", url);
    public static string AuthLoginDeviceCode(string code, string url) => Format("auth.login.deviceCode", code, url);
    public static string AuthLoginWaiting => Format("auth.login.waiting");
    public static string AuthLoginCancelled => Format("auth.login.cancelled");
    public static string AuthLoginFailed(string reason) => Format("auth.login.failed", reason);
    public static string AuthLogoutSuccess(string providerId) => Format("auth.logout.success", providerId);
    public static string AuthStatusSignedIn(string account, string plan) => Format("auth.status.signedIn", account, plan);
    public static string AuthStatusSignedOut(string providerId) => Format("auth.status.signedOut", providerId);
    public static string AuthVerifySuccess(string model) => Format("auth.verify.success", model);
    public static string AuthUsageHeader => Format("auth.usage.header");
    public static string AuthUsageWindowFiveHour => Format("auth.usage.window.fiveHour");
    public static string AuthUsageWindowWeekly => Format("auth.usage.window.weekly");
    public static string AuthUsageWindowPrimary => Format("auth.usage.window.primary");
    public static string AuthUsageWindowSecondary => Format("auth.usage.window.secondary");
    public static string AuthUsageCreditsBalance(string balance) => Format("auth.usage.credits.balance", balance);
    public static string AuthUsageCreditsUnlimited => Format("auth.usage.credits.unlimited");
    public static string AuthUsageLimitReached(string kind) => Format("auth.usage.limitReached", kind);

    private const string EnglishJson = """
{
  "cmd.help": "Show help information",
  "cmd.init": "Create an AGENTS.md file with instructions for DotCraft",
  "cmd.new": "Archive this thread and start a new one",
  "cmd.debug": "Toggle debug mode",
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
  "command.unknown": "Unknown command",
  "command.did_you_mean": "Did you mean",
  "command.view_all": "Type /help to see all available commands.",
  "command.permission_denied": "Permission denied. This command is available to administrators only.",
  "command.service_unavailable": "The service required by this command is unavailable.",
  "command.busy": "This conversation is busy. Try again when the current run finishes, or send /stop.",
  "command.conversation_bound": "This chat is bound to a DotCraft conversation. Unbind it in DotCraft before switching conversations.",
  "command.new.started": "Started a new conversation.",
  "command.stop.description": "Stop the current run; /stop all also clears queued messages and stops background tasks",
  "command.stop.no_active_run": "There is no active agent run.",
  "command.stop.stopped": "Agent stopped.",
  "command.stop.all_stopped": "Stopped everything: the current run, {0} queued message(s) and {1} background task(s).",
  "command.tasks.description": "List background tasks, or stop one with /tasks stop <n>",
  "command.tasks.title": "Background tasks:",
  "command.tasks.empty": "No background tasks.",
  "command.tasks.usage": "Usage: /tasks or /tasks stop <n>",
  "command.tasks.not_found": "There is no background task {0}. Send /tasks to list them.",
  "command.tasks.stop_choice": "Stop {0}",
  "command.tasks.stopped": "{0}: {1}",
  "command.status.description": "Show what this conversation is doing",
  "command.status.title": "Thread: {0}",
  "command.status.state": "State: {0}",
  "command.status.state.idle": "idle",
  "command.status.state.running": "running",
  "command.status.state.waiting_approval": "waiting for approval",
  "command.status.state.waiting_input": "waiting for input",
  "command.status.model": "Model: {0}",
  "command.status.mode": "Mode: {0}",
  "command.status.context": "Context: {0} / {1} tokens",
  "command.status.queued": "Queued messages: {0}",
  "command.status.tasks": "Running background tasks: {0}",
  "command.status.approvals": "Pending approvals: {0}",
  "command.status.unknown": "unknown",
  "command.model.description": "Show the available models, or switch with /model <model>",
  "command.model.current": "Current model: {0}",
  "command.model.unavailable": "Couldn't list models: {0}",
  "command.model.unknown": "{0} is not one of this provider's models. Send /model to list them.",
  "command.model.set": "Later turns will use {0}.",
  "command.mode.description": "Show the mode, or switch with /mode agent or /mode plan",
  "command.mode.current": "Mode: {0}",
  "command.mode.set": "Mode set to {0}.",
  "command.mode.usage": "Usage: /mode [agent|plan]",
  "command.sessions.description": "List this conversation's recent threads",
  "command.sessions.title": "Recent threads:",
  "command.sessions.current": "current",
  "command.sessions.archived": "archived",
  "command.sessions.age": "{0} ago",
  "command.resume.description": "Switch to a thread from /sessions with /resume <n>",
  "command.resume.usage": "Usage: /resume <n>. Send /sessions to list threads.",
  "command.resume.already_current": "That thread is already the current one.",
  "command.resume.resumed": "Switched to {0}.",
  "command.compact.description": "Compact this conversation's context",
  "command.compact.result": "Compaction finished: {0}.",
  "command.reloadSkills.description": "Reload skills changed on disk",
  "command.reloadSkills.result": "Skills reloaded. Each conversation sees the current skills list on its next request.",
  "command.help.title": "Available commands:",
  "command.help.custom_section": "Custom commands:",
  "command.help.admin_suffix": "(admin)",
  "approval.file.operation": "Operation:",
  "approval.file.path": "Path:",
  "approval.file.title": "⚠️  Approval Required: File operation outside workspace",
  "approval.file.approve_question": "Approve this operation?",
  "approval.shell.command": "Command:",
  "approval.shell.working_dir": "Working directory:",
  "approval.shell.title": "⚠️  Approval Required: Shell command",
  "approval.shell.reasons": "Why:",
  "approval.shell.remember_prefix": "Permanent approval remembers commands starting with:",
  "approval.shell.remember_exact": "Permanent approval remembers this exact command only.",
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
  "hub.notification.approval_requested.body": "Needs your approval",
  "hub.notification.input_requested.body": "Has a question for you",
  "hub.notification.thread.default": "Current chat",
  "auth.login.starting": "Signing in to provider '{0}'...",
  "auth.login.url": "If your browser does not open automatically, visit:\n  {0}",
  "auth.login.deviceCode": "Enter code {0} at {1}",
  "auth.login.waiting": "Waiting for authorization in the browser...",
  "auth.login.cancelled": "Sign-in was cancelled.",
  "auth.login.failed": "Sign-in failed: {0}",
  "auth.logout.success": "Signed out of provider '{0}'.",
  "auth.status.signedIn": "Signed in as {0} (plan: {1}).",
  "auth.status.signedOut": "Not signed in. Run `dotcraft auth login {0}` to start.",
  "auth.verify.success": "Credentials refreshed; model '{0}' is listed in the catalog.",
  "auth.usage.header": "Usage:",
  "auth.usage.window.fiveHour": "5h window ",
  "auth.usage.window.weekly": "Weekly    ",
  "auth.usage.window.primary": "Usage     ",
  "auth.usage.window.secondary": "Secondary ",
  "auth.usage.credits.balance": "{0}",
  "auth.usage.credits.unlimited": "unlimited",
  "auth.usage.limitReached": "Limit reached ({0}); requests may fail until reset."
}
""";
}
