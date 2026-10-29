# DotCraft Desktop UX Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.9 |
| **Status** | Living |
| **Date** | 2026-10-29 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Tool Architecture](../architecture/tools-architecture.md), [App Binding](../protocols/app-binding.md), [Plugin Architecture](../architecture/plugin-architecture.md), [Hub Architecture](../architecture/hub-architecture.md), [Remote Tool Host](../architecture/remote-tool-host.md), [Goal Design](../features/goal.md), [Session Import](../features/session-import.md), [Desktop DESIGN.md](../architecture/DESIGN.md) |

Purpose: Define the stable user-experience behavior of **DotCraft Desktop** as a protocol client for DotCraft AppServer: user-visible flows, interaction rules, state ownership, state transitions, and recovery behavior. It does not define frontend implementation, visual design, or framework choices.

## 1. Scope

### 1.1 What This Spec Defines

- How users open workspaces, connect, navigate projects and threads, send turns, review results, and respond to approvals.
- How protocol events change user-visible state.
- Which workspace state owns which surfaces (foreground vs secondary workspaces).
- How secondary surfaces such as plugins, skills, automations, settings, and channel modules behave from the user's perspective.
- How trusted presentation descriptors and interactive View capabilities become safe conversation surfaces.
- How Desktop-owned Runtime Dynamic Tools manage background threads through AppServer.
- How the client communicates failure, recovery, and availability constraints.
- Localization, accessibility, and performance expectations at the UX level.

### 1.2 What This Spec Does Not Define

- Wire protocol payloads, transport rules, or server semantics defined in [appserver-protocol.md](../protocols/appserver-protocol.md).
- TypeScript module contract details defined in [plugin-architecture.md](../architecture/plugin-architecture.md).
- Frontend frameworks, component trees, IPC signatures, process architecture, or state-store structure.
- Layout geometry, colors, typography, icons, spacing, animation, or other visual rules, which belong to [Desktop DESIGN.md](../architecture/DESIGN.md).
- Detailed behavior of Desktop features that have their own specs.

## 2. Goals and Non-Goals

### 2.1 Goals

1. Expose the AppServer protocol as a desktop workflow optimized for persistent threads and long-running agent work.
2. Support multi-thread productivity, including switching between threads while background work continues.
3. Support project-first navigation across local projects while preserving one foreground workspace owner for interactive work.
4. Preserve a clear review loop for approvals, file changes, plans, tool output, and automation runs.
5. Make connection state and recovery paths understandable without requiring protocol knowledge.
6. Keep workspace behavior predictable across reconnects, restarts, and concurrent clients.

### 2.2 Non-Goals

- Embedding the DotCraft runtime in-process.
- Acting as a full IDE, terminal emulator, or general-purpose file browser.
- Freezing a specific visual layout or frontend architecture.
- Aggregating multiple remote workspaces in the background. Remote projects are foreground-only.

## 3. Connection and Session Lifecycle

Desktop is a host adapter over the TypeScript SDK. Its AppServer and Hub transports are owned by the trusted host side of the application; the UI reaches them only through an authorized projection and never opens a transport itself. Desktop does not maintain a parallel JSON-RPC or Hub implementation and consumes the generated protocol contracts rather than restating payload shapes. Desktop extension calls use the same connection routing and remain scoped to the foreground workspace.

### 3.1 Workspace Entry

- The client is workspace-centric and may show multiple known workspaces in one window.
- Exactly one workspace is the foreground workspace at any time. Foreground workspace state owns the composer, active conversation, settings, capabilities, model/provider selection, workspace tools, and server-initiated interactive requests.
- Additional local workspaces may be connected as secondary workspaces. They may contribute thread-list rows and lightweight runtime indicators but do not own workspace-scoped panels or interactive protocol surfaces until promoted to foreground.
- Opening or selecting a stopped workspace starts a connection to an AppServer for that workspace, then promotes it to foreground after initialization succeeds. Thread operations remain unavailable until initialization succeeds.

### 3.1.1 Local and Remote AppServer Ownership

