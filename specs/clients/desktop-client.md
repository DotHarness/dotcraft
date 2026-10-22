# DotCraft Desktop UX Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.2 |
| **Status** | Living |
| **Date** | 2026-10-20 |
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

Desktop is a host adapter over the TypeScript SDK. Its AppServer and Hub transports are owned by the trusted host side of the application; the UI reaches them only through an authorized projection and never opens a transport itself. Desktop does not maintain a parallel JSON-RPC or Hub implementation and consumes the generated protocol contracts rather than restating payload shapes. Desktop Plugin calls use the same connection routing and remain scoped to the foreground workspace.

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
- A switch that completes quickly, such as to a workspace whose AppServer is already running, shows no loading state. Opening placeholders (project thread skeletons, Welcome suggestion and footer skeletons) appear only while the connection is still pending after a short grace period, and reloading the foreground list never replaces or expands a project's known rows.
- Window state may be remembered, but protocol state never leaks across workspaces.
- A switch resets the active thread selection unless the new workspace has a valid remembered equivalent.
- Workspace-scoped panels and capabilities are rehydrated from the new foreground AppServer; secondary workspace state never drives them.
- During local-to-local switching, unchanged plugins keep their instances, icons, and usable sidebar entries. The target workspace's complete plugin catalog determines additions, removals, and replacements according to the [Desktop Plugin lifecycle](../architecture/desktop-plugins.md#runtime-lifecycle).

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
- A secondary row whose thread stops running is marked unread and shows the unread dot, as foreground rows do, until the thread runs again or is opened. Promoting the project keeps its unread marks.
- Remote projects are never background secondary connections. At most one remote project is active, and it is foreground-only. Disconnecting an unsaved remote project removes it from the rail; a saved SSH remote project stays listed without thread rows.

**Actions**

- Clicking a background thread promotes its owning workspace, then opens the thread on the foreground connection.
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
- While a turn is running, the conversation always shows visible activity. The turn's activity ends with one Thinking status slot that keeps its height for the whole running turn, sitting right below the last row at the same spacing as tool rows, so content never jumps. It stays visible through reasoning and tool calls and fades out only while assistant text streams, an image is being generated, an approval is pending, or the latest answer has completed. Streaming assistant text that stops receiving deltas briefly is treated as stalled and shows the status until the next delta. While the turn waits on a user-input request, the slot says it is waiting for the user's answer.
- Conversation Markdown renders math: display math between `$$` or `\[ \]` lines and inline `\( \)`, `\[ \]`, or `$$ $$`. A single `$` stays text, delimiters in code stay literal, and an unparseable formula shows its source. Copying a formula copies its TeX. Raw HTML in replies shows as text except a line break.

### 4.3 Item Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/started` | New agent work becomes visible in the current thread. |
| `item/agentMessage/delta` | Agent text streams incrementally. |
| `item/reasoning/delta` | Updates the Thinking status title; the reasoning body is shown only when the user enables thinking content. |
| `item/toolCall/argumentsDelta` | Tool argument construction streams. Known built-in tools show a bespoke running label and, where useful, a progressive preview; unknown tools (including MCP and module tools) show a generic placeholder without raw argument JSON. |
| `terminal/started`, `terminal/outputDelta`, `terminal/completed` | Shell output and status merge by `threadId + callId` into the matching shell tool row. |
| `item/commandExecution/outputDelta` | Fallback for sessions without `terminal/*`; the same output is never rendered twice. |
| `item/toolExecution/progress` | Structured progress merges by `itemId` and `callId` into the matching tool row. |
| `item/completed` | The final item replaces any in-progress representation. A trusted presentation descriptor may select a local renderer, and an eligible terminal `mcpToolCall` may advertise a live MCP Apps View; generic fallback remains available. |
| `item/usage/delta` | Context usage indicators refresh; deltas accumulate for the active turn and are reconciled by `turn/completed.tokenUsage`. |

- The Thinking status title is the last readable line of the turn's latest reasoning that has one, so between reasoning blocks it keeps the previous title instead of returning to the generic label; with no readable reasoning yet it is a localized Thinking label. When thinking content is shown, reasoning between tool calls is listed in order inside the tool group without splitting it.
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

### 5.1.1 Welcome Suggestions

