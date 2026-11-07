# DotCraft Thread Restoration and Observation Specification

| Field | Value |
|-------|-------|
| **Version** | 0.10.3 |
| **Status** | Living |
| **Date** | 2026-11-07 |
| **Parent Specs** | [Session Core](../architecture/session-core.md), [AppServer Protocol](../protocols/appserver-protocol.md) |
| **Related Specs** | [Thread History Loading](thread-history-loading.md), [Thread Read State](thread-read-state.md), [Desktop Client](../clients/desktop-client.md), [Mobile](../clients/mobile.md), [Protocol Contract Generation](../sdk/protocol-contract-generation.md) |

Purpose: define restoration of a thread's current state and its ordered handoff to live events.

## 1. Goals

- Join a running conversation with its already-produced content and subsequent updates.
- Restore pending approvals, questions, and plan confirmation with their current identities.
- Keep opening history bounded and load further content on demand.
- Use one attachment contract across Desktop, Mobile, SDKs, CLI, ACP, and channel adapters.

## 2. Scope and Non-goals

### 2.1 Scope

Current presentation state, connection attachment, response/event ordering, bounded history loading, and observation ownership.

### 2.2 Non-goals

Execution policy, provider-native history, canonical rollout storage, read-receipt eligibility, and transcript styling remain governed by their owning specifications.

## 3. Responsibilities

| Owner | Responsibility |
|-------|----------------|
| Session Core | Execution, canonical history, current published state, cold recovery, and snapshot/observer handoff. |
| AppServer | Connection attachment, protocol projection, ordered response/event delivery, and release. |
| SDK/transport | Capture-before-request, wire ordering, and consumer ownership. |
| Product clients | Apply restored state, route interactions, merge history by identity, and retain needed observations. |

## 4. Current State and Ordered Handoff

### 4.1 Current presentation state

Core retains the latest published Turn with its Item lifecycle, cumulative streaming text, argument previews, progress, diff, usage, and pending request identities. Configuration, plan, and queued input are captured with the view.

Metadata copies no Item payloads. Summary copies selected Items. Full copies the requested complete content. These views apply to every Turn status.

The latest terminal head remains available until durable history can supply it. Older Turns belong to persistent history. Releasing the runtime releases its current view.

### 4.2 Snapshot boundary

Publication updates current state before fan-out. Snapshot capture and observer registration share that publication order.

1. Prepare or reuse the runtime and applicable bindings.
2. Capture the current view and attach the observer.
3. Write the response before delivering updates beyond the captured boundary.
4. Deliver current pending requests and continue the live feed.

Durable-page reads run outside the publication lock and execution command owner. They read the bounded prefix at or before the captured head while subsequent feed delivery waits behind the response. A head that becomes durable during the read is resolved to its durable cursor.

Rollback, runtime replacement, and deletion invalidate obsolete snapshots. Network writes and user decisions do not hold the execution owner.

### 4.3 Rejoining an attached connection

Repeated resume refreshes the existing observation in feed order. Events preceding that response may be reflected in the refreshed state. Clients apply the response by identity, then continue with subsequent updates.

### 4.4 Failure and teardown

- Failed attachment releases newly acquired resources; failed refresh preserves an existing observation.
- Closing connections reject late attachment.
- Unsubscribe and disconnect release observation, not execution or pending interactions.
- Final lifecycle delivery precedes observation closure when the connection is available.
- Restoration does not mark a thread read.

### 4.5 Recovery work selection

Warm resume uses the loaded runtime, configuration, instruction sources, and execution resources. Metadata-only warm resume reads required metadata but skips historical Turn/Item reconstruction.

Cold resume prepares execution resources and restores model context from the latest valid checkpoint and required suffix, or the necessary earlier context when no checkpoint applies. Display history is queried separately through paged storage.

View selection precedes payload hydration, copying, and serialization. Full hydration reuses selected content and retrieves missing Items.

## 5. Public Protocol Contract

### 5.1 Attachment lifecycle

| Operation | Observation behavior |
|-----------|----------------------|
| `thread/start` | Attach the creator before its response and subsequent events. |
| `thread/fork` | Attach to the new thread. |
| `worktree/createAndStart` and creation/selection commands | Attach once the resulting thread id is available. |
| `thread/resume` | Prepare/reuse the runtime and return an ordered current snapshot. |
| `thread/unsubscribe` | Idempotently release this connection's observation. |
| Connection close | Release the connection's observations. |
| Header/list/history queries | Read state without attachment or execution preparation. |
| `turn/start` | Admit execution; stream consumers establish observation first. |

Each connection has at most one observation per thread. Other authorized connections observe independently.

### 5.2 Resume response and initial page

Resume retains its thread identity and explicit binding parameters. Optional `initialTurnsPage` accepts:

- `limit`: existing Turn-page limits.
- `itemsView`: `none`, `summary` (default), or `full`.
- `summaryTools`: tool selection for summary view.