- In Local mode, Desktop connects through a Hub-managed local AppServer. Desktop may start, stop, and restart that process; connection-level changes may use an Apply & Restart action.
- In Remote mode, the AppServer lifecycle belongs to the user or remote environment. Desktop never restarts a remote AppServer and never routes remote connection changes through local Hub restart semantics.
- Remote connection changes use test-and-connect: Desktop validates the draft `ws://`/`wss://` URL and token, completes an `initialize` probe against the draft endpoint, and persists the settings and switches only after the probe succeeds. A failed probe leaves persisted settings unchanged.
- When Desktop is launched with a transient `--remote` endpoint, persistent connection-mode switching is unavailable for that session, and Settings explains why.
- Remote projects and Docker deployments opened from SSH Machines ([§6.10](#610-ssh-machines)) are a forwarded case of Remote mode: Desktop connects through a local SSH forward and reuses the same test-and-connect path.
- A Remote-mode session is a distinct foreground project identity, separate from the local workspace that initiated it. Local threads, pinned state, and welcome drafts stay isolated from the remote project.
- Connecting to a remote project records the previous local foreground workspace. Disconnecting the remote project or selecting a local project closes the remote client and its forward and restores the previous local foreground when possible.
- A failed connection remains recoverable without restarting Desktop.

### 3.2 Connection States

| State | Meaning |
|-------|---------|
| `connecting` | The client is establishing transport and completing protocol initialization. |
| `connected` | Initialization is complete and thread operations are available. |
| `disconnected` | A previously working connection is unavailable, but recovery may still be possible. |
| `error` | Connection failed or cannot proceed without user intervention. |

### 3.3 Initial Load

- After connection becomes `connected`, the client loads server capabilities, the thread list for the active workspace identity, and capability-gated surfaces such as skills, automations, or the model catalog.
- If no thread exists, the client presents an empty ready state that allows starting a new conversation.

### 3.4 Reconnection

Desktop uses the SDK Wire reconnect profile. Already-written requests fail on disconnect and are never replayed; calls made while reconnecting are ordered behind a successful `initialize` / `initialized` handshake. Desktop restores UI subscriptions, interactive-request state, runtime Dynamic Tools, SSH forwards, and other workspace resources once the connection is ready.

- On unexpected disconnect, the client transitions to `disconnected` and reconnects automatically.
- During reconnect, the user can tell that displayed thread data is local while live updates are unavailable.
- After reconnect, the client reloads capabilities, re-establishes the active thread subscription, and refreshes thread and automation data that may have changed.
- If recovery succeeds, the state returns to `connected` without the user rebuilding context. If recovery fails persistently, the state becomes `error` with a retry path.

### 3.5 Workspace Switching

- Switching promotes one workspace to foreground and demotes the previous one to secondary when it can remain connected.
- A switch that completes quickly, such as to a workspace whose AppServer is already running, shows no loading state. Opening placeholders (project thread skeletons and the Welcome footer skeleton) appear only while the connection is still pending after a short grace period, and reloading the foreground list never replaces or expands a project's known rows.
- Window state may be remembered, but protocol state never leaks across workspaces.
- A switch resets the active thread selection unless the new workspace has a valid remembered equivalent.
- Workspace-scoped panels and capabilities are rehydrated from the new foreground AppServer; secondary workspace state never drives them.
- During local-to-local switching, unchanged plugins keep their instances, icons, and usable sidebar entries. The target workspace's complete plugin catalog determines additions, removals, and replacements according to the [Desktop extension lifecycle](../architecture/desktop-plugins.md#runtime-lifecycle).

### 3.6 Multiple Windows

- Each window owns its own foreground workspace selection; all windows show the same local projects.
- The same workspace may be connected by more than one Desktop process. AppServer multi-client semantics own protocol safety; Desktop does not rely on a process-exclusive workspace lock.
- User actions in one window do not change thread selection or visible state in another, except that workspace-level AppServer broadcasts may update shared thread summaries in all connected windows.
- Each Desktop process registers a control endpoint in a user-scoped process registry. Tray Exit gracefully quits every registered Desktop process before shutting down Hub. Closing an individual window is client-only and stops neither Hub nor other windows.

### 3.7 Projects Rail, Thread Navigation, and Secondary Connections

**Structure**

- The sidebar thread list is project-first: a **Pinned** section, then **Projects**, then (in local mode) **Recents** for the default Chat workspace. It does not use time-group headings.
- Pinned may contain individually pinned threads and pinned projects. A pinned project moves its whole subtree into Pinned; an individually pinned thread appears only in Pinned, never duplicated in its project. Pin state is Desktop-local, covers local and remote project identities, and survives reconnects.
- Recents can optionally also list unpinned threads of loaded projects; a thread shown in two places is highlighted only where it was selected.
- The Projects section is sourced from Hub's project list for local workspaces, remote projects saved for SSH machines, and the active remote foreground project. Adding or removing a local project goes through Hub so every window and paired phone agree; Desktop re-reads the list on `projects.changed`. Stopped local projects appear without being started eagerly.
- Local and remote projects keep distinct identities so remote threads never appear under the local workspace that initiated the remote connection.

**Ordering and collapse**

- Each section, and the project rows themselves, has a Desktop-local sort preference: **Last updated** or **Manual order** (drag to reorder within one list; items never move between lists). Reordering pauses while a search filters the list.
- Project and section collapse state is Desktop-local and persisted. Expanding a project row does not promote it. A project shows a bounded number of threads with a show-more control; the selected thread and search matches are always visible.

**Ownership**

- Foreground workspace state owns the composer, settings, capabilities, tools, active thread subscription, and server-initiated interactive requests. Secondary workspace state may update thread summaries and compact runtime indicators only.
- For local projects whose AppServer is already running, Desktop may open a bounded number of secondary connections per window (LRU). Excess workspaces remain cold until selected.
- Secondary connections load thread summaries and consume only thread-list and runtime notifications; they do not receive turn, item, job, configuration, MCP, or server-request streams.
- Only the latest-started complete thread-list load for a connection may update its cached summaries, so stale results never overwrite newer rows. Filtered, paginated, archived, or internal-thread queries do not replace the cache.
- A failed background refresh keeps cached rows and retries with backoff; the project shows a list-load error only after retries are exhausted, and any later successful load clears it.
- Foreground and secondary rows show the unread dot from the thread's server read receipt ([Thread Read State](../features/thread-read-state.md#6-client-behavior)), hydrated from `thread/list` and updated by `thread/readState/changed`. Desktop marks a thread read only while it is open in a focused window. Promoting a project keeps its receipts.
- Remote projects are never background secondary connections. At most one remote project is active, and it is foreground-only. Disconnecting an unsaved remote project removes it from the rail; a saved SSH remote project stays listed without thread rows.

**Actions**

- Clicking a background thread promotes its owning workspace, then opens the thread on the foreground connection.
- A foreground project's eligible thread row context menu offers **Mark as unread** while the thread is read and **Mark as read** while it is unread, after Rename.
- The global New Chat action starts in the current foreground project. A project-level New Chat first promotes or starts that project; new thread creation always uses the foreground AppServer. The welcome composer keeps a separate draft per project identity and reloads capabilities, skills, plugins, and model state after promotion.
- Project actions depend on project kind: open, pin, edit, open folder in the system file explorer, **Archive chats**, and **Remove project** (unavailable for the foreground project). Remote projects have no edit or file explorer action, and an open remote project offers disconnect instead of removal. Workspace restart and stop live in the tray, not in project actions.
- **Archive chats** archives every loaded, unpinned chat in the project after a confirmation stating the count and warning when work is running.
- A thread row ends its title with a quiet worktree mark when the thread runs in a worktree. When nothing more urgent occupies its status slot (pending request, running, unread), the slot shows the pull request of the thread's branch: Desktop looks it up by the `gitInfo` branch in the GitHub repository named by its `originUrl`, through the local GitHub CLI credentials from the trusted host process, only for rows on screen, sharing one cached lookup per repository and branch that refreshes when the window regains focus and periodically while the pull request is open. Threads without a branch, a GitHub origin, or a pull request show nothing; this works for remote projects too, since the lookup needs no local checkout.
- Thread and project rows reveal details cards on hover or focus. Thread details include the full title, activity time, owning project, and the thread's Git branch (its recorded `gitInfo` branch, else its worktree branch) with any pull request for that branch. Project details include pin control, thread count, waiting/running counts (waiting takes precedence), and path. Unloaded projects say details are not loaded rather than reporting zero threads. Card treatment is defined in [Desktop DESIGN.md](../architecture/DESIGN.md#hover-annotations).

## 4. Protocol Event to UX Behavior

This section defines how protocol messages affect user-visible behavior, as UX outcomes rather than internal state.

### 4.1 Thread Events

| Protocol event | UX behavior |
|---------------|-------------|
| `thread/started` | The new thread appears in navigation and may become selectable immediately. |
| `thread/renamed` | The thread label updates everywhere it is shown. |
| `thread/deleted` | The thread is removed from navigation and any active context; if open, the user moves to a safe fallback state. |
| `thread/archived`, `thread/unarchived` | Thread availability updates immediately; invalid actions are disabled. |
| `thread/resumed` | The thread returns to an active, turn-capable state. |
| `thread/status/changed` | Activity indicators update in the owning workspace's navigation, including secondary workspaces. Summaries use the current Turn identity and start time for elapsed-time display when available. |
| `thread/readState/changed` | The thread's unread dot updates in the owning workspace's navigation, including secondary workspaces. |

A client that displays multiple workspaces routes each notification with the workspace identity of the connection that received it. For secondary connections, only thread-list and runtime notifications update background project groups; notifications affecting conversation content, configuration, tools, MCP, jobs, approvals, user-input requests, or other interactive surfaces are foreground-only.

### 4.2 Turn Events

| Protocol event | UX behavior |
|---------------|-------------|
| `turn/started` | The thread enters a running state. A further submission follows the steer/enqueue rule in [Composer System Actions](#513-composer-system-actions) rather than starting a second turn. |
| `turn/completed` | Running indicators clear and final results show. `tokenUsage` is the final turn aggregate, not an extra delta. |
| `turn/failed` | The user sees the turn ended unsuccessfully and has a path to retry or continue. |
| `turn/cancelled` | The running state clears and the user sees the turn was interrupted. |

- A running turn shows one activity timing boundary with the turn's process beneath it. An explicit final-phase assistant message freezes the duration and folds the process in place before the turn completes; providers without phase metadata fold on completion. Commentary never triggers an early fold. Final replies and persistent results stay outside the folded process.
- A cancelled turn keeps partial replies and tool results expanded and replaces the activity boundary with one stop status. Only a stop requested by this window is attributed to the user; other interruptions use neutral wording. Duration comes from valid start and completion timestamps and is omitted otherwise. Raw cancellation reasons are not appended to conversation text.
- `Turn.error` is the canonical user-facing turn failure. If the same failure is also retained as an Error item, Desktop presents it once.
- While a turn is running, the conversation always shows visible activity. The turn ends with one working line, below everything else in the turn at the same spacing as tool rows, mounted for the whole run so content never jumps. Its thinking glyph loops while the turn works. Its label shows the latest reasoning title, including one from a finished reasoning block, for as long as the turn works, or the localized Thinking label when reasoning yields none; while the turn waits on a user-input request it says it is waiting for the user's answer.
- When the turn stops working (it ends, sleeps, or waits on an approval), the glyph fades out at once. The line keeps its space while its turn is the latest, so the reply does not move, and gives it up in the same frame as the next message appears. A turn opened after it stopped has no working line.
- While the thread has background tasks running (background terminals, running or paused Workflow runs, and working subagents, as the Summary lists them), the latest turn's working line shows their count. While the turn works, the count comes before the label, separated by a middle dot, and the label truncates first. Once the turn stops, the count stays on its own with a pulsing dot in the glyph's place, also on a turn opened after it stopped. Selecting the count shows the Summary. The count leaves when the last task ends.
- Conversation Markdown renders math: display math between `$$` or `\[ \]` lines and inline `\( \)`, `\[ \]`, or `$$ $$`. A single `$` stays text, delimiters in code stay literal, and an unparseable formula shows its source. Copying a formula copies its TeX. Raw HTML in replies shows as text except a line break.

### 4.3 Item Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/started` | New agent work becomes visible in the current thread. |
| `item/agentMessage/delta` | Agent text streams incrementally. |
| `item/reasoning/delta` | Updates the working line's title; the reasoning body is shown only when the user enables thinking content. |
| `item/toolCall/argumentsDelta` | Tool argument construction streams. Known built-in tools show a bespoke running label and, where useful, a progressive preview; unknown tools (including MCP and module tools) show a generic placeholder without raw argument JSON. |
| `terminal/started`, `terminal/outputDelta`, `terminal/completed` | Shell output and status merge by `threadId + callId` into the matching shell tool row. |
| `item/commandExecution/outputDelta` | Fallback for sessions without `terminal/*`; the same output is never rendered twice. |
| `item/toolExecution/progress` | Structured progress merges by `itemId` and `callId` into the matching tool row. |
| `item/completed` | The final item replaces any in-progress representation. A trusted presentation descriptor may select a local renderer, and an eligible terminal `mcpToolCall` may advertise a live MCP Apps View; generic fallback remains available. |
| `item/usage/delta` | Context usage indicators refresh; deltas accumulate for the active turn and are reconciled by `turn/completed.tokenUsage`. |

- The working line's title comes from the turn's latest reasoning that yields one: its latest heading line (a line that is entirely bold or a Markdown heading), or without one, the first line of its latest paragraph whose first line is complete. A new reasoning block keeps the previous title until it yields its own; with none yet the title is a localized Thinking label. A changing title replaces its text in place; the label fades only as it appears or hides. When thinking content is shown, each reasoning is a static Thought row from the moment it starts, expandable to its text as it streams, and reasoning between tool calls is listed in order inside the tool group without splitting it; only the working line shows that reasoning is in progress.
- Remote Tool Host transfers render as a tool row naming the item, direction, machine, and state, with the source and destination route, live byte and file progress, and final counts. Failure preserves the last progress and reason. Historical results without totals show known statistics only and never borrow the current route. Raw JSON is not rendered.

### 4.4 Approval Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/approval/request` | The thread enters a waiting-for-decision state; the approval becomes the highest-priority interaction. |
| `item/approval/resolved` | The decision is reflected immediately and the thread resumes or terminates accordingly. |

### 4.4.1 User Input Request Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/tool/requestUserInput` | The thread enters a waiting-input state. Desktop replaces the normal composer with a question composer and disables normal submission. |
| `item/tool/requestUserInput/resolved` | The question composer clears and the thread resumes normal rendering. |

If the user switches away while a request is pending, Desktop parks it on that thread, badges the thread as needing an answer, and restores the same composer on return. Switching threads is not a dismissal and never sends an empty response. A request replayed through `thread/subscribe` or `thread/resume` with the same logical identity (see [Interactive Request Restore](#532-interactive-request-restore)) restores the actionable composer rather than appearing as new.

### 4.5 Supplemental Events

| Protocol event | UX behavior |
|---------------|-------------|
| `subagent/progress` | Background worker progress may be surfaced but never blocks the main conversation. |
| `plan/updated` | Structured task progress becomes available in the conversation context. |
| `system/event` | Maintenance steps may be surfaced but never overshadow turn output. Provider stream retries (`kind = "streamError"`) occupy one transient status slot, replaced by each later retry and cleared when the turn ends or the thread reloads; they leave no transcript entry, the first attempt is not surfaced, and the provider failure text stays behind a collapsed disclosure. |
| `system/jobResult` | Automation output becomes visible as an out-of-band result tied to its source run. |
| `automation/updated`, `automation/run/updated` | Definition and run views refresh independently. |
| `thread/goal/updated`, `thread/goal/cleared` | Goal surfaces for the affected thread update or clear from the server snapshot without forcing navigation. |
| `config/changed` | Configuration views re-read `config/read` when a changed key path is one they display, and domain tags refresh their domain views, without manual refresh. |

### 4.6 General Rules

- A subscribed active thread updates without manual refresh.
- Changes to an inactive thread may show background activity but never forcibly switch context.
- When a capability is absent, the corresponding surface is disabled or hidden rather than failing late.

## 5. Core Interaction Flows

### 5.1 Open a Workspace

1. User opens or selects a workspace.
2. Client begins connecting and makes connection state visible.
3. After initialization succeeds, the client loads threads and capability-gated data for the default workspace view.
4. If no thread is selected, the user sees a starting point for a new conversation.

The starting point names the foreground project and lets the user switch project in place, using the same project list and actions as the composer's project picker: local projects, remote projects only when already foreground, creating a project, and leaving for the default Chat workspace. In the default Chat workspace there is no project to name.

### 5.1.1 Conversation Prompt Suggestions

- A workspace setting (on by default, independent of memory) enables one suggested next user message after a successful Turn in a conversation with an editable composer. Generation runs only while that conversation is active, the composer is empty, and nothing is queued.
- Desktop generates it in one hidden ephemeral fork of the completed Turn that keeps the parent's model, instructions, tools, provider history, and prompt-cache routing identity. The fork asks for a short next message in the user's own voice, or nothing when no next step is obvious or the topic is unsafe or sensitive. Empty output means no suggestion.
- The suggestion appears as subdued text whenever the composer is empty, never as a draft or submission. Typing or dictation only hides it, so clearing the composer shows it again. Tab accepts it as editable text; Escape dismisses it. Sending, switching conversations, starting another Turn, disabling the setting, or a deadline cancels the request and cleans up the fork.
- The ephemeral thread and its notifications never enter visible conversation or navigation state. An older result never replaces a newer one, and failure leaves the composer unchanged. Traces link the parent Turn to the temporary one and record outcome without message content.

### 5.1.2 Workspace Setup

- Model access and the default model are personal, so setup runs once per computer rather than once per folder. A local folder is ready when its effective configuration, workspace over personal, resolves a configured provider and a model; whether `.craft/config.json` exists does not matter.
- Desktop creates a missing `.craft/` for a ready folder non-interactively with `dotcraft setup --skip-provider` before connecting, then opens the folder like any other ready workspace. It never overwrites existing workspace config.
- A folder that is not ready shows the setup flow before connecting. The flow is local to Desktop and `dotcraft setup` and does not depend on AppServer provider RPCs.
- Setup covers interface language, model access, and model. Completed steps can be revisited; the last step saves the personal defaults, prepares the folder, and opens it. Project instructions are brought in through Agent Import ([Agent import](../features/agent-import.md)), not setup.
- Model access lists saved explicit personal providers, preselecting the personal default, and offers new connections: Sign in with ChatGPT, OpenAI API key, Anthropic API key, or another OpenAI-compatible service. Sign in with ChatGPT completes OAuth before the model step and creates a `chatgptOAuth` provider. Another service requires a name and http(s) endpoint and defaults its API format to `openai-responses`.
- Model discovery uses the DotCraft backend model catalog, not Desktop-owned constants or provider-specific parsing. Unsaved provider drafts are passed to the backend without exposing credentials in process arguments or logs. A refreshed catalog keeps the selected model when still listed. Listing is advisory: manual model entry stays available when it fails.
- Desktop setup requires a provider and model before finishing; CLI setup may allow skipping the provider.
- Desktop setup saves provider credentials and endpoints to personal provider config, sets the personal provider and model defaults, and removes equivalent workspace overrides. A different model for one folder is chosen later in that folder's composer, which saves it to the workspace.
- Settings provider terminology is `OpenAI-Responses` (`openai-responses`), `OpenAI-Legacy` (`openai-chat-completions`), and `Anthropic`.

### 5.2 Start a New Conversation

1. User chooses to create a thread.
2. Client calls `thread/start` or `worktree/createAndStart` with the complete initial thread configuration, including the selected mode and model snapshot. It includes `approvalPolicy` only for an explicit per-thread override; omission preserves workspace inheritance.
3. Desktop clears the composer and opens the conversation surface immediately, showing the submitted message and a creating state. That placeholder is client-only: it never becomes a thread id, never appears in the thread list, and is not subject to the restore pipeline in [5.3.1](#531-desktop-thread-restore-pipeline).
4. When creation succeeds, the thread becomes active and Desktop submits the staged first message after the thread is restored, without patching thread configuration in between. If the user opened another thread meanwhile, Desktop leaves them there and still starts the message on the created thread. Anything typed into the creating state's composer is carried to the created thread as a draft.
5. If creation fails, Desktop returns the user to the originating surface with the submitted content restored and reports the failure. No thread is left behind.

`dotcraft://threads/new?path=<dir>&prompt=<text>` opens the new-chat surface from the operating system or from a Desktop extension via `navigation.openExternal`. An existing local directory in `path` is added to the project list if needed and selected; a non-blank `prompt` is placed in the composer and never sent. No thread is created until the user sends.

When Welcome selects an Agent Profile, Desktop sends `config.agentProfileId` during creation with only the model overlays the Agent Profile start contract allows. It does not override Profile-owned approval, tool, skill, plugin, MCP, or instruction policy.

### 5.3 Resume or Open an Existing Thread

Selecting a thread starts one coordinated subscription and paged-hydration operation (5.3.1). If the thread is not turn-capable, the user sees why and which actions remain allowed.

The conversation never blanks while it restores. A thread present in the thread list opens at once with its header and composer, using the list summary until the header read lands; only the transcript area waits, with an indicator revealed after 400ms. Submitting before the thread is ready keeps the message in the composer and says the conversation is still loading.

Leaving a thread keeps it **warm**: its subscription stays open and its conversation state keeps applying that thread's events, so reopening it renders the current conversation at once with no history read. At most 10 threads are warm per foreground connection. Leaving an eleventh releases the least recently viewed one, and a warm thread not viewed for 3 hours is released; releasing unsubscribes and drops its conversation state. Opening a thread that is not warm restores it through 5.3.1.

### 5.3.1 Desktop Thread Restore Pipeline

Selecting a thread opens a new restore generation and clears the previous one. These invariants hold for every restore; page count, direction, and size are client choices.

1. Desktop establishes `thread/subscribe`, normally with `replayRecent = true`, before issuing history reads, so updates racing the first page are observable.
2. The thread header and newest Turn page are then read concurrently and merged into a chronological store by stable Turn and Item id.
3. Every Turn in a page is hydrated with all its Items; a page never renders a fragment of a Turn.
4. Replayed approval or user-input composers are not exposed until subscription readiness and the header and page reads for the current generation complete.
5. Results belonging to an older restore generation (reads, subscription operations, interactive request results) are ignored.
6. Subscription updates overwrite loaded entities by stable id; an unloaded Item is appended at the head without duplicating a concurrent page result.
7. History is read on demand, not drained. Loaded history is one or more segments of whole Turns; the newest ends at the live head. A gap before or between segments reserves space and loads a page from its nearer side as it approaches the viewport. Jumping to an unloaded Turn loads a segment around it. Adjacent segments merge, and inserting a page above the viewport preserves the visible scroll anchor. Listing Turn metadata for navigation is not a history read.
8. Switching threads keeps the left thread's loaded segments with its warm state; releasing a warm thread drops them. Live-only output buffers are bounded per thread independently of persisted history.
9. A subscription target is (foreground workspace identity, connection epoch, `threadId`). Desktop subscribes only after the thread is present in that workspace's authoritative thread list.
10. Subscription operations for the same target are serialized; a delayed `thread/unsubscribe` never cancels a newer `thread/subscribe` for that target. Different targets, including the same `threadId` in two workspaces, never block one another.
11. Switching threads or workspaces, disconnecting, or closing the window clears the active generation so late async work cannot subscribe through another workspace connection or restore UI into the wrong thread. Switching workspaces, disconnecting, or closing the window also releases every warm thread of that connection. An unsubscribe queued for an older connection is never sent through the new one.

`thread/read` is the current header, the Turn/Item list methods are persisted display history, and `thread/subscribe` is the live channel. A conversation still in the creating state of [5.2](#52-start-a-new-conversation) enters this pipeline only once its thread id exists.

### 5.3.2 Interactive Request Restore

Pending approval and user-input requests belong to the active turn and survive ordinary navigation.

- Switching away is not a decline, cancel, empty answer, or dismissal.
- A request that arrives while its thread is not the active fully-restored thread, or while conversation updates are paused or unreconciled, is parked on its thread. Parked requests activate when the thread is reopened warm, or otherwise after the current generation's header and head-page hydration complete.
- Requests are matched by logical identity `method + threadId + turnId + requestId`; a fresh JSON-RPC envelope id does not make a new request. A replay restores the actionable composer once, without duplicate cards, queue entries, or decisions.
- Multiple pending approvals for one turn form a queue; the next becomes actionable only after the prior is submitted or resolved.
- Local submission state belongs to one logical request and never leaks to another.
- A successful local response may be acknowledged immediately, but the thread stays running or waiting per server state until a resolved, completed, or reconciled snapshot says otherwise.

### 5.3.3 Snapshot and Realtime Reconciliation

Desktop receives thread truth through durable header/history reads and realtime notifications, and converges when they race.

- A stale header or page never regresses newer realtime state. Merging preserves observed terminal evidence for the same item or call: completed tool executions and results, resolved approvals, completed user-input responses, final agent messages, and terminal turn status. A tool with terminal evidence never returns to "awaiting result".
- A terminal turn state clears running and waiting indicators even if the local view still showed live tools or composers.
- `thread/status/changed` is a summary signal, not conversation history. Desktop uses it as a reconciliation trigger: if the server says the active thread is idle while Desktop still shows activity, Desktop reloads the header and newest pages.
- An active thread with a parked request keeps retrying head-page reconciliation on foreground, reconnect, and metadata refresh; a failed read keeps the request parked and never synthesizes a response.
- If completion notifications are missed after a response, the next reconcile restores completed tools, final output, and terminal state without the user switching away.
- Subagent token counts sum the child's turn `tokenUsage` and rise with `thread/tokenUsage/updated`; they never decrease. Subagent elapsed time derives from `runtime.activeTurnStartedAt` and shows unknown when unavailable.
- Rollback, fork, archive, and unarchive clear affected cursors and segments and reload the header and head pages. Delete clears everything. Reconnect creates a new subscription and never reuses pre-disconnect cursors.
- Reconciliation is scoped to the active foreground thread and workspace.

### 5.3.4 Open a Thread from a Workspace-owned Product Surface

When a native product surface opens a Thread, it supplies the Thread id and canonical Workspace path. If that Workspace is not foreground, Desktop records the pending selection, switches workspace through the normal project lifecycle, and selects the Thread after its thread list loads. Returning to the product surface restores its own view state for the lifetime of the window.

### 5.4 Send a Message

1. User composes input and submits it.
2. If the thread is idle and turn-capable, the client calls `turn/start`.
3. The thread enters a running state; a further submission follows the Input Rules below.
4. Incremental output appears as events arrive.
5. When the turn finishes, the thread returns to an idle, completed, failed, or cancelled state.

### 5.5 Input Rules

- The input area accepts plain text and supported structured attachments or references. Native `contextRef` input parts preserve references across start, queue, steer, and history restore; reference-only drafts count as nonempty input.
- The client prevents submission of an empty turn.
- The composer placeholder is one short shared invitation. It changes only when input means something else, in priority order: connecting, waiting on an approval or answer, composing a thread goal, then Plan mode.
- A submission while the thread is running follows [Composer System Actions](#513-composer-system-actions); the path taken is visible before submission.
- If attachments cannot be preserved in a queued or deferred path, the user is warned before sending.

### 5.6 Approval Handling

1. An approval request arrives while a turn is running, and the thread enters a waiting-approval state.
2. The request is surfaced in the composer with enough information to decide. A shell request shows the command, its directory, and the `reasons` from its `shell` block; session and permanent options describe their scope from `rememberedPrefixes` and `remembersExactCommand`. While pending, the conversation shows one quiet awaiting-approval marker; afterwards the outcome shows on the tool row or turn result.
3. The composer shows two actions: reject and the narrowest allow (allow once, or allow for this thread when the request has no single-use grant). Enter takes the narrowest allow and Esc rejects; wider grants such as session or permanent approval sit in the allow action's menu, so a lasting grant always takes a deliberate second choice. Approved work continues, and declined work reflects rejection and may take another path. The composer does not offer `cancel`; stopping the turn resolves its pending approvals as `cancel`.
   The composer's approval-policy picker stays available while a request is pending; switching the thread to full access resolves the pending requests as approved, and later requests in the turn no longer ask ([Session Core](../architecture/session-core.md) §12.2).
4. If the approval times out or becomes invalid, the user sees the resulting turn outcome.
5. A `computerUse` approval asks whether DotCraft may use the application named by `targetLabel` and offers exactly: allow for this thread (`acceptForSession`, the primary action), always allow (`acceptAlways`, which also adds the application to Desktop's always-allowed list), and decline.

### 5.7 User Input Request Handling

1. A user input request arrives while a turn is running; the thread enters a waiting-input state and the normal composer is replaced by a question composer in place, not a modal.
2. Multi-question requests support moving between questions with controls and keyboard; each question keeps its selection and free-form text.
3. The user picks one option per question by pointer or keyboard. Option descriptions are available on hover or focus. A free-form `Other` input appears when provided and is masked for secret questions.
4. Desktop sends the response immediately and acknowledges it locally; the server confirms with `item/tool/requestUserInput/resolved`.
5. Switching threads parks the request (see [4.4.1](#441-user-input-request-events)). The OS notification for a pending approval or question follows [§6.7](#67-settings-surface) and appears at most once per request.
6. Returning to a thread still in `waitingInput` or `waitingApproval` restores the turn state from hydration and accepts the replayed request whether it arrives before or after hydration.

### 5.8 View Changes, Plans, and Tool Output

**Changes**

- The Changes panel shows the most recent turn that changed files: its line totals, then each changed file with counts and a collapsible diff. Without the live Turn diff, a file edited several times shows one diff composed from its per-call diffs when they apply in sequence, and lists the calls separately only when they cannot be composed; a file whose edits cancel out is omitted. Oversized files say so instead of rendering a diff. File actions copy the workspace-relative path and open the containing folder; the panel offers no revert.
- During a running turn the panel reflects the server's live turn diff. After reopening a thread, a turn's files are rebuilt from the per-edit diffs recorded with each file tool result. Threads without recorded per-edit diffs show no entries.
- A completed turn that changed files ends with a change summary card listing edited files with expandable diffs and a Review action that opens the Changes panel.
- The card's Undo reverts the turn's recorded diff through the workspace's Git repository, newest edit first, then becomes Reapply. Oversized files are left alone; a failed file stops the rest; success, partial success, and failure are reported. Outside a Git repository, or for a remote workspace, Undo changes nothing and reports why.
- While a running turn has changed files and nothing waits on the user, a compact changed-files indicator above the composer opens the Changes panel; it is replaced by the summary card when the turn ends.
- Commit and changelist actions use the files still applied across loaded turns.
- File changes never open or switch the detail panel; the indicator, the card's Review, and the panel's own tab open Changes.

**Plans**

- Plan updates stay associated with the active thread and reflect the latest complete snapshot. While `CreatePlan` arguments stream, the plan surface renders a live draft that is replaced by the snapshot on `plan/updated`. Plan overviews render Markdown. A to-do list without a plan is labeled as progress. Plan updates and `CreatePlan` drafts refresh the plan surface without opening or switching the detail panel.
- When the latest completed Plan-mode Turn contains a successful `CreatePlan`, Desktop replaces the composer with a plan-confirmation composer, regardless of later output in that Turn. The confirmation appears without switching threads, survives thread switches and restarts, and clears when the next Turn starts.
- The active composer mode follows explicit configuration metadata from `thread/updated`, thread reads, and thread restoration, including changes made by another client. Metadata without a mode does not reset it. Synchronization preserves drafts and dismissed plan confirmations; local optimistic mode requests and their callbacks remain scoped to the originating workspace and thread. When explicit mode changes arrive during a pending request, its success reconciles the latest observed mode instead of overwriting it; unchanged pre-request metadata does not undo a successful optimistic change. Plan acceptance does not start implementation if a newer observed mode supersedes the requested Agent mode.

**Discussion board**

- The detail panel's Board system tab shows the active thread's shared discussion board (`subagent/board/*`) read-only. It opens from the add-tab menu or from a board link in the conversation, and shows no unread state or count.
- Topics are listed newest activity first, one row each: the opening agent's avatar, the opening post's first line and its time, then channel, author, and reply count. Channel filters narrow the list. An open topic shows its posts oldest first with author, named recipients, and time; a wide panel shows the list and the open topic side by side.
- In a board tool row, the channel name, or the title of a topic read, links to that topic: it opens the Board tab on it and marks the post the row refers to. The row itself still expands, and agent names still open the agent.
- The tab reloads when it opens and on `subagent/board/changed` for the thread's root. Without `capabilities.subAgentSessions` it is unavailable and board names are not links.

**Summary**

- The thread header carries a Summary toggle beside the Detail Panel toggle. When the conversation leaves room beside its reading column, the Summary is a card pinned there; the toggle pins and unpins it, and the choice persists across restarts (pinned by default). Without room, the toggle opens the Summary as a popover under the button that closes on Escape or an outside click.
- The Summary lists, for the active thread: its plan or progress (opens Plan); its subagents with working and finished counts (opens Subagents); running workflows and background terminals, each with Stop, revealing the call that started them; automations that target the thread (opens the automation); the files, skills, MCP tools, and websites it used, plus web search, three at a time with View all (files, websites, and skills open); its context window usage; and its environment: the changed-line totals (opens Changes) and the workspace's project actions. With nothing to show it says so.
- The Summary is Core content of the `conversation.aside.trailing` seat ([Desktop Extensions](../architecture/desktop-plugins.md)), so a Desktop extension can add to, wrap, or replace it there.

**Tool rows**

- Tool output stays readable in-thread and distinguishable from conversational text.
- Consecutive tool calls share one collapsed row that names what its calls did, one phrase per kind of work, without counts. Commentary, user messages, subagent ending rows, and calls with a surface of their own (subagents, stopped tasks, channel messages, plans, automations, workflow launches, answered questions, live MCP App Views, plugin-rendered tools) end a group. Tool output images stay out of the group: the calls that produced them remain in it, and one Viewed an image/Viewed N images disclosure sits below the run. A running tool row shows its shimmering label and, for file edits, its diff stats; it never shows an elapsed timer, which belongs to the turn activity line. Reasoning, background command and workflow notifications, and assistant messages without text never end a group, and to-do updates are not shown. While the turn runs, the trailing group names the call in progress; the Thinking status lives only in the turn's working line. Rows never expand on their own. A failed call stays in its group and shows its failure in the expanded output.
- Desktop and the mobile client decide rendering, grouping, and labels only from the trusted presentation (`CoreNative` source, presentation id, and operation), never from tool names, so a renamed server tool keeps its rendering and an untrusted tool cannot borrow one. A presentation lacking an operation renders with its family's generic labels. Desktop's own runtime thread tools ([5.14](#514-desktop-runtime-thread-tools)), Code Mode, and the Dynamic Workflow launch are identified by name and source instead.
- `TaskStop` (`core.task`/`stop`) renders as one row naming what stopped (background terminal, subagent, or workflow) and its label; it expands only to show a subagent's last message.
- `SendChannelMessage` (`core.channel`) renders as one row naming the destination chat; expanding shows the sent text and attachments; a failure keeps the row and shows the error.
- A file tool call whose presentation options carry `memory` renders as memory recall or update rather than a file operation, following [Memory](../features/memory.md#8-presentation). `MEMORY.md` is not counted as a memory file.
- A file tool call whose presentation options carry `skill` renders as reading, creating, or updating the Skill named by `skillName` rather than as a file operation, following [Skills](../architecture/skills.md#51-presentation). The label carries the Skill name as a Skill reference chip that opens the Skill's detail; a create or update expands to its diff like a file edit, and grouped rows name the Skill work once.
- Completed `RequestUserInput` results render as a question-to-answer list, not raw JSON.
- `CodeMode` and `CodeModeWait` calls and their results are not rendered; the calls a script makes render as ordinary items in time order ([Code Mode](../features/code-mode.md#10-session-projection-and-presentation)). `SendUserMessageAsync` calls are not rendered either; the asynchronous message itself shows as a message. The mobile client follows the same rules.

**Shell and background terminals**

- Desktop declares `backgroundTerminals = true` and uses `terminal/*` notifications as its primary live shell output; `commandExecution` is the persisted projection and fallback.
- Shell rows stay collapsed by default; expanding shows live output while the command runs. Shell output is read-only from Desktop.
- A shell call whose tool result returned while its process keeps running stays live, keeps appending output, and settles on `terminal/completed` or a late `item/completed`, even after its turn ended. After the turn ends it is labeled as a started background terminal; a later cancellation is labeled as stopped. The row has no stop control.
- The thread Summary's **Background tasks** section lists running and paused Workflow runs from `workflow/run/list`, reread through `workflow/run/read` on `workflow/run/updated`, and running background terminals from `terminal/list`. Terminal lifecycle notifications are the primary live source; correlated native `commandExecution` completion items, trusted shell structured results, and `terminal/stop` response snapshots also settle terminal state, without requiring another completion notification. Direct `Exec` results have no structured terminal data, so their execution completion items supply the fallback during both live updates and history loading; a returned tool result alone never implies process exit. A terminal is listed once the `Exec` call that started it has returned while its process remains running; while that call still waits, the command shows only in the conversation. The list is reconciled when the active turn ends, the connection recovers, or a cached Summary reopens, without reloading all history or polling. Cache and request isolation use the Main-owned foreground connection's project id, not the active thread's effective working directory, so switching worktrees does not invalidate background updates. Newer live updates survive an in-flight list read, and an observed terminal end cannot be revived by an older running snapshot.
- A Workflow row stops its run through `workflow/run/stop`; a terminal row's stop-all control calls `terminal/stop` for every listed terminal, applies each successful response immediately and waits for all requests to settle. Successful stops need no additional list read; failures trigger reconciliation before being reported. Partial failures retain tasks not known to have ended, report failure, and allow retry. Both stop controls report failure, and selecting a row reveals the call that started it. The background-task count uses the same reconciled state as the list.
- The Summary's **Sources** lists only what the conversation can open again: files and images the user attached or referenced, skills the user referenced or the agent loaded, MCP servers used, fetched web pages, and web search. Files the agent read on its own are not sources. Task notifications and mailbox deliveries contribute none. An attached image shows its thumbnail, and selecting a source opens it: a file in the file viewer, a skill in Skills, a web page in the browser.
- A thread's Summary state stays live after the Summary is hidden, for the few most recently viewed threads, so reopening it shows current content at once. On first open the Summary shows only its header until its initial reads settle.
- Successful `WriteStdin` calls have no row of their own; their input appears on the originating shell row as further command lines, interpreted as a terminal would. Failed calls keep their row. Empty polls are not shown.
- Output of a command in a background thread keeps updating without forcing focus.

**General**

- A fullscreen overlay covering an embedded native view blocks that view for its whole lifetime; the native view is restored only after the last blocker closes. Ordinary menus and popovers do not block.
- `CreateThread` offers and proposals ([5.14](#514-desktop-runtime-thread-tools)) render as task cards at the position of their calls, in place of the tool rows; a Turn's proposals share one card, which stays outside the collapsed Turn summary. Read-only transcripts show no task cards. An offer card asks whether to start the task in its own chat and answers the waiting call; proposal cards start, dismiss, or keep a proposal here. Options on a card opens the Welcome composer's permission, model, and location controls for the new chat. Answered cards collapse into their outcome; a started offer leaves only the created-chat card, and a started proposal becomes a row that opens its chat.
- The created-chat card at the end of a Turn shimmers the chat's name while that chat has a running Turn.
- Durable user-actionable result cards and available interactive Views stay outside collapsed Turn summaries. The latest successful Dynamic Workflow launch in a Turn is such a result and appears before the final assistant message; failed launches are ordinary tool history.
- The client may reveal related context automatically when new changes or plans appear, based on relevance.

#### 5.8.1 Trusted Local Renderers

Desktop consumes the exact `PresentationId` projected by the server. An active Desktop extension renderer for that id takes precedence, then the Core renderer, then the generic tool card. Tool payloads never provide module paths or executable code, and an unavailable id falls back without changing tool execution authority. Desktop resolves renderers in one place, and pinning, grouping, labels, and render plans consume that result. The authority and audience contract is defined by [Tool Architecture Section 14.1](../architecture/tools-architecture.md#141-trusted-local-renderer-registry).

#### 5.8.2 MCP Apps Interactive Tool Views

Desktop hosts stable MCP Apps `2026-01-26` Views through the standard AppBridge lifecycle (initialize/initialized, ping, notifications, teardown). Interactive UI is optional: ineligible items, unsupported clients, offline resources, initialization failures, and revoked Views render the tool result's text fallback.

- Desktop opens a View for a terminal `mcpToolCall` whose `mcpAppResourceUri` is a `ui://` URI, via `mcpApp/view/open`, then connects the bridge, loads the isolated sandbox proxy, delivers the validated resource, waits for View initialization, and supplies tool input and result. Each stage has a bounded wait and one teardown path. History and reconnect create a new View on demand; no previous handle or bridge state is restored.
- Opening is independent of tool success: Desktop opens the View even for a failed result and delivers the original content and `isError` unchanged. When `view/open` rejects the item, Desktop renders the tool result fallback.
- Inline and fullscreen modes share one handle; picture-in-picture and persisted widget state are not supported. Host context (theme, locale, time zone, dimensions, display mode, CSS variables) and size changes are rate-limited. Reported inline width describes the iframe viewport, so applying it is idempotent; fullscreen ignores resize requests.
- The View runs in an opaque-origin sandbox with CSP, navigation, permission, and capability restrictions. It has no Electron, Node, filesystem, shell, parent DOM, generic IPC, undeclared network, or cross-server authority; camera, microphone, geolocation, and clipboard-write remain denied. It may use same-server app-visible tools and resources, logging, `ui/message`, and `ui/update-model-context` only through the handle-bound bridge, and tool calls still pass server authority, policy, approval, hooks, and limits. Bridge messages and View logs are size- and rate-limited.
- `ui/open-link` URLs are validated and normalized by AppServer before the trusted shell opens them. Offline, revoked, and closed notifications tear down the View and restore fallback.

Host-frame appearance follows [Desktop DESIGN](../architecture/DESIGN.md#interactive-tool-ui); methods, DTOs, limits, and errors are defined by [AppServer Protocol Section 22.10](../protocols/appserver-protocol.md#2210-mcp-apps-opaque-view-methods).

#### 5.8.3 Assistant Visualizations

Reference grammar, authority, workspace ownership, updates, and cleanup are defined by [Tool Architecture Section 14.2](../architecture/tools-architecture.md#142-assistant-visualization-boundary); methods and errors by [AppServer Protocol Section 22.10A](../protocols/appserver-protocol.md#2210a-visualization-views). The author-facing runtime names are documented by the built-in visualize skill.

**Recognition and layout**

- Desktop recognizes visualization references only on their own lines in completed assistant messages, outside fenced code; invalid or inline references stay Markdown. Multiple references keep source order with surrounding Markdown. Streaming messages never mount a possibly incomplete reference. The reference `title` is the View's accessible name.
- A `"mode":"wide"` View may break out of the text column into free conversation space, never into lanes occupied by other conversation surfaces; otherwise Views keep the column width.

**Loading and binding**

- Historical Views load only when near the viewport; before that Desktop reserves a placeholder and sends no request. Loaded Views stay mounted when scrolled away.
- On a new connection, the first View opened for a thread resumes that thread first; concurrent opens share one resume, and connection reset clears the binding. `thread/read`, subscription, and thread-list loading never resume threads or open Views.
- When a newly completed message references a path with loaded Views in the same thread, those Views reopen to show the current file.
- Unmounting, switching threads, or disconnecting closes the handle; a handle returned after cancellation is closed without mounting. Load failure shows an unavailable fallback with Retry.

**Sandbox runtime**

- The fragment runs in a dedicated opaque-origin sandbox and receives only readiness, resize, theme/context updates, and a frozen `window.openai` exposing `widgetState`, `setWidgetState`, `sendFollowUpMessage`, `displayMode`, `maxHeight`, and `requestDisplayMode`.
- Desktop injects the active theme as visualization tokens and keeps them current without reloading the View, so runtime-provided controls match Desktop controls. The View reports its content height within host bounds. Token roles follow [Desktop DESIGN](../architecture/DESIGN.md#visualization).
- `sendFollowUpMessage` requires a trusted Desktop confirmation showing the read-only prompt and whether View context is included; only confirmation sends.
- `widgetState` starts from the snapshot returned by `visualization/view/open`. `setWidgetState` updates the View immediately, saves via `visualization/view/widgetState/update`, and resolves on acknowledgement; invalid or oversized values reject. Acknowledged saves, and `visualization/widgetState/updated` from other clients, propagate to every mounted View of the same thread and path.
- Color inputs and single-choice selects never open platform pickers; Desktop opens its own picker or menu anchored to the control and relays values back with standard `input`/`change` events. One picker per View, requiring recent user activation.
- The runtime provides tooltips, tabs, a day-schedule element, a variant carousel, and the `Tweak` design-controls helper. Carousel navigation is local and never persisted or sent to the model.
- `Tweak` groups describe bounded controls; Desktop renders the Design controls panel only when the user toggles it. Edits are validated by the View, shared in memory across mounted Views of the same path, never persisted, and reach the model only through **Add to message**, which adds one `visualizationChanges` context per path to the composer. Nothing is sent until the user sends.
- The first uncaught error in a View shows a quiet notice with an action that fills (never sends) a fix request in the composer.
- A View never navigates. `http`/`https` links open in the system browser after a trusted confirmation showing the full URL; other targets do nothing. `blob:`/`data:` downloads go through the native save dialog, with a size limit. Bridge requests share a per-View rate limit.

**Fullscreen and copy**

- Every loaded View can open a fullscreen viewer that pages through all visualization references in the conversation. The viewer opens its own handle so the inline View keeps running; shared widget state keeps both consistent. Keys reach the View first; the viewer handles close and paging only when the View does not consume them.
- Each View keeps one display mode for its lifetime. `requestDisplayMode` grants fullscreen from an inline View only with recent user activation; `"inline"` from a fullscreen View closes the viewer.
- Desktop can copy a View's current rendered state to the clipboard as a cropped image, excluding host chrome and surrounding content.

#### 5.8.4 Generative UI in Assistant Messages

Desktop renders each top-level `dotcraft-ui` fenced block in an assistant message as a Generative UI block. One splitter turns a message into Markdown, visualization references (5.8.3), and Generative UI blocks in source order. Blocks share one hidden runner per window, carry out accepted host actions in the thread that owns them, are locked while their message streams or turn runs and in read-only transcripts, and, when the server advertises `generativeUi`, read and save view state through `generativeUi/state/read` and `generativeUi/state/update` and apply `generativeUi/state/updated` from other clients. Saved state is client view data and never sent to the model.

### 5.9 Interrupt a Running Turn

1. User requests interruption while a turn is running.
2. Client calls `turn/interrupt`, immediately marks the Turn as stopping, disables duplicate requests, and keeps the running state visible until the protocol outcome arrives.
3. When `turn/cancelled` arrives, the thread returns to a safe idle state. `turn/completed` and `turn/failed` also clear a pending stopping state.

If the request fails before being accepted, Desktop clears the stopping state and presents the failure.

### 5.10 Archive and Delete

- Archived threads remain readable but not turn-capable. A dedicated archived-thread surface supports browsing and restore; restoring does not force navigation.
- Archiving an idle conversation is immediate with Undo. Archiving a running or waiting conversation requires a stop-and-archive confirmation; dismissing it leaves the work running. This check includes loaded descendants: completed children do not keep an idle parent marked as running. Project-wide archive retains its batch confirmation and uses the current parent and descendant states to choose whether stopping is required. Confirming interrupts the current turn first, and interruption failure prevents the archive. Secondary workspace rows route the operation to their owning workspace and do not use another workspace's activity indicators.
- Runtime notifications replace the cached snapshot atomically across thread summaries, the active thread, and runtime indicators. Missing active-turn identifiers and timestamps clear their previous values; waiting and blocking-maintenance states remain distinct from an executing Turn. Archive decisions do not require a list refresh after an idle notification.
- Deleted threads disappear once deletion is confirmed.
- If a thread is archived or deleted elsewhere while open locally, the user sees the new state immediately and loses only the actions that are no longer valid.

### 5.11 Cross-Channel Visibility

- Desktop requests workspace-scoped thread discovery for foreground, secondary-project, archived, and Desktop-tool thread lists.
- Every non-internal thread whose state workspace exactly matches the selected workspace is visible regardless of origin channel, user id, or channel context, including automations, App Binding origins, and unknown origins.
- App Binding origin declarations provide attribution and branding; they are not a visibility allowlist.
- Origin may be shown when useful; unsupported actions are disabled rather than failing; read and resume follow server capabilities and thread status.

### 5.11.1 Thread Fork and Worktree Handoff

**Fork**

- Fork is offered from the thread header, thread list context menu, and assistant response actions.
- Local fork calls `thread/fork`; new-worktree fork calls `worktree/createAndFork`. Assistant-response fork sends a `forkPoint` for the clicked turn or item. Success selects the returned thread; failure shows recoverable feedback without switching threads. Unavailable modes are disabled.
- A `systemNotice.kind = "forked"` history item renders as an informational divider where inherited history ends.

**Worktree execution**

- `thread.effectiveWorkspacePath` is the file, shell, editor, and Git root for the selected thread. Thread list, provider settings, skill/plugin management, app bindings, memory, and workspace configuration stay scoped to the main workspace.
- Main-workspace file and Git surfaces hide `.craft/worktrees/**`; worktree-bound surfaces operate inside the worktree.
- Git status and branch detection run Git scoped to `effectiveWorkspacePath` rather than parsing `.git/HEAD`, because linked worktrees use `.git` files.
- Branch state is cached per effective Git directory and shared across composers; a refresh keeps the previous snapshot visible until it settles.
- For a restored or newly selected local workspace, the main UI is revealed only after both the AppServer connection and the main Git branch probe settle. Remote workspaces count as settled immediately.

**Composer context row**

- The composer has a context row outside the editable body: on Welcome, project picker, Run on ([§6.11](#611-satellites)), work location, branch or changelist control, then provider subscription status; on a thread, the same without the project picker. It renders whenever any control applies.
- On a thread, when the current branch has a pull request in the workspace's GitHub `origin`, the row shows its number, state, and checks. Desktop looks it up by head branch through the local GitHub CLI credentials, from the trusted host process only, preferring an open pull request; it refreshes while the window is focused and the pull request is open and stops once merged or closed. Missing CLI, no `origin`, no pull request, or a failed lookup show nothing. Desktop never creates or merges pull requests from this control.
- Source control is provider-aware. Git workspaces show the branch/worktree selector. Perforce workspaces show a changelist selector backed by `sourceControl/changelist/list`, `sourceControl/threadTarget/update`, and `sourceControl/changelist/create`. Non-VCS workspaces show no selector.
- On a thread, the changelist selector updates the server-side thread target and relies on `thread/updated` for other clients. On Welcome it is a pre-selection on the foreground workspace, applied as the new thread's target after the thread is created; `default` applies nothing.
- Git worktree and branch controls are hidden or disabled for remote workspaces, missing capabilities, non-Git directories, and Perforce workspaces. Perforce controls remain available in remote workspaces because all Perforce operations run through AppServer.

**Handoff**

- On Welcome, `Work locally` starts a normal local thread; `New worktree` calls `worktree/createAndStart`, with the branch selector choosing `baseRef` without switching the local checkout.
- In a local project, new-worktree creation (Welcome, fork into a new worktree) sends the project's worktree environment as `environmentPath` with an `operationId`; Welcome shows the environment selector only in worktree mode, never as a disabled control. While it runs, the conversation area shows the creation steps and setup output ([Local Environments §8.2](../features/local-environments.md#82-when-it-runs)), with Cancel and Don't use worktree, and the first message waits. Setup failure offers Retry (`worktree/runSetup`), Continue anyway, Edit environment, and Fix with agent ([Local Environments §8](../features/local-environments.md#8-setup)).
- A `cleanupFailed` from handoff to local is shown in the handoff dialog with the output tail and changes no UI state. A `WorktreeInUse` says another program, such as a terminal, is using the worktree folder and to close it and try again.
- On a local thread, handoff to a worktree confirms first and calls `thread/worktree/handoff` with `mode = "worktree"`, sending the current branch as `baseRef`. The dialog offers a new worktree or any `worktree/list` entry no thread is bound to; choosing one sends its `path` and keeps its branch. For a new worktree the branch name field is optional; left empty, the server allocates one. The dialog says uncommitted changes move with the thread.
- On a worktree thread, handoff to local confirms and calls it with `mode = "local"`. The dialog names the branch the local checkout will switch to. A `WorktreeHandoffConflict` lists `conflictPaths` as local changes to commit or stash first and changes no UI state.
- Handoff is unavailable while the thread has queued input and says why.
- The request carries an `operationId`; the dialog lists the `thread/worktree/handoffProgress` steps for it as they arrive and omits skipped ones.
- When the agent moves its thread with a worktree tool, the `thread/updated` header updates the sidebar worktree icon and effective roots like a client handoff.

**Worktree settings**

- Settings › Worktrees › General, shown for Git workspaces, edits `Worktrees.BranchPrefix` and lists `worktree/list` entries with branch, bound thread (or none), and whether each has uncommitted changes or commits beyond its base.
- Remove calls `worktree/remove`. An entry with uncommitted changes or commits beyond its base asks for confirmation first and then sends `discardChanges = true`. An entry bound to an Active thread offers no removal. A `cleanupFailed` shows the output tail and offers Remove anyway (`skipCleanup = true`).
- Branch checkout and create controls operate on the effective Git directory.
- While a thread is running, waiting, or in blocking maintenance, the handoff confirmation disables the final action and explains why.

### 5.11.2 Side Chat

A side chat answers a quick question beside a thread without interrupting it. It is a hidden ephemeral fork created with `thread/fork.sideChat` ([AppServer §4.2.1](../protocols/appserver-protocol.md#421-threadfork)) and shown as a viewer panel tab of its parent. Desktop offers it when `capabilities.sideChat = true` for a non-archived top-level thread with at least one Turn.

**Entry points**: the thread header and a keyboard shortcut; the viewer panel; `/side` (empty) or `/side <question>` (sends the question with the composer's attachments and context as the first message, clearing the composer only once the side chat opens); selected assistant text (added as context to the latest side chat's composer without sending); and a queued follow-up (moved out of the queue and sent as a new side chat's first message, returned to the main composer on failure).

**Panel**

- Each side chat is a numbered tab that stays open while the user visits other threads. Inherited history is hidden, so the transcript starts empty.
- The composer omits controls that act on the parent thread (mode, goal, profile, model, permissions, thread commands); model, mode, and permissions follow the parent at fork time. `/side` is unavailable inside. A message sent while the side chat runs steers its running Turn.
- Stop and Escape interrupt only the side chat's Turn when focus is there; elsewhere they keep acting on the main Turn.
- Approvals and user-input requests from a side chat appear in its own composer, never the main conversation. Tabs indicate needs-response, running, and unread states.

**Lifecycle**

- Closing a tab interrupts its Turn and deletes the fork with `thread/delete`, confirming first when it has messages.
- A side chat uses the AppServer connection it was opened on for every request, so it is deleted on its own server even after a workspace switch.
- Side chats are not restored after restart. When the connection is lost, open side chats become read-only and offer a new one.
- Archiving or deleting the parent deletes its side chats on the server; Desktop closes their tabs on `thread/deleted`.
- Nothing from a side chat enters the main thread automatically.

### 5.12 Manage Thread Goal

Desktop goal behavior is defined by [Goal Design, Client UX Contract](../features/goal.md#9-client-ux-contract). At the Desktop level:

- Goals are a conversation control, not an AppServer custom command. When `capabilities.threadGoals = true`, the slash surface includes a system Goal action; the server owns the goal runtime and Desktop only controls and displays it.
- Active goal state is summarized in the composer footer. Direct `/goal` submissions are handled locally and translated to `thread/goal/*` requests. Creating a goal from Welcome may create a thread and set the goal without starting a turn. Replacing a different non-complete objective requires confirmation.
- Desktop never starts goal continuation turns; it observes server `turn/*` and `item/*` notifications and updates goal UI from `thread/goal/updated` and `thread/goal/cleared`.

**Non-typed user messages**

- User messages with `triggerKind = "goal"` render a visible goal source marker.
- Messages with `triggerKind = "subagentFollowupTask"` or `"subagentInput"` render a source marker indicating they came from another thread; `triggerRefId` is an agent path, not a thread id. Messages with `deliveryMode = "subagentMailbox"` or `triggerKind = "subagentMailbox"` are internal model-facing notifications and never render as user bubbles.
- Background task notifications (`triggerKind = "terminal"` or `"subagent"`, and `"workflow"` with `triggerStatus`) are model-facing: they appear as neither a user bubble, turn navigation entry, queued-input entry, nor composer history, and reordering the queue keeps them in place. A turn started by one marks its activity boundary with the task kind (for a subagent, its avatar) and how it ended (from `triggerStatus`), with an accessible control that opens the task named by `triggerRefId`: a subagent in the Subagents panel, a workflow in its run tab, and a background command by revealing the call that started it. A subagent notification drained into a running turn shows a row at its place naming the ended subagent (see `specs/features/subagents.md`); a background command or workflow notification drained into a running turn shows nothing, because the task's own call already shows how it ended.
- Dynamic Workflow continuation messages (`triggerKind = "workflow"` without `triggerStatus`) render an origin marker labeled by `triggerLabel` that opens the run named by `triggerRefId`.

**Conversation reveal**

Revealing a tool call finds its item by call id; for a task-started turn it finds the starting call (by terminal session id, the `SpawnAgent` result's agent path, or the `Workflow` result's run id), loading older history until the item's turn is present. It then opens every enclosing disclosure, scrolls the item into view, and highlights it briefly (respecting reduced motion). An item no longer in the thread reports that it is gone. An expanded command keeps streaming output.

### 5.13 Composer System Actions

The slash surface lists Desktop-owned system actions above custom Commands and Skills. Selecting one clears the slash query. Each group matches its labels, ids, and aliases (not descriptions): query characters match in order, each skip lands on a word start, and a mid-word start needs three characters. Within a group, prefixes rank above word starts above mid-word matches, then shorter names first.

- **Init** appears only when command management is available and the workspace `command/list` result contains `/init` (AppServer omits it when root `AGENTS.md` or `AGENTS.override.md` exists; Desktop never inspects these files). Selecting it, or submitting `/init`, calls `command/execute` and starts a normal turn with the returned `expandedPrompt`. Command availability reloads after each turn ends.
- **Reload skills** appears only when command management is available, the composer has a thread, and the workspace `command/list` result contains `/reload-skills`. Selecting it, or submitting `/reload-skills`, calls `command/execute` with that command, reports the result, and refreshes the skills list; it never starts an agent turn.
- **Plan mode** is always shown and toggles mode through the same path as its keyboard shortcut, calling `thread/mode/set`. On Welcome it updates the pending mode without starting a thread.
- **Compact** appears only when `capabilities.manualCompaction = true`, the thread has a turn, and no turn is running or awaiting approval. It calls `thread/compact/start` with a long client-side wait; a client timeout does not imply server failure.
- Direct `/plan`, `/agent`, `/compact`, and `/reload-skills` are handled locally and never start an agent turn; `/compact` reports unavailability when its conditions are not met.

**Follow-up while running**

- A Desktop-local, application-wide `followUpQueueMode` preference (`queue` or `steer`, default `steer`) chooses how a non-empty draft is submitted while a regular Turn runs: `turn/steer` with the observed active Turn id, or `turn/enqueue`. Enter and the send button share the decision and the button announces it. Saving the preference affects later submissions only; a failed save keeps the previous choice.
- With `steer` selected and `capabilities.workspaceConfigManagement`, a workspace `InstantInterruptEnabled` toggle shows the server's effective value, writes through `config/value/write`, refreshes on `config/changed`, and restores the previous value on failure. A running Turn keeps the value it started with. When on, a steer interrupts the model's current reply, including its thinking, and restarts it with the message.
- Queue entries can be steered, un-steered, removed, edited, or sent in a side chat. Editing moves an entry into the composer with its text, references, and attachments and remembers its neighbors; while it is being edited, a submission during a running Turn always enqueues, and the entry returns to its original position.
- When the thread is idle, submission uses `turn/start`. During active maintenance (`maintenanceKind = "compacting"`), submission uses `turn/enqueue`, and the empty-draft stop control calls `thread/maintenance/interrupt`.

**Optimistic submission**

- Desktop clears the composer and echoes the submission locally before issuing `turn/start`, `turn/steer`, or `turn/enqueue` (or, on Welcome, thread creation per [5.2](#52-start-a-new-conversation)), without waiting for the response. `turn/start` echoes a user message; steer and enqueue echo queue entries (`guidancePending`, `queued`) that enter the conversation only when the server admits them. A local echo carries the submission's `clientUserMessageId` and is replaced by the matching server entry rather than shown twice.
- A failed submission removes its echo and restores the content: the whole submission if the composer is empty, otherwise the submitted text above the current text. Desktop reports the failure and never resends automatically; `clientUserMessageId` is provenance only.
- A `turn/steer` failure because the Turn changed or ended restores the submission without retrying elsewhere. A `turn/start` rejected as busy because of a maintenance race is retried once through `turn/enqueue`.

**Compaction status and context usage**

- If manual compaction times out while maintenance still reports compacting, Desktop keeps the busy state and stop control and waits for the terminal `system/event`. Skipped or failed outcomes, and terminal `compactFailed`/`compactCancelled` events, show their message in the compact status surface.
- Selecting the context ring shows the Summary with its Context section open. The section's row follows the same `contextUsage` as the ring. Opened, it reads `thread/contextUsage/read` and reads it again whenever `contextUsage` changes while no turn runs; during a turn, tokens beyond the measured categories show as added since the last update. Its Compact button runs the same manual compaction as `/compact`.
- The context ring updates from RPC responses that include `contextUsage`, from terminal compaction `system/event.contextUsage`, and from a compacted `SystemNotice`'s after-values, but never from compaction start events, whose counts are estimates.

### 5.13.1 Continue in a Channel Chat

A conversation can be continued in a connected channel chat through a channel binding ([App Binding](../protocols/app-binding.md)).

- Only an existing thread's composer offers it. When `capabilities.channelStatus = true`, Desktop caches `channel/status` per workspace and refreshes it when stale or when capabilities change, never per render.
- The slash surface shows one `/bind <channel>` action per running `social` or `external` channel. Selecting it or submitting `/bind <channel>` calls `thread/channelBindings/request/create` and never starts a turn. A bare `/bind` binds the only running channel; otherwise it suggests an example. Unknown or stopped channels report not connected.
- The returned code is client-only, unpersisted thread state. A pending card shows the command to send in the channel, remaining time, copy, and cancel (which revokes the pending binding). Before a new request for the same channel, Desktop revokes a still-connecting one so the server does not reject a duplicate.
- The card clears when `thread/appBindings/list` reports the binding settled: once active, the server's persistent channel notice takes over; on expiry Desktop revokes it and suggests `/bind` again.
- Active bindings show a chip beside the thread title. Stopping continuation, from the header or `/unbind [<channel>]`, calls `thread/appBindings/revoke`; the server appends an unbound notice.

### 5.14 Desktop Runtime Thread Tools

Desktop exposes a client-owned thread-management profile as Runtime Dynamic Tools declared on `thread/start`, `worktree/createAndStart`, and `thread/resume`.

**Declaration**

- Tools use the `desktop` namespace and PascalCase names: `CreateThread`, `DismissProposal`, `ListThreads`, `ReadThread`, `SendMessageToThread`, `SetThreadTitle`, `SetThreadArchived`, and `SetThreadPinned`. No other model-visible aliases exist.
- They are declared with `deferLoading = true` so they are discoverable on demand, except on runtimes without deferred-tool discovery.
- Whenever the tools are declared, Desktop also declares `additionalContext["desktop.threadCoordination"]` (`kind = "application"`): a hint to search for the relevant thread tool before background thread management, plus the rules for starting other chats: how to classify the user's latest message, when to offer, propose, or start a chat, and that loading the deferred `CreateThread` does not count as the turn's first call.
- Declarations are stable across Agent/Plan switches; handlers enforce mode policy.
- Desktop declares the tools only when it can handle their `item/tool/call` requests on the active transport. On reconnect with `capabilities.dynamicToolRebind = true`, Desktop re-declares the same specs and context when resuming; without rebind, pending calls fail through the normal unavailable path.

**Behavior**

- Tools call ordinary AppServer methods. `SetThreadPinned` only updates Desktop settings; proposals and offers are Desktop-local state.
- `ListThreads` calls `thread/list` with optional `query`, `limit` (default 20, max 100), `cursor`, and `includeArchived`, returning a page summary with `nextCursor` and `totalMatched`.
- `ReadThread` reads the header and newest complete Turns without resuming, subscribing, or selecting the thread. Arguments: `threadId`, optional Turn `cursor`, `turnLimit` (default 1, 1–10), `includeOutputs` (default false), `maxOutputCharsPerItem` (default 2,000, 0–20,000); invalid arguments fail. Turns are fully hydrated or the call fails. The result `{ schemaVersion: 1, thread, page, turns }` appears in both structured and text content: full user/assistant text and typed Item metadata, media as references, materialized input snapshots ahead of `contextRef`, optional outputs with truncation metadata, newest-first pagination, and a bounded summary of queued inputs with their total count.
- `SendMessageToThread` sends a turn without stealing focus. If the target is running, waiting, or in blocking maintenance, it uses `turn/enqueue`, or fails as busy when unavailable.
- `SetThreadTitle` and `SetThreadArchived` map to `thread/rename`, `thread/archive`, and `thread/unarchive`; visible state updates through normal broadcasts.
- `SetThreadPinned` rejects archived and subagent threads when pinning and updates the project-scoped pin preference so the sidebar updates immediately.
- Optional `reasoningEffort` on send is applied through thread configuration (`thread/config/update`) and affects queued and future Turns, not the active Turn. Unsupported options fail rather than being ignored.
- Calls render as ordinary dynamic tool activity and are non-modal unless an underlying call triggers approval or user input, or `CreateThread` shows an offer. Background-changed threads update sidebar indicators without forcing navigation.
- Results distinguish started from queued work. Failures carry an English fallback and a stable code: `UnsupportedTool`, `UnsupportedOption`, `InvalidArguments`, `ThreadNotFound`, `ThreadArchived`, `ThreadBusy`, `ThreadManagementUnavailable`, `TargetUnsupported`, or `AppServerRequestFailed`. Outcomes the agent must act on, such as an offer's answer, a card that was not shown, or a refused proposal start, are successful results whose text says what to do next.

**Starting chats: `CreateThread`**

- Arguments: `initiation` (`userAsked`, `offer`, or `ownInitiative`; required unless `proposalId`), `title` (under 60 characters; required unless `proposalId`; becomes the new chat's title), `prompt` (the brief; required for `userAsked` and `ownInitiative`, absent for `offer`), `background` (context for someone who has not seen this conversation), `proposalId`, `model`, and `reasoningEffort`.
- A new chat starts in the current workspace with `spawnedFromThreadId` set to the calling thread, recorded as a sibling source, never a subagent relationship; worktree starts are not linked because `worktree/createAndStart` has no such field. Its first message is the brief, preceded by the background when given. The user's active conversation does not change.
- `userAsked` creates the chat and starts its first Turn now. The result keeps the `{ thread, turn, started }` shape, so the conversation shows the created-chat card.
- `ownInitiative` stores a proposal on the calling thread and returns at once with its `taskId`. Nothing runs until the user starts it from its card, or tells the agent to, which then calls `CreateThread` with only `proposalId`.
- `offer` is for a task the user's latest message names. It stores an offer, shows the card, and keeps the `item/tool/call` open without a timeout ([AppServer §11.3](../protocols/appserver-protocol.md#113-itemtoolcall)) until the offer is resolved:
  - **New chat**: Desktop creates the chat at once with the start options chosen on the card and no first Turn. The result tells the agent to call `CreateThread` again with exactly `proposalId`, `prompt`, and `background`; that call starts the chat's first Turn with that brief.
  - **Do it here**: the result tells the agent to do the task in this chat.
  - **Typed past**: the user sends a new message on that thread from Desktop (`turn/start`, `turn/enqueue`, or `turn/steer`) while the offer still waits for an answer. The offer closes, and the result tells the agent to deal with that message first.
  - **Not started**: New chat was chosen but creating the chat failed, including a worktree whose environment setup failed. The user sees why, and the result tells the agent to do the task here.
  - **Abandoned**: the Turn is interrupted, cancelled, failed, or completed, or Desktop's connection drops or is replaced, before an answer. The card is marked not answered, and the agent does nothing further with it.
  - While an offer waits and the window is not focused, Desktop shows a system notification under the Questions notification switch; clicking it opens the chat. Auto modes never notify.
- `proposalId` starts a waiting proposal exactly as proposed, or sends the brief of an accepted offer. For a proposal, `title`, `prompt`, and `background` may restate the brief. Any other argument refuses the call unless a `prompt` is given, in which case the extra arguments are ignored and the result says so. A proposal that is no longer waiting, expired, withdrawn, or unknown is refused with a result that says why.
- `DismissProposal(taskId, reason?)` withdraws a waiting proposal. Results distinguish withdrawn, already started or handled here, already withdrawn, expired, being started, and unknown ids.

**Who can see the card**

- A card can be seen only when the calling Turn came from a person at a Desktop composer: the Turn's `originChannel` is `dotcraft-desktop` and its user message carries no `triggerKind` (background task notifications, subagent input, workflows, automations, goals, apps). Desktop learns this from the `turn/started` notification it receives for the Turn. When it has not seen one, it reads the Turn from `thread/turns/list` and its first item from `thread/items/list`. A Turn whose origin cannot be read counts as nobody's.
- When nobody can see the card, an `offer` returns a "No card was shown" result telling the agent to do the task in this chat, and `ownInitiative` returns a "Nothing was proposed" result telling the agent not to mention a card.

**Proposal storage**

- Proposals and offers are stored per thread in Desktop's per-user state and survive restarts. A record holds the `task_` + 8 hex id, the tool call id, title, brief, background, default model and reasoning effort, kind (`offer` or `proposal`), state, creation time, and the started chat's id.
- A thread keeps at most 20 records. Adding another removes the oldest, and the result names the waiting proposals that were removed. A waiting proposal expires after 7 days.
- On load, an offer that was still waiting is marked not answered; an accepted offer keeps waiting for its brief; a proposal that was starting and already has its chat is marked started.

**Starting from a card**

- The renderer lists a thread's records and observes their changes, answers an offer (New chat with start options, or Do it here), starts one or several proposals with start options (Start all), dismisses a proposal, and marks a proposal handled here.
- Start options are the work location (`local`, or `worktree` with an optional base ref, branch name, and environment through `worktree/createAndStart`), approval policy (`prompt` or `autoApprove`), model, and reasoning effort or reasoning turned off. Unset options fall back to the proposal's defaults, then the workspace defaults.
- A proposal's Do it here is a renderer action: it sends `Do this task here: “{title}”` through the normal composer path and marks the proposal handled here.

**New task prompt setting**

- The Desktop-local `newTaskPrompt` preference decides what an `offer` does: `ask` (default) shows the card; `newThread` accepts it at once as New chat with default start options, at most 10 times per thread since the user's last message there, after which it asks; `here` returns a "No card was shown" result at once.
- The nobody rule applies first. The offer card's Don't ask again menu sets the preference to `newThread` or `here` and answers the offer the same way.

### 5.14.1 File References from Context Menus

- A file target's Add to chat action inserts the same structured file chip as the composer's `@` file search. Directories do not offer it.
- Pending references queue per originating thread or welcome scope, preserve order, never affect another scope, and wait until that composer's draft is editable.
- Insertion happens at the last editor selection (replacing selected text) or appends, and restores focus.
- Paths inside the composer's workspace are workspace-relative; others stay absolute. Drafts and submissions use normal structured file segments and `fileRef` input parts. Remote-workspace composers keep their restriction on local-file input.

### 5.15 Thread References

A user can point the model at earlier chats of the current workspace from the welcome and thread composers.

- References are available while the composer's thread has the Desktop thread tools: always for a not-yet-created thread, otherwise when `dynamicToolRebind` is available or the thread was started or resumed with them on the current connection. Otherwise the `@` menu offers no chats and thread drops are refused.
- A non-empty `@` query lists a bounded number of chats above file results: sidebar chats whose title matches, then content matches from `thread/search` (`sortKey = "lastActiveAt"`, debounced). Each chat appears once; the current thread and already-mentioned threads are excluded; archived and subagent threads are not offered by title.
- Choosing a chat or dropping a sidebar chat inserts a thread chip that serializes to `[@Title](thread://<threadId>)`; sent messages render it as a chip that opens the thread.
- Submission adds one `threadReferences` context ahead of the prompt, listing each mentioned thread once (excluding the target thread) under a header telling the model to call `ReadThread` first. Rendering and history restoration ignore that context; inline links restore the chips.
- If the draft mentions a thread while the tools are unavailable, submission is refused and the draft kept.

### 5.16 Turn Navigation

A thread with several user messages shows a navigation rail that previews and jumps to any user message, including ones in unloaded history (see restore item 7 in [5.3.1](#531-desktop-thread-restore-pipeline)).

### 5.17 Open in Another App

Desktop opens a local project, folder, or file in another installed app. Remote workspaces offer no open targets.

- **Targets.** Built-in targets cover common editors, IDEs, Git clients, file managers, and terminals for the platform, plus a hidden system default used by "Open with". A target is listed only when installed (found on `PATH` or in known install locations).
- **Custom targets.** User configuration `Desktop.CustomFileHandlers` maps an id (letters, digits, `.`, `_`, `-`; up to 64 characters, starting with a letter or digit) to `{ Label, Icon, Command, Args = [], Input = path | jsonArgument | jsonStdin }`, listed after built-ins as `custom:<id>`. `Command` is an absolute path or a `PATH` name. `path` appends the target path after `Args`; `jsonArgument` appends one JSON argument `{ target, path, location }`; `jsonStdin` writes the same JSON to standard input. `location` is `{ line, column }` or `null`. `Icon` is an absolute image path or `data:image/…` URL. Invalid entries are skipped and logged.
- **Positions.** Opening a file at a position passes it to editors that accept one; other targets open the path only.
- **Default target.** Desktop remembers one global target and one per project root. The effective target is the project's choice, else the global choice, else the first installed target. A choice is remembered only on explicit selection from an "Open in" menu; Settings sets the global choice.
- **Surfaces.** Thread menus open the project root in a chosen target; the file viewer opens the file in the effective target or a chosen one; file-reference menus offer the effective target and "Open with". Load and launch failures are reported.

### 5.18 Project Actions

A project can declare actions — named commands such as Run or Test — that run in a terminal from the conversation. Actions are available for local workspaces. They belong to [local environments](../features/local-environments.md), which also define worktree setup and cleanup.

- **Files.** Environment files, their discovery, and their format are defined in [Local Environments §4](../features/local-environments.md#4-environment-files).
- **Subagents row.** The conversation Summary's Subagents row reads "n working" while any child works or waits, otherwise "n done" counting the finished children, and the whole row takes the secondary text colour once none is working; while some work and some have finished, the finished count follows as secondary text. Its avatars show the working children, otherwise the finished ones. The label changes in place without a transition.
- **Environment section.** The conversation Summary has an Environment section: a Changes row once the conversation changed files (opening the Changes panel) and, in a local workspace, the primary action plus an actions menu ([Local Environments §7](../features/local-environments.md#7-conversation-environment-section)). The conversation environment and run order are remembered per project on this device ([Local Environments §5](../features/local-environments.md#5-selection)).
- **Running.** Running an action opens or focuses the conversation's terminal tab for that environment and action, restarts its shell in the environment root inside the thread's execution workspace, and enters the command in that shell's syntax. The terminal owns the process.
- **Adding.** Adding an action writes the whole environment file (creating `environment.json` in the workspace when none exists) and makes it the most recent action without running it. If the file changed on disk since the dialog opened, saving fails and nothing is written.

## 6. Secondary Flows

### 6.1 Plugins and Skills

The Plugins destination has `Plugins` and `Skills` tabs, shown when `capabilities.pluginManagement` and `capabilities.skillsManagement` respectively are available; otherwise the tab or action is hidden or disabled with a reason.

- Users can browse discovered and installable plugins, inspect details and included tools and skills, install or uninstall plugins, and enable or disable installed plugins.
- Desktop tells a local AppServer where its bundled plugin resources live; without that, uninstalled built-in plugins are not installable catalog entries.
- Plugin install, uninstall, and enablement refresh both plugin and skill state, because plugin-contained skills follow the plugin lifecycle.
- Users can browse installed skills, inspect a skill's content, enable or disable skills when supported, and uninstall user-managed `workspace` and `user` skills. Skills with source `plugin` show attribution and are managed only through the owning plugin.

#### 6.1.1 Plugin Creation and Marketplace Sources

- The browse surface offers refresh, manage, and one create control that gathers every way of getting a plugin: authoring one in a conversation, adding a marketplace, and installing from a local folder.
- Authoring stages a welcome draft that mentions the plugin authoring skill with a starting prompt, degrading to plain text when the skill is absent.
- Adding a marketplace collects a source, optional reference, and optional sparse paths, or a local directory. Fetch failures are reported inline so the user can correct input. After adding, the client shows the resolved source, reference, and revision.
- Browse groups entries by category; a marketplace is a delivery route, not a category. A marketplace filter mode lists marketplace-sourced entries grouped by marketplace with per-group refresh and remove; it is offered only while a marketplace is configured.
- Removing a marketplace does not remove installed plugins, and the confirmation says so.
- Plugin detail lists skills, apps, and other included contributions. Installed skill switches use the same `skills/setEnabled` operation as Skills management; a disabled parent plugin shows its skills off without changing saved skill preferences. Skills are matched by plugin attribution and name; a skill shadowed by another source cannot be toggled from the plugin.
- Installing a plugin, from a catalog or local folder, installs it for every workspace. Enablement can be turned off for the active workspace alone. Uninstalling a user-global plugin removes it everywhere, and the confirmation says so.
- Marketplace controls are hidden without `capabilities.pluginMarketplaces`; marketplace add and local-directory install are unavailable for remote workspaces.
- The catalog loads when the surface opens, after client mutations, and on explicit refresh only.

#### 6.1.2 Plugin App Connection and Conversation Binding

Desktop presents app-principal connection and thread binding as separate workflows, both backed by [App Binding](../protocols/app-binding.md):

- A plugin's Apps section manages workspace-level app connection only: install, connect, reconnect, and disconnect. Disconnect confirms first because revoking the app principal also revokes its thread bindings.
- Connecting an app is the user's decision to use it in the workspace. Every conversation started from Welcome binds each installed, enabled, connected app that requires an external connection; there is no per-conversation picker, and existing conversations' bindings do not change.
- The first message creates and opens the thread; Desktop enables and awaits those bindings, then starts the Turn. A failed binding is reported as a warning and the Turn starts without that app.

#### 6.1.3 Desktop Extensions

Installed and enabled plugins may contribute trusted Desktop modules through the inline `desktop` manifest declaration. Desktop derives entry points and content revision from AppServer plugin discovery rather than hardcoding plugin ids.

- Plugin-provided views appear only while the owning plugin is installed and enabled. If the current view's plugin is disabled, removed, or no longer declares it, Desktop moves to a built-in fallback view.
- Desktop modules load from local installed plugin files or a verified local cache of a remote plugin's Desktop output; Desktop never executes JavaScript from remote URLs.
- A remote AppServer must advertise `desktopPluginArtifacts` for Desktop to download and activate its plugin interfaces. Without it, Desktop skips them and explains the required server update; it never substitutes a local package or resolves a remote path locally.
- Desktop extension code is trusted local renderer code. Installing and enabling the plugin is the local trust decision; remote interfaces additionally require a client-side grant per remote workspace. Bundled and user-installed plugins share one host API and lifecycle.
- App Surface access goes through the trusted host's proxy, which follows the App Surface rules in [App Binding](../protocols/app-binding.md#32-app-surface-registry).
- A failed plugin load shows an error state for that contribution without breaking core conversation workflows.

### 6.2 Automations

The `automations` capability controls the Automations destination; without it the destination is hidden. Loading errors and empty lists are distinct states. Desktop consumes `automation/updated` and `automation/run/updated` and reloads on reconnection.

Creation starts at the Welcome composer by default: Desktop stages an instruction plus the built-in `$automations` skill reference in the Welcome draft. Manual creation and editing are also available, with explicit save and cancel, failed drafts retained, and stale versions rejected. Fields depend on follow-up versus independent execution.

### 6.3 Automation Cards

Trusted `core.automation` cards retain the operation snapshot and open the current definition by id. Creation, update, pause, resume, and completion are distinguished. Manual run acceptance displays queued, not succeeded. Missing or deleted definitions remain understandable as historical snapshots without dead edit controls. The automation prompt stays out of the card.

### 6.4 Automation Run Review

Every result opens by automation id and run id. Follow-ups locate the exact turn in an existing conversation; independent runs open their own conversation. Worktree review belongs to a run, never to a definition's mutable latest-thread pointer.

### 6.5 Model Selection

- With the model catalog capability, the client offers model selection from server-provided values; without it, or when it fails, conversation remains usable and manual model entry stays available (including on `EndpointNotSupported`).
- Welcome catalog requests are scoped to the workspace provider; existing-thread requests are scoped to `thread.configuration.providerId` and do not follow workspace changes.
- One combined picker exposes provider, model, reasoning, and speed with consistent keyboard and menu behavior in Composer, Settings, and Setup. Settings and Setup omit provider choice because the surrounding workflow owns it.
- Welcome atomically persists `providerId` and the complete provider-keyed `providerPreferences` map, then sends the selected mode/model/reasoning/speed/context snapshot (and any explicit approval override) in `thread/start` or `worktree/createAndStart`.
- Existing threads expose no Default. A provider or preference change sends one full `thread/config/update`, never a workspace configuration write, and updates local state only on success. A provider with no remembered model gets its first listed model; if listing is unavailable, the thread is unchanged and the user is directed to provider settings.
- Missing or deleted providers remain visible as missing thread state until the user migrates the thread.

### 6.6 Archived Chats

- An archived-thread surface is reachable from Settings when thread management is available. It follows the main list's workspace identity and visibility rules with archived inclusion.
- It is read-only apart from restore. Restoring removes the thread from the archived list immediately.
- Restores or deletes made elsewhere reconcile automatically.

### 6.7 Settings Surface

**Configuration model**

- Desktop follows the three-tier model: live-apply, subsystem-restart, and process-restart fields. Actions are group-scoped (Apply, Restart, Apply & Restart); there is no page-level Save.
- Configuration controls require `capabilities.workspaceConfigManagement`. Desktop reads effective configuration with `config/read` and writes key paths with `config/value/write` or `config/batchWrite`, for local and remote connections alike; it never reads configuration files. Controls apply optimistically and restore the previous value with an error on failure.
- Edit races: live-apply fields keep local in-flight edits when the echo of the same change arrives; process-restart edits stay local until applied or discarded.

**Providers and models** (when `providerManagement` is available)

- Personal providers, including [model backend](../architecture/model-backend-plugins.md) instances, are listed together with `provider/list` and created, edited, tested (`provider/test`, with no hidden chat requests), and deleted from Settings. An instance whose backend is unavailable stays listed with that problem and keeps its configuration.
- The provider editor writes `Providers.<id>` in the user layer with `config/batchWrite`: an untouched API key is sent as `***` or omitted, delete writes `null`, and per-provider toggles write `Providers.<id>.<Field>`. It rejects ids that already exist and passes the layer's `expectedVersion` to avoid overwriting concurrent edits. `openai` is an ordinary provider id; the server rejects deleting the selected provider.
- Provider credentials and endpoints are personal; workspace saves write `ProviderId` and the selected provider's `ProviderPreferences.<id>` entry. Each preference holds model, reasoning, and speed, and a workspace record atomically overrides the personal one for that provider.
- Main and Subagent model settings use the shared picker. Subagent can inherit (removing its provider-specific record) or be customized starting from the main preference.
- Unsupported model listing remains recoverable with manual entry.
- Web search chooses `Tools.Web.SearchProvider` and shows only the settings that service uses: `Auto` and `Exa` the write-only `Tools.Web.ExaApiKey`, `Bing` and `ChatGPT` nothing, and `DeepSeek` its credential source followed by either a provider choice or the write-only `Tools.Web.DeepSeekApiKey`. The provider choice appears only when more than one DeepSeek provider exists; with none, the row offers switching to a key. `ChatGPT` can be chosen only while a ChatGPT-subscription provider is signed in. Changes take effect from the next turn.

**Connections**

- One Connections page separates connection kinds by direction: **Workspace** (how Desktop reaches the workspace runtime), **Satellites** ([§6.11](#611-satellites)), **Share this PC** (who may run tools on this PC, §6.11), **SSH** ([§6.10](#610-ssh-machines)), and **Phones** ([§6.15](#615-phones)). Each segment owns its own model, list, and primary action and never shares state. Segments whose capability is absent show a setup state rather than disappearing, except Share this PC may be hidden when no Satellite runtime exists on the machine.
- Workspace: Local mode shows Hub-managed AppServer actions including Apply & Restart; Remote mode uses Apply & Connect with validate-before-persist and hides local-only binary and restart controls ([3.1.1](#311-local-and-remote-appserver-ownership)).

**Source control** (when `capabilities.sourceControlManagement` is available)

- The user selects `git`, `perforce`, or `none`; unsupported stored values are rejected, not normalized.
- Perforce connection settings are tested and detected on the AppServer (`sourceControl/test`, `sourceControl/get`) so results reflect the workspace-owning environment; Desktop never runs `p4` locally.
- `sourceControl/update` persists only non-sensitive fields. A binding saved without a passing test in the current edit session is saved with `perforce.online = false`. An offline binding suppresses Git controls but does not enable changelist selection or Checkout.
- Desktop refreshes the binding on `config/changed` for `sourceControl` and whenever the foreground connection changes.
- When `sourceControl/get.capabilities.perforceChangelist = true`, the branch selector becomes a changelist selector and the thread commit action becomes Checkout. Checkout calls `sourceControl/changelist/prepare` and never falls back to Git; a blank description is first filled by `workspace/commitMessage/suggest` with `provider = "perforce"`. The user may target the current changelist, another pending one, or a new one (`target = "default"`). A successful Checkout may move already-opened files into the selected target. Desktop does not expose Perforce submit or shelve, and its wording avoids submit semantics.

**Worktrees**

- Settings › Worktrees has a General tab (worktree settings, [§5.11.1](#5111-thread-fork-and-worktree-handoff)) and an Environments tab for every local project's environments ([Local Environments §6](../features/local-environments.md#6-settings--worktrees)).

**Notifications**

- Task completion notifications (`whenUnfocused` default, `always`, `never`) and on-by-default switches for approval requests and questions are Desktop-local preferences. They gate the Hub tray notification kinds in [Hub Architecture §10](../architecture/hub-architecture.md#10-tray-and-notifications): a waiting approval or question notifies only when no focused Desktop window has that workspace in the foreground, and clicking opens the chat.
- When Hub cannot deliver (non-Hub foreground connections such as Remote, SSH, and Docker, or no tray), the window notifies itself under the same switches while unfocused.
- Each request notifies at most once; a replay after reconnect or thread switch does not notify again.

**New tasks**

- A personal setting chooses `newTaskPrompt` ([5.14](#514-desktop-runtime-thread-tools)): ask each time (default), always start a new chat, or always do it here. A failed save keeps the previous choice.

**Personalization and memory**

- A personal Pet tab configures the default companion ([§6.13](#613-desktop-pet)).
- A workspace Personalization tab groups Conversation, Skills, and Memory settings by available capability; empty groups are hidden.
- The Memory group holds memory enablement (`memoryEnabled`), automatic extraction of missed memories (`Memory.AutoExtract`), Dreams when `capabilities.dreams = true`, and delete-all when `capabilities.memoryManagement = true` (`memory/reset` after confirmation). Memory toggles affect new sessions only ([Memory](../features/memory.md#7-memory-switches)). While memory is disabled, dependent settings show disabled with their stored values and an explanation.
- Dreams controls are hidden without `capabilities.dreams`. Entering Personalization or the Dreams page loads `dreams/status`, refreshed after saves and on `config/changed` for `memory` or `Memory`/`Dreams` keys. Manual runs call `dreams/run`, disable while running, and poll status. Run history uses `dreams/list` and opens Dashboard review links at `dashboardUrl#dreams/run/<runId>`; Desktop shows no memory diffs. See [Dreams](../features/dreams.md#10-desktop-ux-contract).

#### 6.7.1 Agent Import

When `capabilities.extensions.agentImport` is present, Settings shows an Import tab for the current workspace. [Agent import](../features/agent-import.md) defines detection, conversion, scope, and synchronization.

- Opening the tab loads settings, candidates, and history; detection can be refreshed. Sources show importable counts; existing or unsupported entries stay inspectable. Import is disabled while a pass runs.
- The import dialog separates host-wide tools and setup, current project configuration, and chat sessions, supports partial selection, and explains where content applies, never showing configuration secrets.
- Confirming submits the selected item identities, fingerprints, and categories, plus the categories offered, to `import/run`. `import/progress` updates progress; completion refreshes candidates, settings, and history. `import_busy` shows the running pass instead.
- Keep-in-sync applies immediately and can be paused without losing imported content or selections; a workspace opt-out disables it. After the first import the synced categories can be customized (at least one required, optionally including future categories), affecting future syncs only.
- History shows one entry per pass with per-item status, a localized reason, and a link to imported content. Outstanding setup (hook trust, missing MCP environment variables, plugin review) is listed with a direct action. A sync pass that changed nothing stays silent.
- Imported threads arrive through `thread/started` and show their source app; imported history never makes them unread.

#### 6.7.2 Computer Use

Settings has a Computer use tab. Its control list toggles the bundled `computer` plugin (any app) and `chrome` plugin, installing the plugin when missing and disabling without uninstalling; Chrome reports its extension state. An always-allowed applications list shows an empty state when empty, supports removal after confirmation, and gains entries only from always-allow approval answers ([5.6](#56-approval-handling)). On platforms without computer use, only Chrome is offered and the allow list is hidden.

#### 6.7.3 Provider Sign-in

The provider editor chooses one connection type, which sets the API format and authentication method together: OpenAI-Responses API, OpenAI-Legacy API, Anthropic API, ChatGPT subscription, and each backend from `provider/backend/list` (one entry per method when a backend declares several). The type is fixed once the provider is saved. Workspace setup offers Sign in with ChatGPT.

- An API type shows key and endpoint fields. A type that signs in shows one sign-in row driven by [provider authentication](../protocols/provider-auth.md): the connection's name, a hint with the signed-in account and plan or the next step, and Sign in, Cancel, or Sign out. A new provider is saved before its first sign-in; one that is already signed in completes without opening a browser.
- While signing in, an Authorization row opens the `auth_url` page, shows a device code for entry, and answers backend prompts in place with secret answers masked; saving waits until sign-in ends. Desktop subscribes before starting login and reads state to recover missed updates; it does not poll.
- Failures show a localized message in the hint with Sign in available again.
- Authenticated providers load their model catalog; missing credentials or catalog failures offer sign-in and retry. A provider that signs in is not rejected for lacking an API key, and a late response cannot switch another editor. ChatGPT credential and refresh rules belong to [OpenAI subscription authentication](../architecture/openai-subscription-auth.md).

Composers show a usage control for the active provider when `provider/auth/usage` reports usage, summarizing the most constrained remaining-headroom window, with details listing available windows, remaining usage, reset time, and optional credits and limit warnings. When usage reports `balances` instead of windows, the control shows the provider's mark and the first balance amount, warns when the balance is insufficient, and its details list total, granted and topped-up amounts per currency. Desktop requests usage again when a turn of the active provider completes.

### 6.8 Channel Modules

This section defines the user-visible workflow for TypeScript channel modules configured from Desktop. The workspace AppServer owns their runtime lifecycle; module and descriptor contracts are defined by [plugin-architecture.md](../architecture/plugin-architecture.md).

#### 6.8.1 Discovery and Identity

- Desktop may list discoverable channel modules from static metadata without executing module logic.
- Modules load from bundled and user-installed locations; a user-installed module overrides a bundled one with the same `moduleId`. Identity is canonicalized by `moduleId`, not folder name.
- Invalid metadata skips that entry without breaking the list.
- Browse and detail surfaces may use optional `interface` metadata (`shortDescription`, `longDescription`, `previewPrompt`, `setupSteps`), localized with the same fallback rules as display names; technical details (package, source, transport, and readable capabilities such as channel tools or interactive setup) belong in the detail information area; the variant appears only where a channel has several ([6.8.6](#686-variants)). The detail page shows `setupSteps` as a numbered list of what to do outside DotCraft, such as on the platform or in its gateway, to finish connecting.

#### 6.8.2 Configuration Workflow

- Module configuration is workspace-scoped and stored in `.craft/<configFileName>`. Users can view and update values required for runtime startup.
- Fields meant only for interactive setup are not ordinary manual-entry fields.
- A module is configured once its config file is readable and no required field is empty. Unconfigured modules offer a setup action that opens configuration directly; configured modules show runtime status.

#### 6.8.3 Enable, Disable, and Runtime Expectations

- Enabling or disabling a module explicitly updates AppServer-owned channel configuration and starts or stops its runtime for the active workspace.
- Saving configuration while a module runs states when restart or re-enable is required.
- Desktop reconnect, window teardown, and workspace navigation never implicitly enable, disable, restart, or replace a channel; Desktop releases only its own polling and setup resources. The workspace AppServer stops channel runtimes when it shuts down.

#### 6.8.4 Module Status Semantics

- Status uses user-meaningful states: at least not configured, connecting, connected, stopped, and error.
- Server-observed `channel/status` is authoritative. Desktop may hold temporary state for an in-flight action but never a competing local process lifecycle.
- Module status is distinct from AppServer connection state.
- In Remote mode, local Start/Stop controls that do not control the remote workspace are hidden or disabled.

#### 6.8.5 Interactive Setup and QR-like Flows

- A module declaring possible interactive setup gets a guided workflow, which may read module-produced temporary artifacts from `.craft/tmp/<moduleId>/...`.
- While the channel's detail page is open, a sign-in artifact such as a QR code opens a modal dialog over the page, whichever mode the page is in. The dialog follows artifact refreshes, shows a success state once the module connects, and offers a retry when the module stops unexpectedly. Dismissing it before sign-in completes stops the module.
- Setup handles artifact refresh, expiration, and repeated attempts without restart. A module that re-enters a setup-required condition surfaces it again with a recovery path.

#### 6.8.6 Variants

- Multiple variants may exist for one logical `channelName`; exactly one is active at a time. Switching variants updates the active module and its configuration workflow and stops a running previous variant.

#### 6.8.7 Refresh and Startup Restore

- An explicit refresh re-evaluates available modules without restart.
- Restoring previously enabled modules on launch is best-effort: missing or unconfigured modules are skipped, and nothing blocks startup.

#### 6.8.8 Diagnostics and Preconditions

- Prerequisite failures (such as missing runtime dependencies) are explicit.
- Before enabling, Desktop validates required fields; an absent config file counts as incomplete configuration, and enabling an incomplete module opens its configuration workflow with the missing fields named.
- Startup or runtime failures give an understandable signal and a next step (retry, reconfigure, inspect logs), distinguishing setup, connectivity, and runtime failures.

### 6.9 What's New

Desktop owns a versioned What's New surface for release highlights, independent of AppServer beyond reaching the normal workspace UI.

- Release text is bundled with Desktop and keyed by app version. Optional media comes from the DotHarness resources repository and is downloaded, size-checked against a per-entry budget (oversized entries are rejected), SHA-256 verified, and cached.
- After an update, unseen highlights appear automatically once the user reaches the normal workspace UI for a ready workspace and required media is cached; never on welcome, setup, launch, or blocking error surfaces. A missing or invalid last-seen version counts as unseen. Media failure skips the automatic prompt without marking the release seen.
- Closing a prompt with unseen releases records the latest visible version as seen.
- Users can reopen What's New manually, which shows all bundled releases up to the running version regardless of seen state, newest expanded.
- Missing media degrades to a stable text presentation.

### 6.10 SSH Machines

The SSH segment of Connections ([§6.7](#67-settings-surface)) lists machines this PC reaches over SSH; one SSH connection is one machine. Machine lifecycle and remote deployment details belong to their own feature spec. Desktop rules:

- A remote project is a folder on a machine. It appears in the Projects rail with remote identity ([§3.7](#37-projects-rail-thread-navigation-and-secondary-connections)), and opening it makes the machine's remote AppServer the foreground connection under [§3.1.1](#311-local-and-remote-appserver-ownership).
- Turning a machine off or deleting it closes Desktop's connections only; work on the machine keeps running and nothing there is removed.
- While an SSH project or deployment is the active session, the Workspace segment shows it read-only instead of an editable URL.

### 6.11 Satellites

The Satellites segment of Connections lists machines lent to the user's agent through [Remote Tool Host](../architecture/remote-tool-host.md); the Share this PC segment shows the other direction. An SSH machine runs DotCraft for the user's remote projects; a satellite is someone else's PC that runs tools for the user's agent. The segments never share state.

**Planes and trust**

- Enrollment (list, invite, revoke, presence) is read from Hub by the trusted host process through the Hub Local API `/v1/satellites*` routes and Hub events. Per-thread routing (connect, disconnect, route state) uses the AppServer `remoteToolHost/*` methods. Both identify a machine by the same `hostId` (the Hub `peerId`). Fleet operations never go through AppServer, and connect/disconnect never go through Hub.
- Remote screen frames ([§10.4](#104-remote-screen-view)) use a third, Hub-bridged plane opened by the trusted host process; AppServer is not involved.
- The Hub bearer token never reaches the renderer. The renderer sees redacted satellite records; invitation URLs are held in memory only.

**Segments**

- Share this PC presents the local Satellite runtime's pairings read-only and is hidden when no Satellite runtime exists.
- Satellites shows a setup state when Hub lacks the satellite capability. Machine state is one of **offline** (not connected to Hub), **ready** (connected, no lease), or **in use** (a thread holds a lease); presence comes from Hub and lease state is overlaid from `remoteToolHost/list` when an AppServer is connected.
- Machine detail shows identity, state, shared folders (busy folders marked as in use here or by another agent; the wire carries only `self`/`other`), recent activity from received Hub events, refresh, and a confirmed remove that revokes durable authority.
- Inviting mints a single-use, expiring link on open (the invited machine's owner chooses the folder); an unexpired link Desktop already holds is shown again. Desktop remembers only ids and expiry of invitations it created, and announces a joining machine only when the join event's consumed invitation id matches one of them.

**Run on**

- The Run on control in the composer context row selects This PC or a (machine, folder) pair with availability. It is hidden when no satellite is enrolled, without `capabilities.remoteToolHost`, when attached to a remote AppServer (whose Hub is not this Desktop's), in the default Chat workspace, and in minimal embedded composers.
- It stays enabled while a turn runs: a selection then is pending (replacing any earlier pending selection) and applies once the thread is idle; the current turn keeps its confirmed route. On Welcome, a choice is a client-held pending route applied when the first message creates the thread, before its first turn.
- Routes are lost on cold resume. Desktop remembers the last explicitly applied choice per thread and re-applies it at most once per thread per connection, only for the visible idle thread whose target is online with a free folder. Choosing This PC forgets it after disconnect succeeds. Silent re-apply failures keep the confirmed route; only explicit selections report errors, worded from the error's `messageKey`.
- Every route change arrives as one `remoteToolHost/route/changed` notification regardless of cause, and the server persists a matching `remoteRoute` system notice, rendered as an inline divider at the point in the turn where the route changed, identically live and on reload. The control follows the notification. After a lost lease it stays on the machine marked offline; the route clears only on disconnect.

**Join links**

- Desktop owns the `dotcraft://` protocol registration whenever installed. `dotcraft://satellite/join` is always consumed by Desktop and forwarded to the installed Satellite client; Desktop never completes a pairing itself and, without Satellite installed, points to its installer. A launch carrying only a join link forwards it and exits without opening a window.

### 6.12 Agent Builder

The Agent Builder edits an Agent Profile as a document beside the conversation, so the person and the agent work on the same subject at once.

- Built-in templates are offered from Welcome without displacing the composer as the screen's subject; keyboard focus reveals a template exactly as hover does.
- The document lists tools, skills, and MCP servers as capability chips identified by catalog artwork or a kind mark; adding one offers a searchable catalog with name, purpose, and origin.
- While the agent edits, one presence cursor marks the field being edited, moves between fields, and remains on the last edited field after the turn. The document is non-interactive while a turn runs.
- The document asks whether the agent may delegate (`tools.agentControl: full` or `disabled`; an authored allow-list reads as off and is kept until changed) and how approvals resolve (ask the person or approve automatically; `default` and `deny` are shown as they resolve and replaced only when the person answers). The outside-the-workspace permission is not part of this surface and is preserved.
- Each builder tool call renders as a profile-change row stating the change, with long values behind a disclosure and failures with their reason.

### 6.13 Desktop Pet

The default companion can be colored and dressed with collectible items; the settings record, appearance source, find rule, and bag and exchange rules belong to the avatar feature. Desktop rules:

- A personal Pet settings tab holds a customization switch, the companion with its color and bag, and exchange; with customization off, only the switch remains.
- Finds arrive on their own and are announced through the shared toast stack with a wear action; an exchange's result uses the same toast. Desktop advances the runtime gate periodically and counts every `item/usage/delta` it receives toward the token gate; timing and odds are not settings.
- All pet copy, including item names and feature lines, is client-owned and localized by item id (`pet.item.<id>.name`, `pet.item.<id>.feature`); a new item needs both keys in every shipped locale.

Visual treatment follows [Desktop DESIGN.md](../architecture/DESIGN.md#pet).

### 6.14 App Updates

Packaged Windows builds update themselves from GitHub Releases. Other platforms and unpackaged builds report updates as unsupported and show no controls.

- Desktop checks at startup and periodically (not while downloading or ready), reading the release feed and `latest.yml`, not the GitHub REST API.
- A newer version downloads in the background, verified against its SHA-512, differentially when the running version's block map is published. A verified download makes the update ready; restarting installs it silently into the same installation. A ready update survives restarts.
- Manual checks from the Help menu report up-to-date or failure; background failures stay silent.
- The update dialog's primary action carries the download: it shows the percentage while the update downloads, its tooltip gives the transferred size and speed, and the same action then restarts into the update. Only the release notes scroll.
- Each release publishes `DotCraft-v<version>-win-<arch>-Setup.exe` with its `.blockmap` per Windows architecture and one `latest.yml` listing all; the updater selects the installer for the running architecture.

### 6.15 Phones

The Phones segment of Connections lists phones that may control this computer and turns phone access on and off. The mobile gateway, pairing codes, and device records belong to the mobile client's spec. Desktop rules:

- The trusted host process reaches the gateway through the Hub Local API `/v1/mobile*` routes and `mobile.*` Hub events, the same plane as Satellites enrollment. The Hub bearer never reaches the renderer; pairing codes and QR payloads stay in memory while shown and are never persisted. A relay token passes from renderer to host only when saving and is never returned by Hub.
- The segment shows a setup state when Hub is unavailable or lacks phone access. A failed start explains its `failureCode` and leaves access off.
- The phone list stays visible while access is off, so phones can be removed (with confirmation). Adding a phone requires access on.
- Pairing shows a code with its expiry; refreshing invalidates the previous code. When `mobile.pairingRequested` matches its code, the dialog asks whether that phone may control this computer, restating that it can do anything the user can do in DotCraft here. Allow waits for the phone and completes only on `mobile.devicePaired` whose `pairingId` matches; Decline, or `mobile.pairingCancelled` for its code, shows a fresh code. Turning access off ends a shown code.
- Off-network relay settings are managed through `PUT` and `DELETE /v1/mobile/relay` and reflect `relay` state; changing the relay leaves pairings unchanged.
- In Remote mode, the segment still manages this Desktop's own Hub; phones reach that Hub's local projects, never the remote AppServer Desktop is attached to.

## 7. Keyboard Accessibility and Localization

### 7.1 Keyboard Expectations

- High-frequency actions are keyboard-accessible: create thread, send message, interrupt turn, navigate threads, jump between user messages, respond to approvals, and dismiss transient overlays when safe.
- If a shortcut is unavailable on one platform, an equivalent keyboard path exists.

### 7.2 Accessibility

- Critical workflows never rely on color alone.
- Focus order stays predictable during navigation, sending, approvals, and review.
- Approval requests and blocking errors move focus so the next required action is clear.
- Streaming content stays readable as it updates.
- Hidden or disabled features communicate why they are unavailable.

### 7.3 Localization

- All client-owned user-facing strings are localizable.
- Protocol values such as model ids and thread ids stay stable and are never translated as routing keys.
- Changing display language updates client-owned UX promptly; locale-sensitive formatting follows the selected locale consistently.

## 8. Error Handling and Recovery

### 8.1 Connection Errors

- Failure before initialization shows a startup failure state with retry.
- Persisted Remote-mode settings with an invalid WebSocket URL produce error type `remote-config-invalid`, whose primary action opens connection settings and clears the blocking overlay so the user can fix the endpoint or return to Local; retry is not the only recovery path.
- A remote project failure is distinguishable from the previous local workspace; switching to a local project or disconnecting returns Desktop to local mode.
- A drop after initialization shows a disconnected state and starts automatic recovery without silently discarding active context.

### 8.2 Thread Errors

- If a thread cannot be read, resumed, or updated, the user sees a clear failure and stays in a safe prior context.
- A selected thread that disappears remotely is removed, falling back to a safe empty or next-valid state.
- If another client starts a turn first, the user sees the thread is busy rather than a silent send failure.

### 8.3 Turn Errors

- Failed turns stay visible in-thread with enough information to understand that work stopped, and the conversation can continue unless the thread is invalid.
- Interrupted and failed turns are distinguishable in user-visible language.

### 8.4 Approval Errors

- If an approval becomes invalid, times out, or cannot be delivered, the user sees the resulting turn outcome.
- If approval handling is unsupported in an environment, that limitation is known before a turn blocks whenever possible.
- If a user-input request cannot be shown or delivered, it resolves with empty answers so the turn can continue.

### 8.5 Input and Attachment Errors

- Invalid input, unsupported or oversized attachments, and failed attachment preparation surface before or at submission.
- A degraded fallback states exactly what was dropped or changed.
- Search or attachment helper failures never corrupt the rest of the composer.

### 8.6 Automation Errors

- If definitions fail to load, the Automations view remains usable enough to retry.
- An action failing on stale state refreshes server truth and reconciles.
- Missing review data shows that the run exists but cannot currently be inspected.

## 9. Non-Functional UX Requirements

### 9.1 Responsiveness

- Streaming text feels live rather than batch-delivered.
- Thread selection, approval responses, answers, and interrupts acknowledge input immediately even when protocol completion arrives later.
- Opening a main view or settings panel never leaves its area blank; Desktop may preload view modules on hover or focus and shows a loading placeholder while a module loads.
- A main view the user leaves may stay mounted for a while so returning restores its state immediately.

### 9.2 Reliability

- The client tolerates reconnects, out-of-order navigation, and concurrent updates from other clients without corrupting visible thread state.
- Capability changes across reconnects enable or disable affected surfaces.

### 9.3 Platform Coverage

- The UX contract applies across supported desktop platforms. Platform differences may change implementation but not the meaning of connection state, thread state, approval flow, or automation flow.

### 9.4 Readability

- Long-running sessions, thread history, tool output, plan progress, and automation output stay legible with long content and repeated updates.

## 10. Auxiliary Surfaces

Surfaces beyond the conversation follow the same rules: protocol-driven behavior, explicit status and recovery, and separation between workflow rules and visual implementation.

### 10.1 Viewer Panel

**Ownership**

- The right-side viewer panel coexists with the changes, plan, and terminal tabs and hosts native file viewers, embedded browser tabs, and terminals.
- It belongs to the conversation surface: an open thread and Welcome show it; other main views hide it without changing its saved state.
- On Welcome, the panel belongs to the foreground project's Welcome draft (open state, tabs, unsent comments). It offers only what works without a thread (files, browser, terminal), and comments and files added from it attach to the Welcome composer. When the first message creates the thread, that panel state moves to the thread with live terminal and browser sessions intact, and Welcome starts fresh.

**File links**

- Chat-local file references, including absolute paths and `file://` links, may open in the viewer even outside the active workspace. Query, fragment, and line hints are parsed before decoding the path, so encoded filename characters stay part of the name; equivalent path spellings resolve to the same file.
- External local files are served only after a user-triggered exact-file authorization, which never extends to the parent directory or siblings.
- Local file links accept one-based `:line`, `:line:column`, `:start-end`, and `#Lstart-Lend` positions. Model guidance teaches one form: standalone Markdown links with an optional one-based `:line`, preferring direct paths over URIs. Every explicit click is a new navigation, applied after loading and scroll restoration; returning to a tab restores its reading position instead. Positions are bounded to the document; Markdown line navigation switches to source mode through the normal save/review guard.
- A navigated line or range is highlighted separately from selection with an empty caret at the start, without changing the document or undo history, and clears on edits or deliberate caret movement.
- User messages stay plain text, except that a Markdown link to a local file renders as a file reference chip that opens at its position.
- Conversation references stay selectable; selecting their text never opens a viewer, and copying across them does not insert line breaks.

**GitHub references**

- In assistant Markdown, a link targeting exactly a github.com pull request or issue renders as a GitHub reference when its text is empty, the URL, `#<n>`, or `<owner>/<repo>#<n>`; other text is kept and gains only state. Other GitHub URLs and hosts stay ordinary links. A click opens the URL normally.
- Desktop reads title, state, author, age, and pull request diff stats through the local GitHub CLI credentials, from the trusted host process only, lazily and with short-lived caching. Failures leave the reference usable with its number and explain the reason on hover. References in the workspace's GitHub `origin` are labeled by number alone.

**Link previews**

- Resting on or keyboard-focusing an ordinary web link in assistant Markdown opens a preview of the page: its host, favicon, preview image, and title. Only `https` links without credentials get a preview; other schemes, `localhost`, `.local`, `.internal`, `.test`, and literal IP hosts keep the plain URL hint, and GitHub references keep their own card. The preview's Open action does exactly what clicking the link does.
- Metadata is fetched by the trusted host process only when a preview opens, never for every link in a transcript. The request carries only the URL: no cookies, credentials, or referrer, in a session separate from the browser panel's. The host refuses any address that resolves to a loopback, private, link-local, or otherwise non-public network, checking again on each redirect (at most three), and bounds each lookup by time and size: HTML is read only up to the end of its head, and the image and favicon are returned inline, limited to raster image types, so the renderer never loads the page's resources itself.
- Results are cached by URL for a few minutes (failures briefly) and concurrent lookups for a URL are shared. A failed or refused lookup leaves the link usable and the preview says why.

**Files and editing**

- The Files tree offers one root per folder of the active local Project, following the active file's containing folder until another root is chosen. Files are labeled relative to the longest containing Project folder, or absolutely otherwise.
- Existing local text files up to 10 MiB open as editable documents with autosave after inactivity and immediate save on the platform shortcut. Files over 10 MiB and up to 20 MiB open read-only; larger files fail without loading. Tab sessions keep drafts, baseline, mode, history, selection, and scroll while unmounted.
- Markdown defaults to an editable preview with a source mode; switching modes waits for the draft to save and stays put on failure or unresolved review.
- The preview shows Markdown images in place, except where the selection is, which shows their source. Relative paths resolve against the document's folder; local images load through the viewer's file boundary, so an image outside the workspace or not authorized shows its alt text instead. `data:` images are shown as is; remote images are not loaded.
- Saves preserve the file's byte-order mark and line endings and may only update an existing regular file inside the active workspace or the exact authorized external file; editing never creates files or broadens access.
- Each save compares the opened version with disk. Sync and saves are serialized per tab so stale reads never overwrite later edits. Disk content equal to the baseline or the draft reconciles silently. Distinct local and disk changes pause autosave and open a review of disk content against the local draft, recording explicit old/new content; every decision uses the latest disk version as the next baseline, and a write version check catches later changes. Very large versions disable diff preview but not decisions.
- Closing waits for the draft to save. Failed saves or unresolved reviews offer to keep viewing or discard local changes (closing without writing).
- Asynchronous highlighting never replaces newer content or resets selection; focused editors own Find.
- The viewer's Open action launches the displayed local file in the selected application ([5.17](#517-open-in-another-app)).

**Embedded browser**

- Embedded browser tabs use a workspace-scoped persistent profile (`persist:dotcraft-viewer:*`) so site data survives restarts without crossing workspaces. Those profiles identify as the bundled Chromium runtime, without Electron or DotCraft product tokens, using the operating system's preferred languages.
- New-tab links open another viewer tab. Script popups, including authentication popups, open as controlled child windows sharing the opener's profile and preserving `window.opener`, `postMessage`, cookies, and script close; they keep the opener's navigation boundaries and close with it.

### 10.2 Browser Automation

- When Desktop declares the browser capability, embedded browser tabs may be controlled by the active agent through the thread-bound browser runtime. Desktop declares `browserUse.backend` and, when it supports several, `browserUse.backends`: `desktop-iab` for the embedded browser and `chrome-extension` for the user's Chrome.
- Agent-controlled tabs remain regular viewer tabs; opening one may focus it the first time, but later automation never steals focus.
- While an agent operates a tab, the tab shows its automation state (session name and recent action when useful) and a virtual cursor.
- Navigation, screenshots, DOM snapshots, console inspection, and coordinate input remain subject to Desktop's browser policy, including local-URL defaults and external-domain approval or blocking.

### 10.3 Desktop Pet

Desktop pet mode presents the composer mascot and its existing conversation on the desktop. It preserves the current workspace, thread, draft, and running work and never creates independent conversation state.

- Dragging the composer mascot to a window edge, or releasing it beyond a detach distance, enters pet mode; a short drag or Escape returns it. Only one mascot is ever visible during transfer, and reduced motion keeps the order without travel.
- The pet stays within display work areas. App restore actions return to the main window. If another chat composer replaces the source composer, the pet transfers to it; companion failure restores the main window.
- The pet carries the source composer's work states and effects; idle behavior never replaces a work state, stays within the display, and is suppressed during interaction and under reduced motion.
- Quick Chat is the pet's only composer: it shares the source editor's draft and submission behavior, never returns to the main window on send, and never lets late updates replace newer typing. Its single control sends, steers or queues into a running turn per the follow-up preference, stops the turn when the draft is empty, or dictates when there is nothing to stop.
- One activity pill shows the source thread's status (needs input over blocked over ready over running), its name, and one derived status line, never the conversation itself. Ready persists until seen; dismissal is remembered per turn.
- While the pet presents a thread, the hidden main window keeps conversation state current so approvals and completions reach the pill.
- The pill may relay a pending tool approval with the composer's decision options, sent through the Desktop decision path, which stays the only resolver; the relay is refused unless the displayed request is still pending. User-input requests and plan confirmation return to the main window.
- A plugin replacement of `composer.mascot` keeps its own character and opts out of the drag transfer.

### 10.4 Remote Screen View

Desktop shows the live desktop of the machine a thread is routed to over the Hub screen plane ([§6.11](#611-satellites)).

- The launcher appears when the thread has a route to a connected machine that declared `screen-v1`, and stays while a view is open. The view opens as a movable dock and can expand to a larger modal without reconnecting.
- One stream per window. Switching threads closes it and remembers the view mode per thread. An offline machine does not close the view; it dims and redials until the machine returns, closing only when the route is removed or the user closes it.
- The view is read-only and latest-frame-only. Stalled, paused, or offline states keep the last frame dimmed and are conveyed through tooltip and accessible name.
- Each surface requests a resolution matching its displayed size at a modest frame rate; a hidden view requests nothing, and hiding or minimizing the window stops capture.
