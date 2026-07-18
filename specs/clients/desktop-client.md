# DotCraft Desktop UX Specification

| Field | Value |
|-------|-------|
| **Version** | 0.6.1 |
| **Status** | Living |
| **Date** | 2026-07-18 |
| **Parent Spec** | [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Tool Architecture](../architecture/tools-architecture.md), [App Binding](../protocols/app-binding.md), [Plugin Architecture](../architecture/plugin-architecture.md), [Goal Design](../features/goal.md), [Remote Server Management](../features/remote-server-management.md), [Desktop DESIGN.md](../architecture/DESIGN.md) |

Purpose: Define the stable user-experience behavior of **DotCraft Desktop** as a protocol client for DotCraft AppServer. This document specifies user-visible flows, interaction rules, state transitions, and recovery behavior. It does not define frontend implementation details, visual design, or framework choices.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Connection and Session Lifecycle](#3-connection-and-session-lifecycle)
- [4. Protocol Event to UX Behavior](#4-protocol-event-to-ux-behavior)
- [5. Core Interaction Flows](#5-core-interaction-flows)
  - [5.1.1 Welcome Suggestions](#511-welcome-suggestions)
  - [5.3 Resume or Open an Existing Thread](#53-resume-or-open-an-existing-thread)
    - [5.3.1 Desktop Thread Restore Pipeline](#531-desktop-thread-restore-pipeline)
    - [5.3.2 Interactive Request Restore](#532-interactive-request-restore)
    - [5.3.3 Snapshot and Realtime Reconciliation](#533-snapshot-and-realtime-reconciliation)
    - [5.3.4 Backend Verification Gate](#534-backend-verification-gate)
  - [5.8 View Changes, Plans, and Tool Output](#58-view-changes-plans-and-tool-output)
    - [5.8.1 Trusted Local Renderers](#581-trusted-local-renderers)
    - [5.8.2 MCP Apps Interactive Tool Views](#582-mcp-apps-interactive-tool-views)
    - [5.8.3 Inline Assistant Visualizations](#583-inline-assistant-visualizations)
- [6. Secondary Flows](#6-secondary-flows)
- [6.7 Settings Surface](#67-settings-surface)
- [6.8 Channel Modules](#68-channel-modules)
- [6.9 What's New](#69-whats-new)
- [6.10 Remote Servers](#610-remote-servers)
- [7. Keyboard Accessibility and Localization](#7-keyboard-accessibility-and-localization)
- [8. Error Handling and Recovery](#8-error-handling-and-recovery)
- [9. Non-Functional UX Requirements](#9-non-functional-ux-requirements)
- [10. Phase 2 Reserved Surface](#10-phase-2-reserved-surface)

---

## 1. Scope

### 1.1 What This Spec Defines

- The user-visible behavior of the Desktop client while connected to DotCraft AppServer.
- How users open workspaces, connect, browse threads, send turns, review results, and respond to approvals.
- How users navigate pinned threads, local projects, and the active remote project from the sidebar.
- How protocol events change user-visible state.
- How secondary surfaces such as Skills and Automations behave from the user's perspective.
- How users discover, configure, enable, and recover Desktop-managed channel modules.
- How trusted plugin-contributed Desktop extensions appear in Desktop surfaces.
- How trusted presentation descriptors and interactive View capabilities become safe conversation surfaces.
- How Desktop-owned Runtime Dynamic Tools can manage background threads through AppServer.
- How the client communicates failure, recovery, and availability constraints.
- Localization, accessibility, and performance expectations at the UX level.

### 1.2 What This Spec Does Not Define

- Wire protocol payloads, transport rules, or server semantics already defined in [appserver-protocol.md](../protocols/appserver-protocol.md).
- TypeScript module contract details (manifest schema, package exports, launcher contract, and conformance rules) defined in [plugin-architecture.md](../architecture/plugin-architecture.md).
- Frontend frameworks, component trees, IPC method signatures, process architecture, or state-store structure.
- Layout geometry, colors, typography, icons, spacing, animation, or other visual design details. Stable Desktop visual decision rules are defined separately in [Desktop DESIGN.md](../architecture/DESIGN.md).
- Platform-specific implementation APIs for notifications, menus, file search, or file persistence.
- Arbitrary third-party UI code execution for tool results.
- Untrusted third-party plugin sandboxing. Desktop extension v1 is limited to installed, trusted plugins.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Expose the AppServer protocol as a desktop workflow optimized for persistent threads and long-running agent work.
2. Support multi-thread productivity, including switching between threads while background work continues.
3. Support project-first navigation across recent local workspaces while preserving one foreground workspace owner for interactive work.
4. Preserve a clear review loop for approvals, file changes, plans, tool output, and automation runs.
5. Make connection state and recovery paths understandable without requiring users to understand protocol internals.
6. Keep workspace behavior predictable across reconnects, restarts, and concurrent clients.

### 2.2 Non-Goals

- Embedding the DotCraft runtime in-process.
- Acting as a full IDE, terminal emulator, or general-purpose file browser.
- Freezing a specific visual layout or frontend architecture.
- Defining remote plugin UI, mobile UX, or future task-board behavior in detail.
- Aggregating multiple remote workspaces in the background. Remote projects are foreground-only in this version.

---

## 3. Connection and Session Lifecycle

### 3.1 Workspace Entry

- The Desktop client is workspace-centric and may show multiple known workspaces in one window.
- Exactly one workspace is the foreground workspace at any time. Foreground workspace state owns the composer, active conversation, settings, capabilities, model/provider selection, workspace tools, and server-initiated interactive requests.
- Additional local workspaces may be connected as secondary workspaces. Secondary workspaces may contribute thread-list rows and lightweight runtime indicators, but they do not own workspace-scoped panels or interactive protocol surfaces until promoted to foreground.
- Opening or selecting a stopped workspace starts a connection attempt to an AppServer instance for that workspace, then promotes it to foreground after initialization succeeds.
- The client may support multiple connection modes, but the foreground UX contract is the same: the user selects or opens a workspace, the client connects, and thread operations remain unavailable until initialization succeeds.

### 3.1.1 Local and Remote AppServer Ownership

- In Local mode, Desktop connects through a Hub-managed local AppServer. Desktop may start, stop, and restart that local process, and connection-level changes may use an Apply & Restart action.
- In Remote mode, the AppServer lifecycle belongs to the user or remote environment. Desktop must not expose remote restart as a supported action, and must not route remote connection changes through local Hub restart semantics.
- Remote connection changes are applied with test-and-connect semantics: Desktop validates the draft `ws://` or `wss://` URL and token, completes a WebSocket `initialize` probe against that draft endpoint with a bounded timeout, then persists the settings and switches to the new connection only after the probe succeeds.
- If the remote probe fails, Desktop leaves the persisted connection settings unchanged so the next launch is not trapped behind a newly saved bad endpoint.
- When Desktop is launched with an explicit transient `--remote` endpoint, persistent connection-mode switching is unavailable. Settings must explain that the launch argument owns the current connection for that session.
- Remote connections opened through the Servers surface (see [§6.10](#610-remote-servers) and [remote-server-management.md](../features/remote-server-management.md)) are a tunnel-fronted special case of Remote mode: Desktop connects to a `ws://127.0.0.1:<port>/ws` local tunnel endpoint and reuses this same test-and-connect path. Remote AppServer lifecycle remains owned by the remote environment; Desktop manages only the SSH tunnel and the deployment-level container lifecycle, never a remote AppServer process restart.
- A Remote-mode session is represented as a distinct foreground project identity, separate from the local workspace that initiated the connection. Servers-managed, manual URL, and transient command-line remote sessions each keep local threads, pinned state, and welcome drafts isolated from the remote foreground project.
- Connecting to a remote project records the previous local foreground workspace when one exists. Disconnecting the remote project or selecting a local project closes the remote client/tunnel and returns Desktop to local mode, restoring the previous local foreground when possible.

### 3.2 Connection States

The client exposes four user-visible connection states:

| State | Meaning |
|-------|---------|
| `connecting` | The client is attempting to establish transport and complete protocol initialization. |
| `connected` | Initialization is complete and thread operations are available. |
| `disconnected` | A previously working connection is currently unavailable, but recovery may still be possible. |
| `error` | Connection failed or cannot proceed without user intervention. |

### 3.3 Initial Load

- After connection becomes `connected`, the client loads the minimum data needed for conversation workflows:
  - server capabilities (including whether `configChange` notifications are enabled for this connection)
  - thread list for the active workspace identity
  - optional capability-gated surfaces such as skills, automations, or model catalog
- If no thread exists, the client presents an empty ready state that clearly allows starting a new conversation.

During first-time workspace setup, provider model discovery uses the DotCraft backend model catalog rather than Desktop-owned model constants or direct provider-specific HTTP parsing. Existing providers are resolved by id; unsaved provider drafts are passed to the backend over stdin so credentials do not enter process arguments or logs. ChatGPT subscription setup completes OAuth in the wizard before requesting the account-scoped catalog. The wizard preserves a user's explicit model selection across refreshes, requires reselection when it disappears, and treats backend cache or bundled results as the only fallback source.

### 3.4 Reconnection

- On unexpected disconnect, the client transitions to `disconnected` and attempts reconnection automatically.
- During reconnect, the user must be able to tell that prior thread data is still local UI state while live updates are temporarily unavailable.
- After reconnect, the client completes a fresh protocol handshake and restores the active session context:
  - reload active capabilities
  - re-establish the active thread subscription when applicable
  - refresh thread and automation data that may have changed while offline
- If recovery succeeds, the state returns to `connected` without requiring the user to rebuild local context manually.
- If recovery fails persistently, the state becomes `error` and the user is given a retry path.

### 3.5 Workspace Switching

- Switching workspace is treated as promoting one workspace to foreground and demoting the previous foreground workspace when it can remain connected as a secondary workspace.
- The previous workspace window state may be remembered, but protocol state must not leak across workspaces.
- A switch resets the active thread selection unless the new workspace has a valid equivalent remembered locally.
- Workspace-scoped panels and capabilities must be rehydrated from the new foreground AppServer. Secondary workspace state must not drive these surfaces.

### 3.6 Multiple Windows

- Each window owns its own foreground workspace selection and may show multiple local recent workspaces.
- Multiple windows may be open concurrently, including windows whose recent workspace lists overlap.
- The same workspace may be connected by more than one Desktop process. AppServer multi-client semantics own protocol safety; Desktop must not rely on a process-exclusive workspace lock to prevent concurrent viewing.
- User actions in one window must not implicitly change thread selection or visible state in another window, except that workspace-level AppServer broadcasts may update shared thread summaries in all connected windows.

### 3.7 Projects Rail, Thread Navigation, and Secondary Connections

- The sidebar thread list is project-first. It shows a top-level **Pinned** section followed by a **Projects** section; time-group headings such as Today, Yesterday, and Previous 7 Days are not shown in the main sidebar thread list.
- The Pinned section may contain both individually pinned threads and pinned projects. Like Projects and Chats, its section header is keyboard-accessible and expands or collapses the complete section; the collapsed preference is persisted by Desktop. Individually pinned threads appear first. A pinned project moves its complete collapsible project subtree into Pinned and is removed from Projects; any individually pinned thread in that project remains in the flat thread rows and is omitted from the moved project subtree so it is never duplicated. Project pin state is Desktop-local, supports local and remote project identities, and survives reconnects.
- Pinned thread rows remain project-scoped. A pinned thread appears in the top section only after its owning project has loaded enough thread summary data, and it is not repeated in that project's ordinary thread rows.
- The Projects section is sourced from recent local workspaces plus the active remote foreground project when one exists. Running local recent workspaces may contribute loaded thread rows; stopped recent local workspaces appear without being started eagerly.
- Local project rows use local workspace identity. Remote project rows use remote identity and distinct local-vs-remote visual treatment so remote threads do not appear under the local workspace that initiated the remote connection.
- A project row expands or collapses the project group. It does not promote the project by itself. Clicking a background thread first promotes the owning local workspace, then opens the selected thread on the foreground connection.
- Foreground workspace state owns the composer, settings, capabilities, tools, active thread subscription, and server-initiated interactive requests. Secondary workspace state may update thread summaries and compact runtime indicators only.
- For local Hub-managed recent workspaces already running, Desktop may open secondary AppServer WebSocket connections. The v1 secondary connection cap is 8 workspaces per window; excess workspaces remain cold until selected or until LRU capacity becomes available.
- Secondary connections initialize, load thread summaries, and consume only thread-list and runtime notifications. They do not subscribe to every thread and do not receive turn, item, job, configuration, MCP, or server-request streams for inactive workspaces.
- Remote projects are not opened as background secondary connections in this version. At most one remote project is active, and it is foreground-only. Disconnecting it removes the remote project from the rail rather than adding it to recent local history.
- The global New Chat action starts in the current foreground project. A project-level New Chat action first promotes or starts the target local project, then opens the welcome composer; new thread creation always uses the foreground AppServer. The welcome composer project selector preserves a separate draft for each project identity and reloads capabilities, skills, plugins, and model state after promotion.
- Project actions are scoped to the project kind. Local projects may expose local opening, path copying, and removal from Projects when they are not foreground. Remote projects expose only remote-appropriate actions such as disconnecting and copying a remote endpoint or path; they must not expose local filesystem actions such as opening the path in the system file explorer.
- Hovering or focusing a thread row reveals a compact details card with the complete thread title, relative activity time, owning project name, and the current Git branch when a local Git head is available. Worktree threads use their recorded worktree branch. Chats, remote projects, and non-Git workspaces omit the branch row.
- Hovering or focusing a project row reveals a project details card with its name, pin control, visible thread count, waiting/running counts, and full local path or remote display path. Waiting takes precedence over running for a thread so one thread is not counted in both states. Connecting projects use a content-shaped loading placeholder; unloaded cold/error projects report that details are not loaded rather than claiming zero threads.
- Thread and project details cards attach to the sidebar edge with an 8px overlap instead of a floating gap. The overlap keeps the card's connection edge visibly inside the sidebar entry region; the card draws a single neutral `var(--glass-border)` hairline only on that edge, mirrors it when viewport constraints flip the card left, and omits the hairline if clamping prevents a real attachment.

---

## 4. Protocol Event to UX Behavior

This section defines how protocol messages affect user-visible behavior. It intentionally describes UX outcomes rather than internal state implementation.

### 4.1 Thread Events

| Protocol event | UX behavior |
|---------------|-------------|
| `thread/started` | The new thread appears in the thread navigation area and may become selectable immediately. |
| `thread/renamed` | Any visible thread label updates everywhere the thread is referenced. |
| `thread/deleted` | The thread is removed from navigation and from any active context. If currently open, the user is moved to a safe fallback state. |
| `thread/statusChanged` | Thread availability updates immediately. Actions that are no longer valid must be disabled or blocked. |
| `thread/resumed` | The thread returns to an active, turn-capable state. |
| `thread/runtimeChanged` | Thread activity indicators update immediately in the owning workspace's thread navigation area, including secondary workspace groups. |

Clients that display multiple workspaces in one process must route each AppServer notification with the workspace identity of the connection that received it. A notification without a workspace identity is interpreted as belonging only to the foreground connection for backward compatibility.

For secondary connections, only thread-list and runtime notifications update background project groups. Notifications that affect conversation content, workspace configuration, tools, MCP, jobs, approvals, user-input requests, or other interactive surfaces are foreground-only.

### 4.2 Turn Events

| Protocol event | UX behavior |
|---------------|-------------|
| `turn/started` | The active thread enters a running state. Sending a new turn on the same thread is blocked. |
| `turn/completed` | Running indicators clear and final turn results are shown. `tokenUsage` is the final turn aggregate, not an extra delta when real-time usage events were already consumed. |
| `turn/failed` | The user sees that the turn ended unsuccessfully and is given a path to retry or continue. |
| `turn/cancelled` | The running state clears and the user sees that the turn was interrupted. |

`Turn.error` is the canonical user-facing Turn failure. If the same failure is also retained as an Error Item, Desktop presents it once. Distinct errors remain independently visible.

While a turn is actively running, the conversation view must always show visible activity. If no live reasoning, non-stalled non-empty streaming assistant text, running tool row, approval wait row, user-input wait row, or system maintenance status row is currently visible, Desktop renders a non-persistent Thinking indicator at the active turn tail until the next visible live item appears. If non-empty assistant text is streaming but no text delta arrives for 2 seconds, Desktop treats that text stream as stalled and shows the same non-persistent Thinking indicator below the current streaming message; the indicator disappears as soon as a new text delta arrives, and the delta continues appending to the same streaming assistant message.

### 4.3 Item Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/started` | New agent work becomes visible in the current thread. |
| `item/agentMessage/delta` | Agent text streams incrementally when streaming is enabled. |
| `item/reasoning/delta` | Reasoning content is exposed only if the client chooses to show reasoning. |
| `item/toolCall/argumentsDelta` | Tool argument construction streams incrementally. For known built-in tools, the client renders a bespoke running label (e.g. "Writing <path>", "Searching \"<pattern>\"", "Drafting plan...") and, where useful, a progressive preview of the parsed argument fields. For unknown tools (including MCP and module tools), the client renders a generic "Generating parameters for <toolName>..." placeholder without surfacing the raw argument JSON. |
| `terminal/started`, `terminal/outputDelta`, `terminal/completed` | Running shell output/status is merged by `terminal.threadId + terminal.callId` into the matching `Exec` tool card in both the conversation view and the Terminal review surface. |
| `item/commandExecution/outputDelta` | Compatibility fallback for clients or sessions that do not receive `terminal/*`; Desktop must not double-render the same shell output when both paths are present. |
| `item/completed` | The final item output replaces or finalizes any in-progress representation. A trusted presentation descriptor may select a local renderer, and an eligible terminal `mcpToolCall` may advertise a live MCP Apps View; generic fallback remains available. |
| `item/usage/delta` | Context usage indicators refresh when the client surfaces real-time usage. Deltas accumulate for the active turn and are reconciled by the final `turn/completed.tokenUsage` snapshot. |

### 4.4 Approval Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/approval/request` | The current thread enters a waiting-for-user-decision state. The approval request becomes the highest-priority interaction. |
| `item/approval/resolved` | The approval decision is reflected immediately and the thread resumes or terminates according to the decision. |

### 4.4.1 User Input Request Events

| Protocol event | UX behavior |
|---------------|-------------|
| `item/tool/requestUserInput` | The current thread enters a waiting-input state. Desktop replaces the normal composer with a question composer and disables normal message submission. |
| `item/tool/requestUserInput/resolved` | The pending question composer clears and the thread resumes normal running/rendering state. |

If the user switches away while a request is pending, Desktop must park the request on that thread, show a sidebar badge indicating that an answer is needed, and restore the same composer when the user returns. Switching threads is not a dismissal and must not send an empty response.

Desktop must also tolerate the request being replayed by AppServer when the user returns via `thread/subscribe` or `thread/resume`. A replayed `item/approval/request` or `item/tool/requestUserInput` with the same logical `requestId` restores the actionable composer; it must not be treated as a new turn or a duplicate dismissal prompt.

### 4.5 Supplemental Events

| Protocol event | UX behavior |
|---------------|-------------|
| `subagent/progress` | The client may surface background worker progress if useful, but must not block the main conversation. |
| `plan/updated` | Structured task progress becomes available in the current conversation context. |
| `system/event` | Maintenance steps may be surfaced when relevant but must not overshadow core turn output. Provider stream retry events (`kind = "streamError"`) are shown as transient, tool-like rows at the active turn tail while the turn is running, then cleared on turn completion, failure, cancellation, or thread reload. |
| `system/jobResult` | Automation or heartbeat output becomes visible as an out-of-band result associated with its source run. |
| `cron/stateChanged` | Automation status views refresh to reflect the current job state. |
| `thread/goal/updated` | Goal-aware surfaces for the affected thread update from the server snapshot without forcing thread navigation. |
| `thread/goal/cleared` | Goal-aware surfaces for the affected thread remove the current goal snapshot without forcing thread navigation. |
| `workspace/configChanged` | Settings-adjacent surfaces re-fetch impacted regions (`skills`, `mcp`, `externalChannel`, workspace config fields, including welcome-suggestion personalization state and Dreams memory settings) without requiring manual full-page refresh. |

### 4.6 General Rules

- If the active thread is subscribed, updates should appear without requiring manual refresh.
- If the user is viewing another thread when an inactive thread changes, the client may indicate background activity but must not forcibly switch context.
- When a capability is absent, the corresponding UX surface is disabled or hidden rather than failing late.

---

## 5. Core Interaction Flows

### 5.1 Open a Workspace

1. User opens or selects a workspace.
2. Client begins connecting and makes connection state visible.
3. After initialization succeeds, the client loads threads and any capability-gated data needed for the default workspace view.
4. If no thread is selected, the user is shown a clear starting point for a new conversation.

### 5.1.1 Welcome Suggestions

- When the workspace is connected and the conversation area is in an empty or ready-to-start state, the client may render a small set of welcome suggestions.
- Welcome suggestions are intended to feel like likely next tasks for the current workspace, not a fixed set of product-category shortcuts.
- The client may render local fallback suggestions immediately so the empty state remains useful before any server-backed result is available.
- If the server advertises `capabilities.extensions.welcomeSuggestions`, the client may call `welcome/suggestions` for the active workspace identity after connection is ready.
- Dynamic welcome-suggestion requests are gated by the current workspace personalization setting. If the workspace has personalized welcome suggestions disabled, the client must not request them and must continue showing only its local default suggestions.
- The client should replace visible fallback suggestions only when it receives a valid workspace-specific result (`source = dynamic`) from the server cache; fresh personalization updates are picked up asynchronously after successful memory consolidation, not synchronously during connection startup.
- Server-backed dynamic suggestions may come from a persisted workspace snapshot that survives Desktop shutdown and AppServer restart. This snapshot represents the latest successful dynamic result; shutdown, failed refreshes, canceled refreshes, or insufficient new evidence must not require the client to forget an already returned dynamic suggestion set.
- If the capability is absent, the request fails, the request times out, or the server responds with `source = none`, the local fallback suggestions remain visible without forcing an error state.
- Dynamic welcome suggestions must never block welcome-screen load. If no cached dynamic result exists yet for the workspace, the static fallback suggestions stay visible without a loading placeholder.
- Dynamic suggestions should use a dedicated source icon so users can distinguish personalized recommendations from the static default shortcut set.
- Suggestions should remain short, diverse, and obviously actionable when shown in a compact list.
- Choosing a suggestion prefills the input composer with the suggestion's prompt text. It must not auto-send the message or implicitly create a thread before the user confirms submission.
- The welcome suggestion surface is advisory. It should not be treated as a durable history, a command palette, or a substitute for browsing existing threads.

### 5.1.2 Workspace Setup

- If a selected folder has no `.craft/config.json`, Desktop may show a guided setup flow before connecting to AppServer.
- The setup flow is local to Desktop and the `dotcraft setup` command; it must not depend on AppServer provider-management RPCs because the workspace is not connected yet.
- Provider setup offers only the actions needed to initialize a usable workspace: select an existing explicit provider, create a provider from an `OpenAI-Responses` or `Anthropic` template, or create a custom provider.
- Desktop guided setup must require a provider and model before creating the workspace. CLI setup may still expose skip-provider behavior for advanced automation scenarios.
- Desktop provider protocol terminology uses `OpenAI-Responses`, `OpenAI-Legacy`, and `Anthropic`. `OpenAI-Responses` writes `openai-responses`; `OpenAI-Legacy` writes `openai-chat-completions`; legacy `openai` values may be read as `OpenAI-Legacy` but must not be emitted by Desktop mutations.
- Setup shows only explicit personal providers. Existing root-level legacy `ApiKey` / `EndPoint` fields in user config are ignored by setup.
- Provider credentials and endpoints are saved to personal provider config. Workspace setup writes only provider/model selection overrides for provider-aware setup.
- Model listing and provider probing are advisory. If listing fails or is unsupported, setup must keep manual model entry available.

### 5.2 Start a New Conversation

1. User chooses to create a thread.
2. Client calls `thread/start`.
3. The new thread becomes active immediately after success.
4. The input area becomes ready for the first message.
5. If thread creation fails, the user remains in the prior safe state with a retry path.

### 5.3 Resume or Open an Existing Thread

1. User selects a thread from the navigation area.
2. Client loads the thread content with enough history to make the conversation understandable.
3. Client subscribes to future updates for the selected thread when real-time updates are needed.
4. If the thread is not turn-capable, the user sees why and which actions remain allowed.

### 5.3.1 Desktop Thread Restore Pipeline

Desktop treats opening, returning to, or restoring an existing thread as a coordinated restore operation, not as independent `thread/read` and `thread/subscribe` side effects.

1. When the user selects a thread, Desktop creates a new restore generation for that thread and clears any restore generation that belonged to the previously active thread.
2. Desktop must hydrate the selected thread from `thread/read` with turn history and establish a `thread/subscribe` observer, normally with `replayRecent = true`.
3. The `thread/read` and `thread/subscribe` requests may run serially or in parallel, but the active conversation must not expose replayed approval or user-input composers until both the current generation's history hydration and subscription readiness have completed.
4. Any `thread/read`, `thread/subscribe`, `thread/unsubscribe`, or server-to-client interactive request result that belongs to an older restore generation must be ignored for the active conversation.
5. For the same `threadId`, Desktop must serialize subscription operations. A queued or delayed `thread/unsubscribe` must not cancel a newer active `thread/subscribe` for the same thread after the user has returned.
6. Switching threads, switching workspaces, disconnecting, or closing the window must clear the active restore generation and prevent late async work from restoring UI into the wrong foreground thread.

This pipeline is a Desktop client responsibility. It does not change the AppServer protocol: `thread/read` remains the authoritative read-only snapshot, and `thread/subscribe` remains the live notification channel.

### 5.3.2 Interactive Request Restore

Pending approval and model-initiated user-input requests are part of the active turn and must survive ordinary Desktop navigation.

- Switching away from a thread is not a decline, cancel, approval timeout, empty user-input answer, or dismissal.
- If `item/approval/request` or `item/tool/requestUserInput` arrives while its source thread is not the active fully-restored thread, while conversation rendering is paused, or while deferred conversation updates have not been reconciled, Desktop parks the request on that source thread instead of presenting it immediately.
- Desktop activates parked requests only after the latest full `thread/read` generation has hydrated the active conversation. A request that arrives during an in-flight read makes that read insufficient and requires a follow-up generation before the composer may appear.
- Replayed requests are matched by logical identity: `method + threadId + turnId + requestId`. A fresh JSON-RPC envelope id is transport state and must not make the prompt a new logical request.
- Replayed requests with the same logical identity restore the actionable composer once; they must not create duplicate cards, duplicate queue entries, or duplicate local decisions.
- Multiple pending approvals for one turn are restored as a queue. The user resolves one visible approval at a time, and the next approval becomes actionable only after the prior approval has been submitted or resolved.
- Local UI submission state such as selected option, submitting, submitted, and local accepted/rejected display belongs to one logical request identity only. It must not leak from one approval or user-input request to another.
- A successful local response may be acknowledged immediately in the UI, but the thread remains running or waiting according to the latest server state until `item/approval/resolved`, `item/tool/requestUserInput/resolved`, `item/completed`, `turn/completed`, or a reconciled `thread/read` snapshot says otherwise.

### 5.3.3 Snapshot and Realtime Reconciliation

Desktop receives thread truth through two channels: durable snapshots from `thread/read` and realtime notifications from `thread/subscribe`. The UI must converge when these channels race.

- `thread/read` is the authoritative persisted snapshot for the selected thread, but a stale snapshot must not regress newer realtime state already observed by the active renderer.
- When merging a snapshot into the active conversation, Desktop must preserve already-observed terminal evidence for the same logical item or call, including completed `ToolExecution`, completed `ToolResult`, resolved approval cards, completed user-input responses, final agent messages, and terminal turn status.
- Tool calls that already have terminal evidence must not return to a live "awaiting result" display because an older snapshot only contained the `ToolCall`.
- A final `turn/completed`, `turn/failed`, or `turn/cancelled` state must clear running/waiting indicators even if an earlier local view still had live tools or composers.
- `thread/runtimeChanged` is a summary signal for thread-list and activity state. It does not replace turn/item notifications and must not be treated as complete conversation history.
- Desktop may use `thread/runtimeChanged` as a reconciliation trigger. If the server runtime says the active thread is idle while Desktop still shows running, waiting, or live awaiting-result tools, Desktop must perform a full `thread/read` with turns and reconcile the active conversation.
- An active thread with a parked approval or user-input request must keep retrying full reconciliation on foreground, reconnect, and metadata refresh paths. A failed read keeps the request parked and must not synthesize a response.
- After submitting an approval or user-input response, Desktop should continue applying live notifications normally. If live completion notifications are missed, the next full snapshot reconcile must restore completed tools, final assistant output, and terminal turn state without requiring the user to switch away and back.
- Reconciliation must be scoped to the active foreground thread and workspace. Snapshot state from one thread or workspace must not preserve or overwrite realtime state from another.

### 5.3.4 Backend Verification Gate

Before changing Desktop restore behavior for a reported restore bug, the implementer must verify whether AppServer and Session Core already contain the correct canonical state.

- Treat the bug as Desktop-owned when rollout evidence or `thread/read` contains the expected completed tool results, approval responses, final agent message, and terminal turn state, but the active Desktop UI does not show them.
- Treat the bug as AppServer or Session Core-owned when rollout evidence or `thread/read` is missing those canonical items, has impossible ordering, omits the terminal turn state, or cannot read a thread that should be readable.
- Treat replay as backend-owned when `thread/subscribe` or `thread/resume` does not replay unresolved interactive requests for a thread that is still in `waitingApproval` or `waitingInput`, or when replay creates duplicate logical requests with different `requestId` values.
- Treat subscription ordering and UI gating as Desktop-owned when backend evidence is correct but Desktop shows an approval composer before restore hydration is complete, loses a later approval, leaks local submitted state across approvals, or keeps completed tools live.
- A Desktop fix must cite the evidence source used for this classification: rollout file, `thread/read` payload, `thread/runtimeChanged` snapshot, trace/session metadata, or an AppServer protocol log.
- If backend evidence contradicts the expected Session Core or AppServer protocol behavior, backend repair takes priority over renderer workarounds.

### 5.4 Send a Message

1. User composes input and submits it.
2. If the thread is idle and turn-capable, the client calls `turn/start`.
3. The thread enters a running state and duplicate submissions for the same thread are blocked.
4. Incremental output appears as events arrive.
5. When the turn finishes, the thread returns to an idle, completed, failed, or cancelled state.

### 5.5 Input Rules

- The input area accepts plain text and any supported structured attachments or references.
- The client must prevent submission of an empty turn.
- If the thread is currently running, the client must either block a second submission on that thread or convert it into an explicit queued-follow-up behavior. The behavior must be consistent and visible to the user.
- If attachments cannot be preserved in a queued or deferred path, the user must be warned before the message is sent.

### 5.6 Approval Handling

1. An approval request arrives while a turn is running.
2. The active thread enters a waiting-approval state.
3. The approval request is surfaced with enough information for the user to decide.
4. The user can approve, decline, session-approve when supported, or cancel as allowed by the protocol surface.
5. After the decision:
   - approved work continues
   - declined work reflects rejection and may continue with an alternative path
   - cancelled work terminates the turn
6. If approval times out or is no longer valid, the user sees the resulting turn outcome.

### 5.7 User Input Request Handling

1. A user input request arrives while a turn is running.
2. The active thread enters a waiting-input state and the normal composer is replaced by a user-input composer.
3. Desktop surfaces the request in the composer location, above the normal composer footer controls, rather than as a full-screen modal.
4. Multi-question requests show previous/next question controls in the composer header. The controls are disabled at the first and last question respectively. `ArrowLeft`/`ArrowRight` navigate questions when focus is not inside an input field.
5. The user chooses one option per question by click, number key, or `ArrowUp`/`ArrowDown`; the selected row shows active arrow hints on the right. Clicking the already-selected normal option advances to the next question, or submits when it is the last question.
6. Options with an agent-provided description show an info icon; hover or keyboard focus reveals the description in a tooltip.
7. The user may use the free-form `Other` row when provided. `Other` is always a native inline input row with placeholder text; secret questions mask this input. Clicking the `Other` row only selects/focuses the input and does not auto-advance. Switching questions preserves each question's selected option and `Other` text.
8. The question composer uses the same compact type scale as the normal conversation and plan approval composer: the question text is regular UI heading weight, options are normal UI text, and the input row uses native input sizing.
9. Desktop sends the JSON-RPC response immediately and acknowledges the answer locally; the server later confirms with `item/tool/requestUserInput/resolved`.
10. If the user switches threads while a request is pending, Desktop parks the request on the source thread, shows that thread as needing an answer in the sidebar, and may show a native notification when the window is unfocused.
11. When the user returns to a thread that is still in `waitingInput` or `waitingApproval`, Desktop restores the active turn state from history/runtime hydration and accepts AppServer's replayed unresolved request even if it arrives before or after history hydration. Switching threads alone must never send an empty answer.

### 5.8 View Changes, Plans, and Tool Output

- File changes produced during a thread remain discoverable until reverted or superseded.
- Plan updates remain associated with the active thread and reflect the latest complete plan snapshot. While a `CreatePlan` tool call is still streaming its arguments, the dedicated plan surface renders a live draft (title, overview, and any fully-formed todo entries) so the user sees the plan taking shape in real time; the draft is replaced by the finalized snapshot once `plan/updated` is received.
- Tool output remains readable in-thread and must remain distinguishable from agent conversational text.
- Completed `RequestUserInput` tool results render as a question-to-answer list using the original question text and the user's selected option or free-form response, rather than exposing the raw response JSON.
- Desktop declares `backgroundTerminals = true` and treats `terminal/*` notifications as its primary live shell output data. `commandExecution` remains a persisted/compatibility projection and fallback.
- In the conversation view, shell work remains collapsed by default using the normal tool-card style. If the user expands the card, live output may be shown there while the command is still running.
- The Terminal detail surface merges terminal snapshots and command execution history for the current thread, including in-progress commands.
- If `terminal.backgroundReason = "runInBackground"`, Desktop does not keep appending subsequent process output to the inline foreground `Exec` card; the background terminal UI owns ongoing output.
- If the user switches to another thread while a command is still running, the output continues updating in the background thread state without forcing a focus change.
- Desktop does not require interactive terminal input; shell output is read-only from the Desktop client's perspective.
- The client may reveal related context automatically when new changes or plans appear, but the rule should be based on relevance, not on any fixed panel design.

Durable user-actionable result cards and available interactive Views remain outside collapsed Turn summaries. Collapsing intermediate work must not hide an action the user still needs or a completed result intended for direct review.

#### 5.8.1 Trusted Local Renderers

Desktop consumes trusted `PresentationId` and Core provenance projected by the server. It does not select local code from tool names, arguments, results, MCP metadata, Dynamic declarations, or plugin payloads. Unknown, unavailable, invalid, or provenance-mismatched descriptors use the generic tool card.

The local registry covers trusted renderers for plans, cron, skills, subagents, shell, file writes and streaming diffs, web operations, user input, file reads, todo updates, deferred tool search, and generic fallback. Conversation pinning, grouping, labels, and render plans consume the registry result instead of branching independently on tool names. The authority and audience contract is defined by [Tool Architecture Section 14.1](../architecture/tools-architecture.md#141-trusted-local-renderer-registry).

#### 5.8.2 MCP Apps Interactive Tool Views

Desktop hosts stable MCP Apps `2026-01-26` Views through the standard AppBridge initialize/initialized, ping, notification, and teardown lifecycle. Interactive UI remains optional: ineligible items, unsupported clients, offline resources, initialization failures, and revoked Views render the terminal tool result's model/text fallback.

Desktop opens a View only for a terminal `mcpToolCall` whose current projection advertises availability. It calls `mcpApp/view/open`, connects AppBridge, loads the isolated sandbox proxy, delivers the validated resource after proxy readiness, waits for View initialization, and then supplies tool input and result. Each stage has a bounded wait and uses one teardown path. History and reconnect create a new View on demand; Desktop never restores a previous handle, iframe, pending context, or bridge state.

View availability is independent of tool success. When the current projection advertises `mcpApp.available = true`, Desktop opens the View even when the terminal result has `success = false`; the original content, structured content, metadata, and `isError` value are delivered to the View unchanged. The generic failed tool card remains the fallback only when the View is unavailable, cannot be opened, or is revoked.

The host supports inline and fullscreen modes on the same handle. Picture-in-picture and persisted widget state are not supported. Theme, locale, time zone, dimensions, display mode, and standard CSS variables are delivered through host context; size and context changes are coalesced to at most ten updates per second.

Inline dimensions remain flexible. A View's `ui/notifications/size-changed` width describes the iframe viewport, not the host frame around it; Desktop accounts for host-owned borders and other inline chrome when sizing the outer frame so applying a reported width is idempotent and cannot create a resize feedback loop. Fullscreen dimensions remain fixed and ignore View resize requests.

The inner document runs in an opaque-origin sandbox with CSP, navigation, permission, and capability restrictions. It receives no Electron, Node, filesystem, shell, parent DOM, generic IPC, undeclared network, or cross-server authority. Declared camera, microphone, geolocation, and clipboard-write permissions remain denied. The View may use same-server app-visible tools and resources, logging, `ui/message`, and `ui/update-model-context` only through the handle-bound host bridge. Tool calls still pass server authority, policy, approval, hooks, timeout, and result limits.

For `ui/open-link`, Desktop asks AppServer to validate and normalize the URL before invoking the trusted shell boundary. Offline, revoked, and closed status notifications tear down the View immediately and restore generic fallback. Host-frame appearance and fullscreen visual treatment follow [Desktop DESIGN](../architecture/DESIGN.md#interactive-tool-ui); exact methods, DTOs, limits, and errors remain defined by [AppServer Protocol Section 22.10](../protocols/appserver-protocol.md#2210-mcp-apps-opaque-view-methods).

#### 5.8.3 Inline Assistant Visualizations

Desktop recognizes exact standalone `::dotcraft-inline-vis{file="example-name.html"}` directives only in completed assistant messages and outside fenced code. Invalid syntax, paths, attributes, inline code, and other prefixes remain ordinary Markdown. One message may contain multiple directives; Markdown and Views retain source order. Streaming messages do not mount a possibly incomplete directive.

Historical Views load only when their host enters the conversation scroll container's 320px preload range. Before that boundary, Desktop reserves a static content-shaped placeholder and sends no AppServer request. Loading begins with a single animated shape-matched skeleton and covers connection/thread runtime rebinding, `visualization/view/open`, sandbox bootstrap, and iframe readiness. Loaded Views remain mounted when scrolled away.

On a new Desktop connection, the first near-viewport View for a thread resumes that thread before opening the View. Concurrent opens share one connection/thread resume. A successful `thread/start` or explicit `thread/resume` satisfies the binding; connection reset clears it. `thread/read`, subscription, and thread-list loading never eagerly resume or open visualization Views.

The fragment runs in the dedicated opaque-origin sandbox and receives only readiness, coalesced resize, theme/context updates, and `window.openai.sendFollowUpMessage`. A follow-up shows a trusted Desktop confirmation with a read-only prompt; only confirmation sends the handle-bound message. Cancellation, teardown, disconnect, and stale handles reject the pending View request.

Desktop may copy the current rendered View to the system clipboard as a tightly cropped image. The capture includes the current SVG, canvas, form, and interaction state with the active theme background; it excludes host actions, loading/error UI, surrounding Markdown, and other conversation content. The host action and View surface follow [Desktop DESIGN](../architecture/DESIGN.md#inline-visualization).

Unmounting, switching threads, or disconnecting closes an acquired handle. A handle returned after cancellation is closed without mounting. Loading failure renders the localized unavailable fallback and Retry action; Retry repeats the on-demand open path. Exact directive authority, workspace ownership, and transient-file semantics are defined by [Tool Architecture Section 14.2](../architecture/tools-architecture.md#142-assistant-inline-visualization-boundary). Exact methods and errors remain defined by [AppServer Protocol Section 22.10A](../protocols/appserver-protocol.md#2210a-inline-visualization-views).

### 5.9 Interrupt a Running Turn

1. User requests interruption while a turn is running.
2. Client calls `turn/interrupt`.
3. The running state remains visible until interruption is confirmed by protocol outcome.
4. When `turn/cancelled` arrives, the client returns the thread to a safe idle state.

### 5.10 Archive and Delete

- Archived threads remain readable but not turn-capable.
- The client may expose a dedicated archived-thread management surface for browsing and restore actions.
- Restoring an archived thread returns it to the active thread set without forcing automatic navigation into that conversation.
- Deleted threads disappear from the client once deletion is confirmed.
- If a thread is archived or deleted elsewhere while open locally, the user must see the updated state immediately and lose only the actions that are no longer valid.

### 5.10 Cross-Channel Visibility

- If the server supports cross-channel thread discovery, the Desktop client may present threads whose origin differs from the desktop client itself.
- The UX contract is that origin differences must not make the thread list confusing:
  - origin may be shown when useful
  - unsupported actions must be disabled rather than failing unexpectedly
  - read and resume behavior must follow server capabilities and thread status

### 5.10.1 Thread Fork And Worktree Handoff

Desktop exposes conversation branching as a normal thread action.

Entry points:

- The active thread header overflow menu exposes a `Fork` submenu.
- The thread list context menu exposes direct fork actions for the selected row.
- Assistant response footer actions expose a compact fork button for message-level branching.

Behavior:

- Local fork calls `thread/fork` and selects the returned thread after success.
- New-worktree fork calls `worktree/createAndFork` and selects the returned thread after success.
- Assistant response fork sends a `forkPoint` for the clicked turn or item and creates a local fork.
- Failures show recoverable feedback and must not switch the active thread.
- Capability absence disables unavailable modes instead of showing actions that fail immediately.

Forked timeline:

- When history includes `systemNotice.kind = "forked"`, Desktop renders a compact localized divider at that item position.
- The divider marks where inherited source history ends and fork-specific work begins.
- The marker is informational and must not become the primary way to navigate source history.

Worktree execution:

- Desktop treats `thread.effectiveWorkspacePath` as the active file, shell, editor, and Git root for the selected thread.
- Thread list, provider settings, skill/plugin management, app bindings, memory, and workspace configuration remain scoped to the main workspace.
- Main-workspace file and Git surfaces hide `.craft/worktrees/**`; worktree-bound surfaces operate inside the selected worktree.
- Git status and branch detection for worktree threads must use Git commands scoped to `effectiveWorkspacePath`; clients must not parse `.git/HEAD` directly because linked worktrees may use `.git` files.
- Worktree indicators are compact status affordances. They should reveal branch/path details when useful without visually dominating the conversation.
- The composer may expose worktree and branch controls in a compact footer below the input chrome. These controls are outside the editable composer body.
- Desktop caches Git branch state per effective Git directory and reuses that state across welcome, local thread, and worktree thread composers. Refreshes must keep the previous branch snapshot visible until the new probe settles; the footer hides only after a local path is confirmed unavailable for Git or when the workspace is remote.
- The composer footer is provider-aware on both the welcome screen and existing threads. Git workspaces show the Git branch/worktree selector. Perforce workspaces show a changelist selector backed by AppServer `sourceControl/changelist/list`, `sourceControl/threadTarget/update`, and `sourceControl/changelist/create`; the selector label is the bare changelist number (or `default`), without a `CL ` prefix. None/non-VCS workspaces do not show a VCS selector.
- On an existing thread the changelist selector is thread-scoped: selecting or creating a changelist updates the server-side thread target and relies on `thread/updated` to keep other clients in sync.
- On the welcome screen the changelist selector is a pre-selection (mirroring how git pre-selects a worktree `baseRef`): it lists the foreground workspace's changelists with a threadId-less `sourceControl/changelist/list`, and `Create changelist…` calls `sourceControl/changelist/create` without a `threadId` (created on the foreground workspace). The pick is held in welcome state and applied as the new thread's target via `sourceControl/threadTarget/update` once the first message creates the thread; a `default` pick applies nothing.
- For a restored or newly selected local ready workspace, the launch transition must wait for both AppServer connection and the main workspace Git branch probe to settle before revealing the main workspace UI. Remote workspaces are treated as Git-settled immediately.
- On the welcome screen, `Work locally` starts a normal local thread. `New worktree` calls `worktree/createAndStart`; its branch selector chooses the worktree `baseRef` and must not switch the local checkout.
- On an existing local thread, the footer opens a confirmation dialog before calling `thread/worktree/handoff` with `mode = "worktree"`. The branch field defaults to `dotcraft/<workspace-folder-slug>`, and the current branch is sent as `baseRef`.
- On an existing worktree thread, the footer opens a confirmation dialog before calling `thread/worktree/handoff` with `mode = "local"`. The dialog presents the worktree branch and local workspace target.
- Desktop may show a local progress checklist while a handoff request is pending. These checklist rows are presentation hints and are not protocol progress notifications.
- Worktree -> local handoff checks out the worktree branch locally, applies the worktree's uncommitted changes back to the local workspace, and removes the managed worktree. If local dirty changes conflict, Desktop shows the server error and does not switch UI state.
- Branch checkout and create-and-checkout controls operate on the current effective Git directory. Local mode operates on the main workspace; worktree mode operates on the selected thread's worktree path.
- Desktop hides or disables Git worktree and branch controls for remote workspaces, missing capabilities, non-Git directories, and Perforce workspaces. Perforce changelist controls remain available in remote workspaces because all Perforce operations run through AppServer. For existing threads that are running, waiting for approval/input, or in blocking maintenance, the local/worktree handoff menu remains available, but the confirmation dialog disables the final handoff action and explains that the workspace cannot be switched while a conversation is in progress.

### 5.11 Manage Thread Goal

Desktop goal behavior is defined by [Goal Design §11.7](../features/goal.md#117-desktop-ux-contract).

At the Desktop UX level:

- Goals are exposed as a conversation control, not as an AppServer custom command.
- When `capabilities.threadGoals = true`, the slash reference surface includes a system Goal action above Commands and Skills. This capability means the server owns the complete goal runtime; Desktop only controls and displays it.
- Active goal state is summarized in the composer footer when a current thread has a goal.
- Direct `/goal` submissions are handled locally by Desktop and translated to `thread/goal/*` requests.
- Creating a goal from the welcome screen may create a thread and set the goal without starting an agent turn.
- Goal replacement requires explicit confirmation when it would replace a different non-complete objective.
- Desktop does not start automatic goal continuation turns. When an active goal continues, Desktop observes normal `turn/*` and `item/*` notifications from the server and updates goal UI from `thread/goal/updated` / `thread/goal/cleared`.
- Goal continuation user messages with `triggerKind = "goal"` must render a visible source marker, such as "Goal auto-continue" / "目标自动推进", so users can distinguish server-initiated goal work from typed input.
- SubAgent-sourced user messages with `triggerKind = "subagentFollowupTask"` or `"subagentInput"` must render a visible source marker. Desktop uses the thread-source badge copy, such as "Sent by DotCraft from another thread" / "DotCraft 从另一个会话发送", and keeps action-specific wording in the tooltip/detail; `triggerRefId` is an agent path, not a thread id. Messages with `deliveryMode = "subagentMailbox"` or `triggerKind = "subagentMailbox"` are internal, model-visible mailbox notifications and must not render as user bubbles in the main conversation.

### 5.12 Composer System Actions

The slash reference surface includes Desktop-owned system actions above custom Commands and Skills:

- Init is shown with the hint "Create an AGENTS.md file with instructions for DotCraft" only when command management is available and the workspace-scoped `command/list` result contains `/init`. AppServer omits `/init` when `.craft/AGENTS.md` is already a regular file; Desktop must not inspect the file locally. Selecting Init, or directly submitting `/init`, calls the server-managed `command/execute` method and starts a normal agent turn with the returned `expandedPrompt`. Direct execution remains safe when discovery is stale because the agent refuses to overwrite the file. Desktop reloads command availability after a turn reaches a terminal state.
- Plan mode is always shown with the label "Plan mode". Its hint reflects the current mode: "Enable Plan mode" in Agent mode and "Disable Plan mode" in Plan mode. Selecting it uses the same local mode toggle path as `Shift+Tab` and calls `thread/mode/set`.
- Manual compaction is shown as "Compact" with the hint "Compact this session's context" only when `capabilities.manualCompaction = true`, the active thread has at least one turn, and no turn is running or waiting for approval. Selecting it calls `thread/compact/start` with the active `threadId` and a long client wait timeout of 300 seconds. That timeout is only the renderer's wait limit for the request/response pair; it does not prove that server-side compaction has failed.
- Manual memory consolidation is shown as "Consolidate" / "整理" with the hint "Consolidate long-term memory" only when `capabilities.manualMemoryConsolidation = true`, the active thread has at least one turn, and no turn is running or waiting for approval. Selecting it calls `thread/memory/consolidate/start` with the active `threadId` and a long maintenance timeout of 300 seconds.
- When `system/event` or `thread/runtimeChanged` reports active maintenance (`maintenanceKind = "compacting"` or `"consolidating"`), the composer uses the same busy interaction pattern as a running turn: sending a non-empty draft queues it with `turn/enqueue`, and the empty-draft stop control calls `thread/maintenance/interrupt`.
- If a normal `turn/start` races with a maintenance transition and is rejected as busy, Desktop preserves the draft and retries through `turn/enqueue`.
- If the manual compaction request times out while `thread/runtimeChanged` or `system/event` still reports `maintenanceKind = "compacting"`, Desktop keeps the busy compacting state, preserves the stop control, and waits for the terminal `system/event`. If manual compaction returns `outcome = "skipped"` or `outcome = "failed"`, or a terminal `compactFailed` / `compactCancelled` event arrives, Desktop shows the returned or event message using the same compact status surface. Short histories should normally compact through the server's full-history fallback.
- If manual memory consolidation returns `outcome = "skipped"` or `outcome = "failed"`, Desktop shows the returned message using the same transient status surface.
- Selecting a system action from slash search clears the slash query from the composer instead of leaving `/` behind.
- Direct `/plan`, `/agent`, `/compact`, and `/consolidate` submissions are handled locally and must not start a normal agent turn. Direct `/init` is translated locally into server-managed command execution and starts a normal turn only when the server returns an expanded prompt. `/compact` and `/consolidate` show an unavailable message instead of submitting a turn when their visibility conditions are not met. On the welcome screen, Plan mode is also shown as a system action, and `/plan` / `/agent` update the pending welcome mode without starting a thread.
- Desktop updates the context ring from the RPC response when it includes `contextUsage`, and also consumes `system/event.contextUsage` on terminal compaction notifications so the ring updates even if a long manual compaction outlives the renderer request timeout. Desktop must not update the ring from `compacting` start events because their token counts are projected request estimates, not stable active-context snapshots. When a compacted `SystemNotice` item is the only event that reaches the renderer, Desktop uses its `tokensAfter` / `percentLeftAfter` fields to update an already-seeded ring instead of waiting for the next model request.

### 5.13 Desktop Runtime Thread Tools

Desktop may expose the AppServer Protocol's Desktop Thread Management Runtime Tool Profile to agents by declaring Runtime Dynamic Tools on `thread/start` and `thread/resume`.

Required behavior:

- Desktop-owned thread tools use the `desktop` namespace and DotCraft PascalCase tool names: `CreateThread`, `ListThreads`, `ReadThread`, `SendMessageToThread`, `SetThreadTitle`, `SetThreadArchived`, and `SetThreadPinned`.
- Desktop must not expose snake_case aliases as model-visible DotCraft tool names. Compatibility aliases, if needed for private integrations, must stay inside the Desktop tool handler and must not change the DotCraft tool surface.
- Desktop declares these tools with `deferLoading = true` by default so they are discoverable on demand and do not expand the ordinary top-level tool list. Direct exposure is reserved for runtimes without deferred-tool discovery.
- Desktop declares `additionalContext["desktop.threadCoordination"]` with `kind = "application"` whenever it declares the thread tools. The value is a concise App Context hint telling the agent to search for the relevant thread tool before background thread management.
- Desktop declares these tools only when it can handle `item/tool/call` requests for them on the active AppServer transport.
- Desktop implements lifecycle, history, and turn tools by calling ordinary AppServer methods. `SetThreadPinned` is the only Desktop-local state mutation in this profile and only updates Desktop settings.
- `CreateThread` calls `thread/start` using the current workspace identity, then submits the initial prompt with `turn/start`. The created thread appears through normal `thread/started` synchronization, but Desktop does not switch the user's active conversation unless the user explicitly opens it. If `reasoningEffort` is supplied, Desktop maps it into persistent thread reasoning configuration before the first turn.
- `ListThreads` calls `thread/list` with `query`, `limit`, `cursor`, and `includeArchived` when provided, then returns a model-facing page summary including `nextCursor` and `totalMatched`.
- `ReadThread` calls `thread/read` with `turnLimit` and `cursor` when provided and returns a compact payload-aware summary without resuming the thread, subscribing the UI to it, or making it active. The summary must bound turn history, summarize queued inputs, extract useful message/tool previews from item payloads, and avoid raw media data or uncapped command/tool output.
- `SendMessageToThread` sends a normal turn to the target thread without stealing focus. If `reasoningEffort` is supplied, Desktop first reads and updates the target thread configuration through `thread/config/update`; the update applies to queued and future turns. If the thread is running, waiting, or under blocking maintenance, Desktop uses `turn/enqueue` when available; otherwise the tool returns a structured busy failure.
- `SetThreadTitle` and `SetThreadArchived` map to `thread/rename`, `thread/archive`, and `thread/unarchive`. Desktop waits for the RPC result and normal broadcasts to update visible state.
- `SetThreadPinned` reads the target thread only when pinning, rejects archived or subagent child threads, updates project-scoped pinned-thread preferences, and emits a renderer settings sync so the sidebar updates immediately. Unpinning may remove the id without a successful thread read.
- On reconnect, Desktop re-declares the same tool specs and runtime additional context when it resumes a thread and `capabilities.dynamicToolRebind = true`. If rebind is unavailable, pending calls fail through the normal Runtime Dynamic Tools unavailable path rather than silently routing to stale handlers.
- Runtime thread-tool calls render as ordinary dynamic tool activity in the conversation. They are non-modal unless an underlying AppServer call triggers an existing approval or user-input flow.
- If a background-created or background-updated thread changes while the user is viewing another thread, Desktop updates the sidebar/list indicators but must not force navigation.
- Tool failures use stable error codes from the AppServer profile and a concise localized Desktop message where shown to the user.

---

## 6. Secondary Flows

### 6.1 Plugins and Skills

The sidebar Plugins entry opens a two-level surface with `Plugins` and `Skills` tabs. Plugins is the default tab when `capabilities.pluginManagement` is available; Skills remains available as the second tab when `capabilities.skillsManagement` is available.

Required behavior:

- Users can browse discovered and installable plugins, inspect plugin details, see included tools and skills, install or remove managed built-in plugins, and enable or disable installed plugins.
- Desktop launches AppServer with `DOTCRAFT_BUILTIN_PLUGIN_ROOTS` pointing at its bundled plugin resources. If that environment is absent, uninstalled built-in plugins are not shown as installable catalog entries.
- Plugin installation, removal, and enablement refresh both plugin and skill state because plugin-contained skills are controlled by the plugin lifecycle.
- Browser is the built-in reference plugin. It shows the `NodeReplJs` tool and the `browser` skill in its included content.
- Users can enter a Skills view if the server exposes skills capabilities.
- Users can browse installed skills.
- Users can inspect the content of a selected skill.
- Users can enable or disable a skill when the server supports that action.
- Users can uninstall user-managed `workspace` and `user` skills from the skill detail dialog. Uninstalling a skill also removes its workspace-local variants.
- Skills with source `plugin` show plugin attribution.
- Skills with source `plugin` are managed through the owning plugin lifecycle and do not expose a standalone skill uninstall action.
- If a skill is unavailable because server-side requirements are unmet, the client explains that the skill exists but is currently unusable.
- If plugin or skills capability is absent, the corresponding tab or action is hidden or disabled with a clear reason.

### 6.1.1 Desktop Extensions

Installed and enabled plugins may contribute trusted Desktop extensions through plugin metadata. Desktop must derive extension entry points from AppServer plugin discovery results instead of hardcoding plugin ids in the client.

Required behavior:

- Extension-provided main views appear in the sidebar only while the owning plugin is installed and enabled.
- If the current view belongs to a plugin that is disabled, removed, or no longer declares that view, Desktop moves the user to a safe built-in fallback view.
- Plugin detail pages list declared Desktop extension content alongside skills, apps, and tool integrations.
- Extension bundles load from local installed plugin files only. Desktop must not execute JavaScript directly from remote URLs.
- Extension code runs as trusted local renderer code.
- Extension host APIs expose only app surfaces declared by `requiredAppSurfaces`. Each `{ appId, surfaceId, access }` entry scopes `host.appSurfaces.getJson` to `read` and `host.appSurfaces.postJson` to `write`; the declared app ids also scope App Binding status/connection/open helpers.
- App Surface calls accept only an origin-relative path. Extension code cannot supply an absolute URL, origin, endpoint, authorization header, or bearer.
- Desktop must enforce descriptor-bound extension host capabilities in the main process from a verified plugin descriptor. Renderer-provided policy values are not an authorization source.
- For every App Surface call, Desktop main resolves `(appId, surfaceId)` through `app/surface/resolve`, proxies the GET or POST to the returned loopback HTTP(S) endpoint, and injects the returned bearer. Endpoint and bearer values never enter renderer state.
- Missing or expired publications produce the stable `AppSurfaceUnavailable` error. Desktop may show a reconnect/unavailable state but must not bypass the registry or reuse an expired resolution.
- Extension app traffic must go through `host.appSurfaces`; bundles must not rely on broad renderer `connect-src` access for app-owned local surfaces.
- Failed extension loads show a localized error state for that extension surface without breaking core conversation workflows.

### 6.2 Automations

The Automations surface remains within Desktop scope as a workflow, not a UI design.

Required behavior:

- Users can enter an Automations view if at least one relevant automation capability is available.
- The client separates capability availability from current data availability:
  - unsupported features are disabled
  - supported but empty features show empty states
- Automation data refreshes on entry and after server-side state changes.

### 6.3 Cron Jobs

Required behavior:

- Users can list cron jobs when `cronManagement` is available.
- Users can inspect each job's enabled state, recent result summary, and most recent associated thread when available.
- Users can enable, disable, or remove jobs when supported by the server.
- If job state changes elsewhere, the list refreshes through `cron/stateChanged` or explicit reload.
- If a job has a recent execution thread, users can open that thread's history for review.

### 6.4 Cron Run Review

- Reviewing a cron run is a read-only workflow.
- The review experience must expose the conversation and outputs associated with the most recent run thread.
- Users must be able to leave the review state without losing their place in the automations list.

### 6.5 Model Selection

- If model catalog capability is available, the client may offer model selection using server-provided values.
- If model catalog capability is absent or temporarily fails, the conversation workflow remains usable.
- Welcome model catalog requests are scoped to the workspace provider. Existing-thread requests are scoped to `thread.configuration.providerId` and do not follow workspace changes.
- If model listing returns `EndpointNotSupported` or another provider-neutral error, the client must keep manual model entry available.
- The combined picker exposes configured Provider and model submenus with the existing keyboard and ARIA menu behavior.
- Welcome atomically persists `providerId` and `providerModels`, then sends the complete pair in `thread/start` or `worktree/createAndStart`.
- Existing threads do not expose Default. A provider/model choice sends one full `thread/config/update`, never `workspace/config/update`, and updates local state only after success.
- If a target provider has no remembered model, Desktop selects its first listed model. If listing is unavailable, it leaves the thread unchanged and directs the user to Model Providers settings.
- Missing/deleted providers remain visible as missing thread state until the user explicitly migrates the thread.

### 6.6 Archived Threads

Required behavior:

- Users can enter an archived-thread management surface from Settings when thread-management capability is available.
- The archived-thread list follows the same workspace identity and cross-channel visibility rules as the main thread list, but queries with archived inclusion enabled.
- The archived-thread surface is read-only apart from restore actions; it does not provide message sending.
- Restoring a thread removes it from the archived list immediately and makes it eligible to reappear in the main thread list after local refresh or status synchronization.
- If a thread is restored or deleted elsewhere while the archived-thread surface is open, the visible list reconciles automatically without requiring a full app restart.

### 6.7 Settings Surface

The Settings surface remains within Desktop scope as a workflow contract rather than a visual-design specification.

Required behavior:

- Desktop follows the three-tier configuration model: live-apply fields, subsystem-restart fields, and process-restart fields.
- When `providerManagement` is available, Desktop exposes provider-aware model settings:
  - personal providers can be created, edited, tested, and deleted from Settings;
  - `openai` is a normal explicit provider id and can be created, selected, edited, and deleted like other providers when it is not the active workspace selection;
  - provider credentials and endpoints are personal config, while workspace saves write `providerId`, `model`, and the provider-keyed `providerModels` preference map;
  - provider testing uses `provider/test` and must not perform hidden chat-completion requests;
  - unsupported model listing remains a recoverable setup state with manual model entry.
- The legacy shared footer Save/Cancel pattern is retired. Settings actions are group-scoped (for example Apply, Restart, or Apply & Restart) based on the tier semantics of that group.
- The Connections settings group distinguishes lifecycle ownership:
  - Local mode shows Hub-managed AppServer actions, including Apply & Restart when local process settings change.
  - Remote mode uses Apply & Connect for URL/token changes, validates before persisting, and hides or disables local-only AppServer binary and restart controls with explanatory copy.
- When `capabilities.sourceControlManagement` is available, Desktop exposes a workspace-scoped `Source Control` tab:
  - the user selects a provider (`git`, `perforce`, `none`); any legacy stored `auto` value is normalized by AppServer to Git and is not shown as an auto-detection mode.
  - for Perforce, the user configures the connection (P4CONFIG/default or manual parameters) and runs Test Connection.
  - Test Connection and provider detection execute on the AppServer (`sourceControl/test`/`sourceControl/get`) so results reflect the workspace-owning environment in both local and remote modes; Desktop never runs `p4` locally.
  - binding is saved with `sourceControl/update`, which persists only non-sensitive fields (no password/ticket); a workspace may be bound while unverified, surfaced as a `Not verified` or offline status.
  - when a Perforce form has not passed Test Connection in the current edit session, Desktop still allows Save but persists `perforce.online = false`; only a connected test result allows saving the binding online.
  - an offline Perforce binding suppresses Git branch/worktree controls, but does not enable Perforce changelist selection or `Checkout`; the user must configure the AppServer `p4` environment and pass Test Connection to bring it online.
  - Desktop reacts to `workspace/configChanged` with the `sourceControl` region to refresh the binding status without manual refresh. Because `sourceControl/get` describes whichever workspace the foreground AppServer connection is bound to, Desktop must also re-resolve the binding whenever that connection changes — a workspace switch promotes a different connection and re-emits `connected` — so the source-control surface reflects the new foreground workspace rather than a previously cached provider.
  - When `sourceControl/get.capabilities.perforceChangelist = true`, Desktop replaces the Git branch footer selector with a Perforce changelist selector and changes the Thread Header commit action to `Checkout`.
  - `Checkout` calls `sourceControl/changelist/prepare` and never invokes local `window.api.git.commit`; when the description is blank, Desktop first calls `workspace/commitMessage/suggest` with `provider = "perforce"` to generate a changelist description from AppServer-side Perforce context. The Checkout dialog lets users choose the current target, another pending changelist, or `New Changelist`; `New Changelist` sends `target = "default"` so AppServer creates a numbered pending changelist during prepare. The dialog and toast copy must avoid submit/commit semantics. Desktop does not expose Perforce submit or shelve in this version.
  - A successful `Checkout` may move files that are already opened in another pending changelist into the selected target; Desktop treats the selected thread target as the user's explicit prepare intent.
- Desktop exposes a workspace-level `Personalization` tab with an `Enable personalized welcome suggestions` toggle backed by workspace config rather than client-global preferences.
- Desktop groups Personalization settings into Conversation, Learning, Memory, and Dreams cards when the corresponding capabilities are available. Empty groups are hidden.
- Toggling personalized welcome suggestions applies immediately for the active workspace. On success, the client reacts to the resulting `workspace/configChanged` notification and updates the welcome surface without requiring manual refresh or app restart.
- When `capabilities.dreams = true`, Desktop exposes Dreams under `Personalization` with controls for scheduled background memory organization, auto-update, frequency, recent-thread lookback, latest run status, a manual "Run now" action, and a "Manage Dreams" run-history entry.
- Desktop hides Dreams controls when `capabilities.dreams` is absent or false. Entering the Personalization surface loads `dreams/status`; saving Dreams settings or receiving `workspace/configChanged` with the `memory` region refreshes the status.
- Manual Dreams runs call `dreams/run`, disable the action while `running = true`, and poll `dreams/status` until the run completes or the client times out. Desktop shows concise succeeded, skipped, and failed states.
- The Dreams management surface calls `dreams/list`, shows lightweight run history, and opens Dashboard review links at `dashboardUrl#dreams/run/<runId>`. It does not show raw markdown, index diffs, or final review decisions. Pending Dreams output is not presented as prompt-active memory until it is applied through Dashboard/API, unless `Dreams.AutoApply` was enabled before the successful run.
- Edit-race policy is deterministic:
  - Tier A (live-apply) preserves local in-flight edits when the client receives an echo notification for the same logical change.
  - Tier C (process-restart staged edits) keeps pending edits local until the user applies or discards them.

### 6.8 Channel Modules

This section defines the user-visible workflow for Desktop-managed TypeScript channel modules. It intentionally omits build scripts, package-pipeline internals, IPC method names, and UI component-level design.

#### 6.8.1 Discovery and Identity

- The Desktop client may expose a Modules group in the Channels workflow for discoverable channel modules.
- Module discovery is based on static module metadata and must not require Desktop to execute module business logic just to list available modules.
- Desktop may load modules from bundled and user-installed locations; if both provide the same `moduleId`, user-installed content overrides bundled content.
- Module identity is canonicalized by `moduleId` rather than folder name.
- Invalid or incomplete module metadata must not break the full modules list; invalid entries are skipped while valid modules remain available.
- Desktop may render Channels browse and detail surfaces from optional module `interface` metadata:
  - list subtitles prefer `interface.shortDescription` and fall back to package/source identity when absent.
  - detail pages prefer `interface.longDescription` for body copy and `interface.previewPrompt` for the preview phrase.
  - package name, source, variant, transport, and capability summary are treated as technical information and belong in the detail information area rather than the browse subtitle.
- Desktop must localize module `interface` metadata with the same locale fallback rules used for module display names.

#### 6.8.2 Configuration Workflow

- Module configuration is workspace-scoped and stored in `.craft/<configFileName>`.
- Desktop must allow users to view and update module configuration values required for runtime startup.
- Configuration key semantics and descriptor contracts remain defined by [plugin-architecture.md](../architecture/plugin-architecture.md).
- Fields intended for interactive setup only are not treated as ordinary manual-entry fields in the default config workflow.

#### 6.8.3 Enable, Disable, and Runtime Expectations

- Users can explicitly enable and disable a module from Desktop.
- Enabling starts the module runtime workflow for the active workspace context.
- Disabling stops the module runtime workflow and returns the module to a non-running state.
- Saving configuration while a module is running must produce a clear message when restart or re-enable is required before changes take effect.
- On app quit or workspace switch, Desktop must not leave module runtimes in an undefined state; active module runtimes are stopped as part of lifecycle teardown.

#### 6.8.4 Module Status Semantics

- Module status is communicated through user-meaningful states, including at least not configured, connecting, connected, stopped, and error conditions.
- Desktop may derive module status from both local runtime lifecycle and server-observed channel availability, but the user-facing status must remain coherent and actionable.
- Module status is distinct from Desktop AppServer connection state. A connected AppServer session does not imply all enabled modules are connected.
- In Remote mode, server-observed channel status is authoritative when available. Desktop must not let local module process state override a remote `channel/status` result, and local module Start/Stop controls should be hidden or disabled because they do not control remote adapters.

#### 6.8.5 Interactive Setup and QR-like Flows

- If a module declares that interactive setup may be required, Desktop must provide a corresponding guided workflow.
- Desktop may consume module-produced temporary setup artifacts from `.craft/tmp/<moduleId>/...` as read-only inputs for user guidance.
- Interactive setup experiences must handle artifact refresh, expiration, and repeated setup attempts without requiring full app restart.
- If a previously ready module later re-enters an interactive-setup-required condition, Desktop must surface that requirement again and provide a recovery path.

#### 6.8.6 Variants

- Multiple module variants may exist for the same logical `channelName`.
- Desktop allows selecting which variant is active for a given channel family.
- At any given time, only one variant is active per logical channel.
- Switching variants updates the active module context and associated configuration workflow; if the previous variant is running, Desktop stops it before or during the switch.

#### 6.8.7 Refresh and Startup Restore

- Desktop supports an explicit refresh path that re-evaluates available modules without requiring full application restart.
- If Desktop supports restoring previously enabled modules on a later launch, that behavior must be best-effort:
  - missing modules are skipped safely
  - modules without valid workspace configuration are skipped safely
- Missing restore prerequisites must not block the rest of Desktop startup.

#### 6.8.8 Diagnostics and Preconditions

- Desktop must expose clear prerequisite failures for module execution (for example, missing runtime dependencies).
- Before enabling a module, Desktop validates required configuration fields and surfaces actionable guidance when data is incomplete.
- When module runtime startup or operation fails, users must receive an understandable failure signal and a next-step action (retry, reconfigure, or inspect logs).
- Diagnostics should help users distinguish setup failures, connectivity failures, and runtime crashes.

### 6.9 What's New

Desktop owns a versioned What's New surface for release highlights. It is a client UX feature and must not depend on AppServer availability beyond reaching the normal workspace UI.

- What's New release text is bundled with Desktop and keyed by Desktop app version.
- Optional media lives in the DotHarness resources repository and is downloaded, size-checked, SHA-256 verified, and cached locally by Desktop.
- After a Desktop update, the client shows unseen release highlights once the user reaches the normal workspace UI for a ready workspace and all required unseen-release media is cached.
- The automatic prompt must not appear on the welcome screen, setup wizard, setup handoff, launch interstitial, or blocking error surfaces.
- A missing or invalid last-seen version means release highlights for the current Desktop version are unseen.
- If remote media download or verification fails, the automatic prompt does not appear for that attempt and does not mark the release as seen.
- Closing any What's New prompt that contains unseen releases records the latest visible release version as seen.
- Users can reopen What's New manually from the Help menu and from the sidebar version label.
- Manual reopen shows bundled release text up to the running Desktop version, regardless of seen state, and may replace loading placeholders with cached media as downloads finish.
- When the surface displays more than one release, the newest release is shown expanded and older releases are collapsed behind a disclosure control that toggles them in place without leaving the surface.
- Release highlights are grouped by version and may include short text plus optional media.
- Missing or unloadable media must degrade to a stable text/icon presentation rather than showing broken image chrome.
- Remote What's New media must remain small enough for first-run UX expectations; each manifest entry's declared size must stay within the agreed per-card animated-asset budget, and manifest entries declaring a larger size must be rejected at load time.

### 6.10 Remote Servers

Desktop owns a **Servers** surface for managing remote DotCraft Docker stacks over SSH. The full architecture, settings schema, API contract, SSH/Compose operations, and security model are defined in [remote-server-management.md](../features/remote-server-management.md); this section states the Desktop UX workflow contract.

- The Servers surface is a dedicated Settings tab, separate from the Connections group, with **list → detail drill-in** navigation: a list of saved servers, a per-server detail view, and a back path. No new top-level navigation is added.
- A **server (host)** is an SSH target; a **stack** is one DotCraft Compose deployment on that host. One host has many stacks. Host SSH-reachability and the active Desktop session are distinct signals and must not be conflated; reachability is shown per host, and an "active" marker indicates the host whose stack is the current session.
- The server list exposes at most one primary action (Add server) and a first-run empty state explaining the feature and its prerequisites.
- The detail view exposes Test SSH, an SSH summary, a stacks section (one card per stack), and a redacted recent-operations area. When SSH is unreachable, stack actions are disabled and a redacted error with a retry is shown.
- Each stack card tiers its actions: **Open in Desktop** (the single primary; toggles to Disconnect when active) and secondary **Dashboard** / **Logs**, with **Update**, **Restart**, **Stop/Start**, **Edit**, and **Remove** in an overflow menu. Stack lifecycle operation state is shown on the card itself and must not make **Open in Desktop** look like the running action. Update-available is informational, not risk. Logs appear as an inline, bounded, redacted, monospace panel under the card.
- The stack card version slot shows only a real DotCraft AppServer/runtime version when status can read one; mutable Docker tags such as `latest` are not displayed as versions.
- Adding or editing a server uses a second-level Settings page, not a nested modal. The page collects name, SSH target, and an optional identity file override (key/agent only; no password entry or key storage), surfaces local SSH aliases/keys when available, and may offer one-click import of discovered stacks. Stack records never accept the AppServer token; token presence is shown as present/missing only.
- "Open in Desktop" reads the remote `workspace/.craft/appserver.token`, opens a local SSH tunnel, and connects through the existing remote-mode test-and-connect path (§3.1.1) using a `ws://127.0.0.1:<port>/ws` URL. Desktop must not expose remote AppServer restart; stack lifecycle (start/stop/restart) is a deployment action distinct from AppServer process restart.
- A Servers-opened stack appears in the Projects rail as a distinct remote foreground project. Its thread list, pinned threads, and welcome draft are isolated from the local workspace used before the remote connection. Disconnecting the remote project closes the tunnel/client and returns Desktop to local mode when a local workspace is available.
- There is one source of truth for the active connection. While a Servers stack is the active session, the Connections group shows a read-only "Connected via Servers ▸ &lt;host&gt; / &lt;stack&gt;" banner linking back to Servers instead of an editable raw URL; the raw URL/token form remains for the manual/advanced case.
- The visual treatment follows [Desktop DESIGN.md](../architecture/DESIGN.md): neutral-first surfaces, semantic color only for state and risk, and the neutral inverted primary for Open in Desktop.

---

## 7. Keyboard Accessibility and Localization

### 7.1 Keyboard Expectations

- High-frequency actions must be keyboard-accessible:
  - create thread
  - send message
  - interrupt turn
  - navigate threads
  - respond to approvals
  - dismiss transient blocking overlays when safe
- If a shortcut is unavailable on one platform, an equivalent keyboard path must still exist.

### 7.2 Accessibility

- All critical workflows must be usable without relying on color alone.
- Focus order must remain predictable during thread navigation, sending, approvals, and review flows.
- Approval requests and blocking errors must move focus in a way that makes the next required action clear.
- Streaming content must remain readable as it updates.
- Hidden or disabled features must communicate why they are unavailable.

### 7.3 Localization

- All client-owned user-facing strings must be localizable.
- Server-provided identifiers, model ids, thread ids, and similar protocol values must remain stable and must not be translated as routing keys.
- Changing display language must update client-owned UX within a short and predictable refresh path.
- Locale-sensitive formatting such as time and date should follow the selected language or locale policy consistently.

---

## 8. Error Handling and Recovery

### 8.1 Connection Errors

- If connection fails before initialization, the user sees a startup failure state with retry.
- If startup fails because persisted Remote mode settings contain an invalid WebSocket URL, the error type is `remote-config-invalid`. The primary action opens Settings > Connection and clears the blocking error overlay so the user can fix the URL/token or switch back to Local. Retry alone must not be the only recovery path.
- If a represented remote project fails during connect or reconnect, the user must be able to distinguish the remote project failure from the previous local workspace. Switching to a local project or choosing a remote disconnect action returns Desktop to local mode.
- If connection drops after initialization, the user sees a disconnected state and automatic recovery begins.
- The client must not silently discard active context during reconnection.

### 8.2 Thread Errors

- If a thread cannot be read, resumed, or updated, the user sees a clear failure message and remains in a safe prior context.
- If a selected thread disappears remotely, the client removes it and falls back to a safe empty or next-valid thread state.
- If another client starts a turn first, the user sees that the thread is busy rather than experiencing a silent send failure.

### 8.3 Turn Errors

- Failed turns remain visible in-thread with enough information to understand that work stopped.
- Users must be able to continue the conversation after a failure unless the thread itself is no longer valid.
- Interrupted turns and failed turns must be distinguishable in user-visible language.

### 8.4 Approval Errors

- If approval is no longer valid, times out, or cannot be delivered, the user sees the resulting turn outcome.
- If the client does not support approval handling for a given environment, that limitation must be known before a turn reaches a blocked state whenever possible.

User input request delivery follows the same reliability expectation: if the dialog cannot be shown or the request cannot be delivered, the client/server resolve it with empty answers so the turn can continue.

### 8.5 Input and Attachment Errors

- Invalid input, unsupported attachments, oversized attachments, or failed attachment preparation must be surfaced before or at submission time.
- If a degraded fallback is used, the client must say exactly what was dropped or changed.
- Search or attachment helper failures must not corrupt the rest of the composer workflow.

### 8.6 Automation Errors

- If cron list loading fails, the Automations view remains usable enough to retry.
- If a cron action fails due to stale state, the client refreshes server truth and reconciles the visible state.
- If automation review data is missing, the user sees that the run exists but cannot currently be inspected.

---

## 9. Non-Functional UX Requirements

### 9.1 Responsiveness

- Streaming text should appear quickly enough to feel live rather than batch-delivered.
- User actions such as thread selection, approval response, model-question answer, and interrupt should visibly acknowledge input immediately, even if final protocol completion arrives later.

### 9.2 Reliability

- The client must tolerate reconnects, out-of-order user navigation, and concurrent updates from other clients without corrupting visible thread state.
- Protocol capability changes across reconnects must be reflected by enabling or disabling affected UX surfaces.

### 9.3 Platform Coverage

- The UX contract applies across supported desktop platforms.
- Platform differences may change implementation details, but not the meaning of connection state, thread state, approval flow, or automation flow.

### 9.4 Accessibility and Readability

- Long-running sessions must remain understandable over time.
- Thread history, tool output, plan progress, and automation output must remain legible in the presence of long content and repeated updates.

---

## 10. Phase 2 Reserved Surface

- The Desktop client may later expose task-oriented surfaces beyond conversation, skills, and automations.
- This document reserves that expansion without defining future layout or visual form.
- Any future task-board or GitHub-tracker UX must preserve the same principles used here:
  - protocol-driven behavior
  - explicit status and recovery
  - clear separation between workflow rules and visual implementation

### 10.1 Viewer Panel (Reserved)

- Desktop reserves an auxiliary right-side **viewer panel** surface that coexists with the existing changes / plan / terminal tabs and lets users open native file viewers and embedded browser tabs without leaving the workspace.
- Chat-local file references, including absolute local paths and `file://` links, may open in the viewer panel even when the file is outside the active workspace. External local files must be served only after a user-triggered exact-file authorization; authorizing one external file must not authorize its parent directory or sibling files.
- The viewer panel must preserve the same principles this document applies to the rest of desktop behavior: protocol-driven where applicable, explicit status and recovery, and clear separation between workflow rules and visual implementation.

### 10.2 Browser Automation

- When Desktop declares the browser capability, embedded browser tabs may be controlled by the active agent through the thread-bound browser runtime.
- Desktop must declare `browserUse.backend` and may also declare `browserUse.backends` when it supports more than one browser automation backend. `desktop-iab` identifies the embedded browser backend; `chrome-extension` identifies the user's Chrome backend.
- The Desktop embedded browser runtime contract is defined in [Desktop In-App Browser Runtime](../features/desktop-inapp-browser.md).
- Agent-controlled browser tabs remain regular viewer tabs: opening a browser tab may focus it on first open, but subsequent automation updates must not steal focus from the user's current thread or active tab.
- While an agent is actively operating a browser tab, Desktop must surface an automation state on the tab chrome, including the session name when available and a concise last-action hint when useful.
- Coordinate and locator-driven browser actions should render a virtual cursor inside the page whenever the page can accept the injected overlay. Failure to render the overlay must not block the underlying browser action.
- Navigation, screenshots, DOM snapshots, console-log inspection, and coordinate input remain subject to Desktop's browser policy, including local-url defaults and external-domain approval or blocking.
