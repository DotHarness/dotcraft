// TUI strings.

pub const THINKING: &str = "Thinking...";
pub const THINKING_COLLAPSED: &str = "Thinking... (Tab to expand)";
pub const CONNECTED: &str = "● Connected";
pub const DISCONNECTED: &str = "○ Disconnected";
pub const MODE_AGENT: &str = "Agent";
pub const MODE_PLAN: &str = "Plan";
pub const ENTER_TO_SEND: &str = "Enter to send  Shift+Enter newline  Ctrl+C interrupt";
pub const APPROVAL_TITLE: &str = "Approval Required";
pub const PLAN_TITLE_PREFIX: &str = "Plan: ";
pub const SUBAGENTS_TITLE: &str = "SubAgents";
pub const PLACEHOLDER: &str = "Type a message, /skills, or $skill...";
pub const TYPING_INDICATOR: &str = "▍";
pub const SCROLL_INDICATOR: &str = "↓ {} more lines";
pub const TURN_RUNNING: &str = "Running";
pub const TURN_APPROVAL: &str = "⏸ Awaiting approval";
pub const TURN_USER_INPUT: &str = "⏸ Awaiting answer";
pub const TURN_IDLE: &str = "";
pub const SYSTEM_COMPACTING: &str = "⟳ Compacting context...";
pub const SYSTEM_CONSOLIDATING: &str = "⟳ Consolidating...";
pub const TOOL_RUNNING_PREFIX: &str = "⠋";
pub const TOOL_DONE_PREFIX: &str = "✓";
pub const TOOL_ERROR_PREFIX: &str = "✗";
pub const USER_PREFIX: &str = "❯";
pub const ERROR_PREFIX: &str = "✗";
pub const REASONING_HEADER: &str = "💭 Thinking";
pub const TOKENS_LABEL: &str = "tokens";
pub const SCROLL_TOP: &str = "↑ top";
pub const SCROLL_BOTTOM: &str = "↓ bottom";
pub const MORE_LINES: &str = "↓";
pub const APPROVE: &str = "Approve";
pub const REJECT: &str = "Reject";
pub const EXPAND_HINT: &str = "(Enter to expand)";
pub const COLLAPSE_HINT: &str = "(Enter to collapse)";
pub const TAB_TOGGLE_REASONING: &str = "Tab: toggle reasoning";

// Phase 3: approval overlay
pub const APPROVAL_SHELL: &str = "Shell Command";
pub const APPROVAL_FILE: &str = "File Operation";
pub const APPROVAL_ACCEPT: &str = "Accept";
pub const APPROVAL_ACCEPT_SESSION: &str = "Accept for Session";
pub const APPROVAL_ACCEPT_ALWAYS: &str = "Accept Always";
pub const APPROVAL_DECLINE: &str = "Decline";
pub const APPROVAL_CANCEL: &str = "Cancel Turn";
pub const APPROVAL_OPERATION_LABEL: &str = "Command";
pub const APPROVAL_TARGET_LABEL: &str = "Directory";
pub const APPROVAL_REASON_LABEL: &str = "Reason";

// Phase 3: focus indicator
pub const FOCUS_CHAT_HINT: &str = "Esc: scroll chat";
pub const FOCUS_INPUT_HINT: &str = "Enter/i: input";

// Phase 3: notification toast
pub const NOTIFICATION_JOB_RESULT: &str = "Job Result";
pub const NOTIFICATION_SUCCESS: &str = "Success";
pub const NOTIFICATION_ERROR: &str = "Error";

// Phase 4: thread picker overlay
pub const SESSIONS_TITLE: &str = "Sessions";
pub const SESSIONS_EMPTY: &str = "No threads found.";
pub const SESSIONS_LOADING: &str = "Loading...";
pub const SESSIONS_RESUME_HINT: &str = "Enter: Resume";
pub const SESSIONS_ARCHIVE_HINT: &str = "a: Archive";
pub const SESSIONS_DELETE_HINT: &str = "d: Delete";
pub const SESSIONS_CLOSE_HINT: &str = "Esc: Close";

// Phase 4: misc
pub const CRON_NO_JOBS: &str = "No cron jobs configured.";
pub const THREAD_NOT_FOUND: &str = "Thread not found.";
pub const FEATURE_UNAVAILABLE: &str = "This feature is not available on this server.";
pub const MODEL_USAGE: &str = "Usage: /model [name|default]";
pub const MODEL_UPDATED_DEFAULT: &str = "Model reset to Default.";
pub const MODEL_UPDATED_TO: &str = "Model updated to {}.";
pub const THINKING_USAGE: &str = "Usage: /thinking [default|off|low|medium|high|extra-high]";
pub const THINKING_UPDATED_DEFAULT: &str = "Thinking reset to Default.";
pub const THINKING_UPDATED_TO: &str = "Thinking updated to {}.";
pub const MODEL_DEFAULT_LABEL: &str = "Default";
pub const MODEL_PICKER_TITLE: &str = "Model Selection";
pub const MODEL_PICKER_LOADING: &str = "Loading models...";
pub const MODEL_PICKER_EMPTY: &str = "No models available.";
pub const MODEL_PICKER_SELECT_HINT: &str = "Enter: Apply";
pub const MODEL_PICKER_CLOSE_HINT: &str = "Esc: Close";
pub const WELCOME_MODEL_LABEL: &str = "Model";