- In an empty, connected conversation area, the client may show a small set of welcome suggestions that feel like likely next tasks for the workspace.
- Local fallback suggestions render immediately. If the server advertises `capabilities.extensions.welcomeSuggestions` and the workspace's personalization setting allows it, the client calls `welcome/suggestions` after connection and replaces the fallbacks only with a valid workspace-specific result (`source = dynamic`).
- Dynamic suggestions come from a server-persisted workspace snapshot of the latest successful result, generated from a snapshot of the workspace memory store and refreshed in the background. Shutdown or failed refreshes never force the client to forget a returned set.
- Absent capability, failure, timeout, or `source = none` keeps the fallbacks without an error state. Dynamic suggestions never block welcome-screen load and show no loading placeholder.
- Dynamic suggestions are visually distinguishable from the static defaults.
- Choosing a suggestion prefills the composer; it never auto-sends or creates a thread.

### 5.1.2 Conversation Prompt Suggestions

- A workspace setting (on by default, independent of memory) enables one suggested next user message after a successful Turn in a conversation with an editable composer. Generation runs only while that conversation is active, the composer is empty, and nothing is queued.
- Desktop generates it in one hidden ephemeral fork of the completed Turn that keeps the parent's model, instructions, tools, provider history, and prompt-cache routing identity. The fork asks for a short next message in the user's own voice, or nothing when no next step is obvious or the topic is unsafe or sensitive. Empty output means no suggestion.
- The suggestion appears as subdued text in the empty composer, never as a draft or submission. Tab accepts it as editable text; Escape dismisses it. Typing, switching conversations, starting another Turn, disabling the setting, or a deadline cancels the request and cleans up the fork.
- The ephemeral thread and its notifications never enter visible conversation or navigation state. An older result never replaces a newer one, and failure leaves the composer unchanged. Traces link the parent Turn to the temporary one and record outcome without message content.

### 5.1.3 Workspace Setup

- If a selected folder has no `.craft/config.json`, Desktop may show a guided setup flow before connecting. The flow is local to Desktop and `dotcraft setup` and does not depend on AppServer provider RPCs.
- Setup covers folder and interface language, project instructions (only when a migration source exists), model access, and model. Completed steps can be revisited; the last step creates the workspace.
- Model access lists saved explicit personal providers, preselecting the personal default, and offers new connections: Sign in with ChatGPT, OpenAI API key, Anthropic API key, or another OpenAI-compatible service. Sign in with ChatGPT completes OAuth before the model step and creates a `chatgptOAuth` provider. Another service requires a name and http(s) endpoint and defaults its API format to `openai-responses`.
- Model discovery uses the DotCraft backend model catalog, not Desktop-owned constants or provider-specific parsing. Unsaved provider drafts are passed to the backend without exposing credentials in process arguments or logs. A refreshed catalog keeps the selected model when still listed. Listing is advisory: manual model entry stays available when it fails.
- Desktop setup requires a provider and model before creating the workspace; CLI setup may allow skipping the provider.
- Provider credentials and endpoints are always saved to personal provider config. A future-default choice controls scope: when enabled, setup updates the personal provider and model defaults and removes equivalent workspace overrides; when disabled, the selected provider and complete model preference are written as explicit workspace overrides even if they equal current personal defaults.
- Settings provider terminology is `OpenAI-Responses` (`openai-responses`), `OpenAI-Legacy` (`openai-chat-completions`), and `Anthropic`.
- Setup may offer the nearest ancestor `CLAUDE.md` (searching no higher than the Git root) as a project-instruction source, only when no root `AGENTS.md` or `AGENTS.override.md` exists. It copies it to the workspace root as `AGENTS.md` without changing the source or overwriting an existing destination.

### 5.2 Start a New Conversation