The result contains the Thread header, instruction sources, required `currentState`, and the requested descending page with continuation/backwards cursors. `currentState` carries the current Turn identity, Item count, plan eligibility, progress, diff, and pending request identities. Items belong to the selected page.

Omitting `initialTurnsPage` requests metadata-only restoration. An initial page reserves capacity for the captured head and reads the preceding durable Turns. Its continuation includes the first omitted durable Turn, including when `limit = 1`.

Summary retains the pinned-result and summary-tool rules in [Thread History Loading](thread-history-loading.md). Full applies to selected Turns, not the whole thread.

Ephemeral threads expose current process-local state without durable history.

### 5.3 Execution preparation and bindings

Cold recovery persists an abandoned Turn as interrupted before responding. Warm resume preserves the running instance and parent-owned child runtime.

Omitted bindings preserve their owners and context. Explicit replacements follow their authorization rules. A capable client may restore a vacant binding on a directly owned thread after connection loss; it does not displace a live owner. Unchanged bindings do not refresh the Agent.

Archived threads use header/history queries and are unarchived before resume.

### 5.4 Pending approvals and questions

After the response, capability-appropriate unresolved requests are delivered with their original logical identities. Waiting flags gate ordinary submission until the matching request is available.

Requests are parked by logical identity during client restoration. Resolved or withdrawn requests are removed. Multiple approvals retain serial presentation while live events continue. A referenced patch or other entity is hydrated individually when needed.

### 5.5 Plan implementation choice

Plan confirmation is separate from an approval/input request. Eligibility uses current Plan mode, the latest completed Turn's successful CreatePlan call/result, current plan content, and local dismissal state.

Apply those values together before exposing the choice. Approvals and questions take precedence. Accepting implementation confirms the scoped mode change before submitting the implementation instruction. A target or mode change superseding that operation prevents submission. Older history cannot revive a consumed choice.

## 6. Client Workflows

### 6.1 Open or cold-reopen a conversation

1. Reuse a warm attached view when available.
2. Otherwise stage incoming notifications/requests and show a cached or parallel read-only preview.
3. Resume with the five-Turn summary opening page.
4. Apply the response at the ordered transport/IPC response boundary, reconcile staged requests, then release subsequent events.
5. Expose the current interaction and continue loading visible history on demand.

Drafts retain their existing loading behavior. Action readiness follows the applied current state, not RPC Promise settlement or completion of all history reads.

Late results are scoped to their thread, workspace, and connection lifetime.

### 6.2 Warm and background observations

Desktop retains its bounded warm set and releases observations on eviction. Mobile retains visible chats and running/waiting/task chats required by its live-session contract.

### 6.3 Transcript and archived viewing

Active child transcripts resume without taking over execution bindings. Archived transcripts use read/paged history. Summary expansion, scroll anchoring, and offline snapshots follow the history-loading and client specifications.

### 6.4 Execution resources after navigation

Native resources and tasks retain observation until their Turn ends, independently of renderer navigation. Owners release their requirement when work finishes.

A native owner rejoining a running thread uses metadata-only resume without overrides. If its Turn is already terminal or no longer active, the owner completes cleanup from the snapshot.

### 6.5 SDK, CLI, ACP, and channel execution

Stream consumers register local capture before attachment and Turn admission. They filter by typed thread identity and the Turn id returned by their own `turn/start` before reducing output, usage, or completion.

A shared feed spans Turns. Persistent clients can retain it; one-shot consumers release their ownership at operation/connection end. The final local owner releases the connection attachment.

Execution owns admission, background draining, completion, and the initiating client's non-interactive policy independently of observation.

### 6.6 Bounded Turn and Item loading

- Opening uses five summary Turns.
- Detail reads one page of at most 100 Items per selected Turn, with four workers; preview hydration uses two workers.
- Per-Turn cursors remain until exhausted. Viewport demand and explicit expansion drive continuation.
- Summary and previously loaded Items are reused; known missing entities use targeted reads.
- Current Item hydration applies its base at the ordered response boundary before subsequent deltas.
- Loading/error indicators, drafts, and scroll anchors retain their existing behavior.

## 7. Constraints

Core remains the execution authority. Connection/target identity, pending request identity, and resource ownership govern their respective lifetimes. Restoring state does not trigger completion notifications, advance unread state, rerun tools, or consume suggestions.

Public protocol artifacts are generated from their owning contracts.

## 8. Acceptance Criteria

- Requested views recover current content and connect to subsequent updates exactly once.
- Repeated resume keeps one ordered observation.
- Concurrent completion and new-Turn admission preserve page limits and continuation.
- Pending interactions and plan confirmation retain identity, priority, and consumption state.
- Navigation, reconnect, and resource ownership release the correct observation.
- Read-only/archived history remains independent of runtime activation.
- Supported clients and generated contracts use the same restoration contract.