// UX polish: footer hints
pub const MODE_CYCLE_HINT: &str = "shift+tab to cycle";
pub const SHORTCUTS_HINT: &str = "/skills";
pub const FOOTER_NO_THREAD: &str = "No thread";

// UX polish round 2: welcome, action feedback
pub const WELCOME_MESSAGE: &str = "✦ DotCraft v{}\n  Workspace: {}\n  Type a message to start · /skills for skills · /sessions for history";
pub const NEW_SESSION_HINT: &str = "New conversation. Type a message to begin.";
pub const SESSION_LOADED_PREFIX: &str = "Session loaded:";

// New design (v0.2.0): StatusIndicator, FooterLine, WelcomeScreen
pub const WORKING: &str = "Working";
pub const ESC_TO_INTERRUPT: &str = "esc to interrupt";
pub const TAB_TO_QUEUE: &str = "tab to queue message";
pub const ENTER_TO_SEND_HINT: &str = "enter to send · shift+enter newline";
pub const QUIT_CONFIRM_HINT: &str = "press ctrl+c again to quit";
pub const WELCOME_HINT_START: &str = "Type a message to start";
pub const WELCOME_HINT_COMMANDS: &str = "$skill to mention skills · /skills to manage";
pub const WELCOME_CONNECTING: &str = "Connecting...";
pub const WELCOME_READY: &str = "Connected — press any key to start";
pub const WELCOME_WORKSPACE_LABEL: &str = "workspace";
pub const WELCOME_THREAD_LABEL: &str = "thread";
pub const WELCOME_STATUS_LABEL: &str = "status";
pub const WELCOME_MODEL_CHANGE_HINT: &str = "to change";
pub const WELCOME_CONNECTION_FAILED: &str = "Connection failed";
pub const WELCOME_TIP_PREFIX: &str = "Tip:";
pub const COMMAND_POPUP_HINT: &str = "↑/↓ navigate · tab complete · enter run · esc close";
pub const SKILL_POPUP_HINT: &str = "↑/↓ navigate · tab insert · enter insert · esc close";
pub const SKILL_POPUP_INSERT_HINT: &str = "Press enter to insert or esc to close";
pub const SKILLS_PICKER_TITLE_LINE: &str = "Enable/disable skills";
pub const SKILLS_PICKER_SEARCH_HINT: &str = "type to search";
pub const SKILLS_PICKER_LOADING: &str = "Loading skills...";
pub const SKILLS_PICKER_EMPTY: &str = "No matching skills";
pub const PERMISSIONS_PICKER_TITLE: &str = "Choose permissions";
pub const PERMISSIONS_PICKER_SUBTITLE: &str = "applies to this thread or the next one";
pub const PICKER_NAVIGATE_HINT: &str = "navigate";
pub const PICKER_TOGGLE_HINT: &str = "toggle";
pub const PICKER_APPLY_HINT: &str = "apply";
pub const PICKER_CLOSE_HINT: &str = "close";
pub const PERMISSIONS_DEFAULT_LABEL: &str = "Default";
pub const PERMISSIONS_DEFAULT_DESC: &str = "Use workspace approval defaults.";
pub const PERMISSIONS_AUTO_APPROVE_LABEL: &str = "Auto approve";
pub const PERMISSIONS_AUTO_APPROVE_DESC: &str = "Automatically accept approval-gated operations.";
pub const PERMISSIONS_INTERRUPT_LABEL: &str = "Interrupt";
pub const PERMISSIONS_INTERRUPT_DESC: &str = "Cancel the turn instead of asking for approval.";
pub const PERMISSIONS_WORKSPACE_ONLY_LABEL: &str = "Workspace only";
pub const PERMISSIONS_WORKSPACE_ONLY_DESC: &str = "Reject operations outside the workspace.";
pub const PERMISSIONS_ASK_OUTSIDE_LABEL: &str = "Ask outside workspace";
pub const PERMISSIONS_ASK_OUTSIDE_DESC: &str = "Prompt before outside-workspace operations.";
pub const CALLING: &str = "Calling";
pub const CALLED: &str = "Called";
pub const SUBAGENTS_ACTIVE_HEADER: &str = "SubAgents ({} active, {} done)";
pub const SUBAGENTS_COMPLETE: &str = "SubAgents completed";
pub const PENDING_QUEUED_PREFIX: &str = "Queued:";