1. User chooses to create a thread.
2. Client calls `thread/start` or `worktree/createAndStart` with the complete initial thread configuration, including the selected mode and model snapshot. It includes `approvalPolicy` only for an explicit per-thread override; omission preserves workspace inheritance.
3. Desktop clears the composer and opens the conversation surface immediately, showing the submitted message and a creating state. That placeholder is client-only: it never becomes a thread id, never appears in the thread list, and is not subject to the restore pipeline in [5.3.1](#531-desktop-thread-restore-pipeline).
4. When creation succeeds, the thread becomes active and Desktop submits the staged first message after the thread is restored, without patching thread configuration in between. If the user opened another thread meanwhile, Desktop leaves them there and still starts the message on the created thread. Anything typed into the creating state's composer is carried to the created thread as a draft.
5. If creation fails, Desktop returns the user to the originating surface with the submitted content restored and reports the failure. No thread is left behind.

`dotcraft://threads/new?path=<dir>&prompt=<text>` opens the new-chat surface from the operating system or from a Desktop Plugin via `navigation.openExternal`. An existing local directory in `path` is added to the project list if needed and selected; a non-blank `prompt` is placed in the composer and never sent. No thread is created until the user sends.

When Welcome selects an Agent Profile, Desktop sends `config.agentProfileId` during creation with only the model overlays the Agent Profile start contract allows. It does not override Profile-owned approval, tool, skill, plugin, MCP, or instruction policy.

### 5.3 Resume or Open an Existing Thread

Selecting a thread starts one coordinated subscription and paged-hydration operation (5.3.1). If the thread is not turn-capable, the user sees why and which actions remain allowed.

### 5.3.1 Desktop Thread Restore Pipeline

Selecting a thread opens a new restore generation and clears the previous one. These invariants hold for every restore; page count, direction, and size are client choices.

1. Desktop establishes `thread/subscribe`, normally with `replayRecent = true`, before issuing history reads, so updates racing the first page are observable.
2. The thread header and newest Turn page are then read concurrently and merged into a chronological store by stable Turn and Item id.
3. Every Turn in a page is hydrated with all its Items; a page never renders a fragment of a Turn.
4. Replayed approval or user-input composers are not exposed until subscription readiness and the header and page reads for the current generation complete.
5. Results belonging to an older restore generation (reads, subscription operations, interactive request results) are ignored.
6. Subscription updates overwrite loaded entities by stable id; an unloaded Item is appended at the head without duplicating a concurrent page result.
7. History is read on demand, not drained. Loaded history is one or more segments of whole Turns; the newest ends at the live head. A gap before or between segments reserves space and loads a page from its nearer side as it approaches the viewport. Jumping to an unloaded Turn loads a segment around it. Adjacent segments merge, and inserting a page above the viewport preserves the visible scroll anchor. Listing Turn metadata for navigation is not a history read.
8. Switching threads releases loaded segments. Live-only output buffers are bounded independently of persisted history.
9. A subscription target is (foreground workspace identity, connection epoch, `threadId`). Desktop subscribes only after the thread is present in that workspace's authoritative thread list.
10. Subscription operations for the same target are serialized; a delayed `thread/unsubscribe` never cancels a newer `thread/subscribe` for that target. Different targets, including the same `threadId` in two workspaces, never block one another.
11. Switching threads or workspaces, disconnecting, or closing the window clears the active generation so late async work cannot subscribe through another workspace connection or restore UI into the wrong thread. An unsubscribe queued for an older connection is never sent through the new one.

`thread/read` is the current header, the Turn/Item list methods are persisted display history, and `thread/subscribe` is the live channel. A conversation still in the creating state of [5.2](#52-start-a-new-conversation) enters this pipeline only once its thread id exists.

### 5.3.2 Interactive Request Restore

Pending approval and user-input requests belong to the active turn and survive ordinary navigation.

- Switching away is not a decline, cancel, empty answer, or dismissal.
- A request that arrives while its thread is not the active fully-restored thread, or while conversation updates are paused or unreconciled, is parked on its thread. Parked requests activate only after the current generation's header and head-page hydration complete.
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
3. The user can approve, decline, session-approve when supported, or cancel, as the protocol allows. Approved work continues, declined work reflects rejection and may take another path, and cancel terminates the turn.
4. If the approval times out or becomes invalid, the user sees the resulting turn outcome.
5. A `computerUse` approval asks whether DotCraft may use the application named by `targetLabel` and offers exactly: always allow (`acceptAlways`, which also adds the application to Desktop's always-allowed list), allow for this thread (`acceptForSession`), and decline.

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
- When the latest completed Plan-mode Turn contains a successful `CreatePlan`, Desktop replaces the composer with a plan-confirmation composer, regardless of later output in that Turn. The confirmation survives thread switches and restarts and clears when the next Turn starts.

**Tool rows**

- Tool output stays readable in-thread and distinguishable from conversational text.
- Consecutive tool calls share one collapsed row that names what its calls did, one phrase per kind of work, without counts. Commentary, user messages, and calls with a surface of their own (subagents, stopped tasks, channel messages, plans, automations, workflow launches, answered questions, live MCP App Views, plugin-rendered tools, image views) end a group. Reasoning, background task notifications, and assistant messages without text never end a group, and to-do updates are not shown. While the turn runs, the trailing group names the call in progress; the Thinking status lives only in the turn's status slot. Rows never expand on their own. A failed call stays in its group and shows its failure in the expanded output.
- Desktop and the mobile client decide rendering, grouping, and labels only from the trusted presentation (`CoreNative` source, presentation id, and operation), never from tool names, so a renamed server tool keeps its rendering and an untrusted tool cannot borrow one. A presentation lacking an operation renders with its family's generic labels. Desktop's own runtime thread tools ([5.14](#514-desktop-runtime-thread-tools)), Code Mode, and the Dynamic Workflow launch are identified by name and source instead.
- `TaskStop` (`core.task`/`stop`) renders as one row naming what stopped (background terminal, subagent, or workflow) and its label; it expands only to show a subagent's last message.
- `SendChannelMessage` (`core.channel`) renders as one row naming the destination chat; expanding shows the sent text and attachments; a failure keeps the row and shows the error.
- A file tool call whose presentation options carry `memory` renders as memory recall or update rather than a file operation, following [Memory](../features/memory.md#8-presentation). `MEMORY.md` is not counted as a memory file.
- Completed `RequestUserInput` results render as a question-to-answer list, not raw JSON.
- `CodeMode` and `CodeModeWait` calls and their results are not rendered; the calls a script makes render as ordinary items in time order ([Code Mode](../features/code-mode.md#10-session-projection-and-presentation)). `clock__Sleep` and `SendUserMessageAsync` calls are not rendered either; the asynchronous message itself shows as a message. The mobile client follows the same rules.

**Shell and background terminals**

- Desktop declares `backgroundTerminals = true` and uses `terminal/*` notifications as its primary live shell output; `commandExecution` is the persisted projection and fallback.
- Shell rows stay collapsed by default; expanding shows live output while the command runs. Shell output is read-only from Desktop.
- A shell call whose tool result returned while its process keeps running stays live, keeps appending output, and settles on `terminal/completed` or a late `item/completed`, even after its turn ended. After the turn ends it is labeled as a started background terminal; a later cancellation is labeled as stopped. The row has no stop control.
- The thread Summary's **Background tasks** section lists running and paused Workflow runs from `workflow/run/list`, reread through `workflow/run/read` on `workflow/run/updated`, and running background terminals from `terminal/list`, kept current by `terminal/started`, `terminal/completed`, and `terminal/cleaned`. A terminal is listed once the `Exec` call that started it has returned, so a command handed to the background during a turn appears at once; while that call still waits, the command shows only in the conversation. A Workflow row stops its run through `workflow/run/stop`; a terminal row's stop-all control calls `terminal/stop` for every listed terminal. Both report failure, and selecting a row reveals the call that started it.
- The Summary's **Sources** lists only what the conversation can open again: files and images the user attached or referenced, skills the user referenced or the agent loaded, MCP servers used, fetched web pages, and web search. Files the agent read on its own are not sources. Task notifications and mailbox deliveries contribute none. An attached image shows its thumbnail, and selecting a source opens it: a file in the file viewer, a skill in Skills, a web page in the browser.
- A thread's Summary state stays live after the Summary is hidden, for the few most recently viewed threads, so reopening it shows current content at once. On first open the Summary shows only its header until its initial reads settle.
- Successful `WriteStdin` calls have no row of their own; their input appears on the originating shell row as further command lines, interpreted as a terminal would. Failed calls keep their row. Empty polls are not shown.
- Output of a command in a background thread keeps updating without forcing focus.

**General**

- A fullscreen overlay covering an embedded native view blocks that view for its whole lifetime; the native view is restored only after the last blocker closes. Ordinary menus and popovers do not block.
- Durable user-actionable result cards and available interactive Views stay outside collapsed Turn summaries. The latest successful Dynamic Workflow launch in a Turn is such a result and appears before the final assistant message; failed launches are ordinary tool history.
- The client may reveal related context automatically when new changes or plans appear, based on relevance.

#### 5.8.1 Trusted Local Renderers

Desktop consumes the exact `PresentationId` projected by the server. An active Desktop Plugin renderer for that id takes precedence, then the Core renderer, then the generic tool card. Tool payloads never provide module paths or executable code, and an unavailable id falls back without changing tool execution authority. Desktop resolves renderers in one place, and pinning, grouping, labels, and render plans consume that result. The authority and audience contract is defined by [Tool Architecture Section 14.1](../architecture/tools-architecture.md#141-trusted-local-renderer-registry).

#### 5.8.2 MCP Apps Interactive Tool Views

Desktop hosts stable MCP Apps `2026-01-26` Views through the standard AppBridge lifecycle (initialize/initialized, ping, notifications, teardown). Interactive UI is optional: ineligible items, unsupported clients, offline resources, initialization failures, and revoked Views render the tool result's text fallback.

- Desktop opens a View only for a terminal `mcpToolCall` whose current projection advertises availability, via `mcpApp/view/open`, then connects the bridge, loads the isolated sandbox proxy, delivers the validated resource, waits for View initialization, and supplies tool input and result. Each stage has a bounded wait and one teardown path. History and reconnect create a new View on demand; no previous handle or bridge state is restored.
- Availability is independent of tool success: with `mcpApp.available = true`, Desktop opens the View even for a failed result and delivers the original content and `isError` unchanged.
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
- Archiving an idle conversation is immediate with Undo. Archiving a running or waiting conversation requires a stop-and-archive confirmation; dismissing it leaves the work running. Confirming interrupts the current turn first, and interruption failure prevents the archive. Secondary workspace rows route the operation to their owning workspace.
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
- On a local thread, handoff to a worktree confirms first and calls `thread/worktree/handoff` with `mode = "worktree"`, sending the current branch as `baseRef`. The dialog offers a new worktree or any `worktree/list` entry no thread is bound to; choosing one sends its `path` and keeps its branch. For a new worktree the branch name field is optional; left empty, the server allocates one. The dialog says uncommitted changes move with the thread.
- On a worktree thread, handoff to local confirms and calls it with `mode = "local"`. The dialog names the branch the local checkout will switch to. A `WorktreeHandoffConflict` lists `conflictPaths` as local changes to commit or stash first and changes no UI state.
- Handoff is unavailable while the thread has queued input and says why.
- The request carries an `operationId`; the dialog lists the `thread/worktree/handoffProgress` steps for it as they arrive and omits skipped ones.
- When the agent moves its thread with a worktree tool, the `thread/updated` header updates the sidebar worktree icon and effective roots like a client handoff.

**Worktree settings**

- Settings › Source control › Worktrees, shown for Git workspaces, edits `Worktrees.BranchPrefix` and lists `worktree/list` entries with branch, bound thread (or none), and whether each has uncommitted changes or commits beyond its base.
- Remove calls `worktree/remove`. An entry with uncommitted changes or commits beyond its base asks for confirmation first and then sends `discardChanges = true`. An entry bound to an Active thread offers no removal.
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
- Background task notifications (`triggerKind = "terminal"` or `"subagent"`, and `"workflow"` with `triggerStatus`) are model-facing: they appear as neither a user bubble, turn navigation entry, queued-input entry, nor composer history, and reordering the queue keeps them in place. A turn started by one marks its activity boundary with the task kind and how it ended (from `triggerStatus`), with an accessible control that opens the task named by `triggerRefId`: a subagent in the Subagents panel, a workflow in its run tab, and a background command by revealing the call that started it. A notification drained into a running turn shows nothing, because the task's own call already shows how it ended.
- Dynamic Workflow continuation messages (`triggerKind = "workflow"` without `triggerStatus`) render an origin marker labeled by `triggerLabel` that opens the run named by `triggerRefId`.

**Conversation reveal**

Revealing a tool call finds its item by call id; for a task-started turn it finds the starting call (by terminal session id, the `SpawnAgent` result's agent path, or the `Workflow` result's run id), loading older history until the item's turn is present. It then opens every enclosing disclosure, scrolls the item into view, and highlights it briefly (respecting reduced motion). An item no longer in the thread reports that it is gone. An expanded command keeps streaming output.

### 5.13 Composer System Actions

The slash surface lists Desktop-owned system actions above custom Commands and Skills. Selecting one clears the slash query. Each group matches its labels, ids, and aliases (not descriptions): query characters match in order, each skip lands on a word start, and a mid-word start needs three characters. Within a group, prefixes rank above word starts above mid-word matches, then shorter names first.

- **Init** appears only when command management is available and the workspace `command/list` result contains `/init` (AppServer omits it when root `AGENTS.md` or `AGENTS.override.md` exists; Desktop never inspects these files). Selecting it, or submitting `/init`, calls `command/execute` and starts a normal turn with the returned `expandedPrompt`. Command availability reloads after each turn ends.
- **Plan mode** is always shown and toggles mode through the same path as its keyboard shortcut, calling `thread/mode/set`. On Welcome it updates the pending mode without starting a thread.
- **Compact** appears only when `capabilities.manualCompaction = true`, the thread has a turn, and no turn is running or awaiting approval. It calls `thread/compact/start` with a long client-side wait; a client timeout does not imply server failure.
- Direct `/plan`, `/agent`, and `/compact` are handled locally and never start an agent turn; `/compact` reports unavailability when its conditions are not met.

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

- Tools use the `desktop` namespace and PascalCase names: `CreateThread`, `ListThreads`, `ReadThread`, `SendMessageToThread`, `SetThreadTitle`, `SetThreadArchived`, and `SetThreadPinned`. No other model-visible aliases exist.
- They are declared with `deferLoading = true` so they are discoverable on demand, except on runtimes without deferred-tool discovery.
- Whenever the tools are declared, Desktop also declares `additionalContext["desktop.threadCoordination"]` (`kind = "application"`), a concise hint to search for the relevant thread tool before background thread management.
- Declarations are stable across Agent/Plan switches; handlers enforce mode policy.
- Desktop declares the tools only when it can handle their `item/tool/call` requests on the active transport. On reconnect with `capabilities.dynamicToolRebind = true`, Desktop re-declares the same specs and context when resuming; without rebind, pending calls fail through the normal unavailable path.

**Behavior**

- Tools call ordinary AppServer methods. `SetThreadPinned` is the only Desktop-local mutation and only updates Desktop settings.
- `CreateThread` requires a prompt; an optional display name becomes the title. It calls `thread/start` in the current workspace, then `turn/start`. The originating thread is recorded as a sibling source, never a subagent relationship. The user's active conversation does not change.
- `ListThreads` calls `thread/list` with optional `query`, `limit` (default 20, max 100), `cursor`, and `includeArchived`, returning a page summary with `nextCursor` and `totalMatched`.
- `ReadThread` reads the header and newest complete Turns without resuming, subscribing, or selecting the thread. Arguments: `threadId`, optional Turn `cursor`, `turnLimit` (default 1, 1–10), `includeOutputs` (default false), `maxOutputCharsPerItem` (default 2,000, 0–20,000); invalid arguments fail. Turns are fully hydrated or the call fails. The result `{ schemaVersion: 1, thread, page, turns }` appears in both structured and text content: full user/assistant text and typed Item metadata, media as references, materialized input snapshots ahead of `contextRef`, optional outputs with truncation metadata, newest-first pagination, and a bounded summary of queued inputs with their total count.
- `SendMessageToThread` sends a turn without stealing focus. If the target is running, waiting, or in blocking maintenance, it uses `turn/enqueue`, or fails as busy when unavailable.
- `SetThreadTitle` and `SetThreadArchived` map to `thread/rename`, `thread/archive`, and `thread/unarchive`; visible state updates through normal broadcasts.
- `SetThreadPinned` rejects archived and subagent threads when pinning and updates the project-scoped pin preference so the sidebar updates immediately.
- Optional `reasoningEffort` on create or send is applied through thread configuration (`thread/config/update`) and affects queued and future Turns, not the active Turn. Unsupported options fail rather than being ignored.
- Calls render as ordinary dynamic tool activity and are non-modal unless an underlying call triggers approval or user input. Background-changed threads update sidebar indicators without forcing navigation.
- Results distinguish started from queued work. Failures carry an English fallback and a stable code: `UnsupportedTool`, `UnsupportedOption`, `InvalidArguments`, `ThreadNotFound`, `ThreadArchived`, `ThreadBusy`, `ThreadManagementUnavailable`, `TargetUnsupported`, or `AppServerRequestFailed`.

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

A project can declare actions — named commands such as Run or Test — that run in a terminal from the conversation. Actions are available for local workspaces.

- **Files.** Each `.craft/environments/*.json` file is one environment. Desktop reads the environment folders of the workspace and each parent up to the folder containing `.git` (bounded depth). `environment.json` in the nearest folder is the default; others follow by distance, then path. Files are re-read when they change on disk.
- **Format.** `{ "version": 1, "name": string, "actions": [{ "name", "icon", "command", "platform"? }] }`. `version` defaults to `1`. `icon` is `tool`, `run`, `debug`, or `test` (others show as `tool`). `platform` is `win32`, `darwin`, or `linux` and limits the action to that platform. Strings are trimmed; actions with an empty name or command are dropped. An unparseable or invalid file is listed as an environment needing attention, with the reason.
- **Subagents row.** The conversation Summary's Subagents row reads "n working" while any child works or waits, otherwise "n done" counting the finished children, in one label style either way; while some work and some have finished, the finished count follows as secondary text. Its avatars show the working children, otherwise the finished ones. The label changes in place without a transition.
- **Environment section.** The conversation Summary has an Environment section: a Changes row once the conversation changed files (opening the Changes panel) and, in a local workspace, the primary action plus an actions menu (each action, add action, change environment, open terminal at the workspace root). Actions are ordered by most recent run in the project; the most recent is primary. Without environments the menu offers local environment setup; an environment needing attention offers to open its file. The chosen environment and run order are remembered per project on this device.
- **Running.** Running an action opens or focuses the conversation's terminal tab for that environment and action, restarts its shell in the folder containing the environment's `.craft` folder, and enters the command in that shell's syntax. The terminal owns the process.
- **Adding.** Adding an action writes the whole environment file (creating `environment.json` in the workspace when none exists) and runs it. If the file changed on disk since the dialog opened, saving fails and nothing is written.

## 6. Secondary Flows

### 6.1 Plugins and Skills

The Plugins destination has `Plugins` and `Skills` tabs, shown when `capabilities.pluginManagement` and `capabilities.skillsManagement` respectively are available; otherwise the tab or action is hidden or disabled with a reason.

- Users can browse discovered and installable plugins, inspect details and included tools and skills, install or uninstall plugins, and enable or disable installed plugins.
- Desktop tells a local AppServer where its bundled plugin resources live; without that, uninstalled built-in plugins are not installable catalog entries.
- Plugin install, uninstall, and enablement refresh both plugin and skill state, because plugin-contained skills follow the plugin lifecycle.
- Users can browse installed skills, inspect a skill's content, enable or disable skills when supported, and uninstall user-managed `workspace` and `user` skills (including their workspace-local variants). Skills with source `plugin` show attribution and are managed only through the owning plugin.

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

#### 6.1.3 Desktop Plugins

Installed and enabled plugins may contribute trusted Desktop modules through the inline `desktop` manifest declaration. Desktop derives entry points and content revision from AppServer plugin discovery rather than hardcoding plugin ids.

- Plugin-provided views appear only while the owning plugin is installed and enabled. If the current view's plugin is disabled, removed, or no longer declares it, Desktop moves to a built-in fallback view.
- Desktop modules load from local installed plugin files or a verified local cache of a remote plugin's Desktop output; Desktop never executes JavaScript from remote URLs.
- A remote AppServer must advertise `desktopPluginArtifacts` for Desktop to download and activate its plugin interfaces. Without it, Desktop skips them and explains the required server update; it never substitutes a local package or resolves a remote path locally.
- Desktop Plugin code is trusted local renderer code. Installing and enabling the plugin is the local trust decision; remote interfaces additionally require a client-side grant per remote workspace. Bundled and user-installed plugins share one host API and lifecycle.
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

**Notifications**

- Task completion notifications (`whenUnfocused` default, `always`, `never`) and on-by-default switches for approval requests and questions are Desktop-local preferences. They gate the Hub tray notification kinds in [Hub Architecture §10](../architecture/hub-architecture.md#10-tray-and-notifications): a waiting approval or question notifies only when no focused Desktop window has that workspace in the foreground, and clicking opens the chat.
- When Hub cannot deliver (non-Hub foreground connections such as Remote, SSH, and Docker, or no tray), the window notifies itself under the same switches while unfocused.
- Each request notifies at most once; a replay after reconnect or thread switch does not notify again.

**Personalization and memory**

- A personal Pet tab configures the default companion ([§6.13](#613-desktop-pet)).
- A workspace Personalization tab groups Conversation, Skills, and Memory settings by available capability; empty groups are hidden. Its welcome-suggestion toggle is workspace config, applies immediately, and updates the welcome surface on `config/changed`.
- The Memory group holds memory enablement (`memoryEnabled`), automatic extraction of missed memories (`Memory.AutoExtract`), Dreams when `capabilities.dreams = true`, and delete-all when `capabilities.memoryManagement = true` (`memory/reset` after confirmation). Memory toggles affect new sessions only ([Memory](../features/memory.md#7-memory-switches)). While memory is disabled, dependent settings show disabled with their stored values and an explanation.
- Dreams controls are hidden without `capabilities.dreams`. Entering Personalization or the Dreams page loads `dreams/status`, refreshed after saves and on `config/changed` for `memory` or `Memory`/`Dreams` keys. Manual runs call `dreams/run`, disable while running, and poll status. Run history uses `dreams/list` and opens Dashboard review links at `dashboardUrl#dreams/run/<runId>`; Desktop shows no memory diffs. See [Dreams](../features/dreams.md#10-desktop-ux-contract).

#### 6.7.1 Agent Import

When `capabilities.extensions.agentImport` is present, Settings shows an Import tab for the current workspace. [Agent import](../features/agent-import.md) defines detection, conversion, scope, and synchronization.

- Opening the tab loads settings, candidates, and history; detection can be refreshed. Sources show importable counts; existing or unsupported entries stay inspectable. Import is disabled while a pass runs.
- The import dialog separates host-wide tools and setup, current project configuration, and chat sessions, supports partial selection, and explains where content applies, never showing configuration secrets.
- Confirming submits the selected item identities, fingerprints, and categories, plus the categories offered, to `import/run`. `import/progress` updates progress; completion refreshes candidates, settings, and history. `import_busy` shows the running pass instead.
- Keep-in-sync applies immediately and can be paused without losing imported content or selections; a workspace opt-out disables it. After the first import the synced categories can be customized (at least one required, optionally including future categories), affecting future syncs only.
- History shows one entry per pass with per-item status, a localized reason, and a link to imported content. Outstanding setup (hook trust, missing MCP environment variables, plugin review) is listed with a direct action. A sync pass that changed nothing stays silent.
- Imported threads arrive through `thread/started`, are marked unread, and show their source app.

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
- Browse and detail surfaces may use optional `interface` metadata (`shortDescription`, `longDescription`, `previewPrompt`), localized with the same fallback rules as display names; technical details (package, source, transport, and readable capabilities such as channel tools or interactive setup) belong in the detail information area; the variant appears only where a channel has several ([6.8.6](#686-variants)).

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
- Each release publishes `DotCraft-v<version>-win-<arch>-Setup.exe` with its `.blockmap` per Windows architecture and one `latest.yml` listing all; the updater selects the installer for the running architecture.

### 6.15 Phones

The Phones segment of Connections lists phones that may control this computer and turns phone access on and off. The mobile gateway, pairing codes, and device records belong to the mobile client's spec. Desktop rules:

- The trusted host process reaches the gateway through the Hub Local API `/v1/mobile*` routes and `mobile.*` Hub events, the same plane as Satellites enrollment. The Hub bearer never reaches the renderer; pairing codes and QR payloads stay in memory while shown and are never persisted. A relay token passes from renderer to host only when saving and is never returned by Hub.
- The segment shows a setup state when Hub is unavailable or lacks phone access. A failed start explains its `failureCode` and leaves access off.
- The phone list stays visible while access is off, so phones can be removed (with confirmation). Adding a phone requires access on.
- Pairing shows a code with its expiry; refreshing invalidates the previous code, and the dialog completes only on `mobile.devicePaired` whose `pairingId` matches its code. Turning access off ends a shown code.
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
