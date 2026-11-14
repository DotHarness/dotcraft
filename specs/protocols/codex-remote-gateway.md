# Codex remote control gateway

| Field | Value |
|---|---|
| Version | 0.12.0 |
| Status | Living |
| Date | 2026-11-14 |
| Parent specs | [AppServer protocol](appserver-protocol.md), [Runtime boundaries](../architecture/runtime-module-boundaries.md) |

## 1. Purpose and scope

One gateway exposes multiple authorized DotCraft workspaces as one Codex-compatible remote host. The official mobile client selects a project, opens or creates a conversation, submits coding tasks, observes output, interrupts execution and answers supported approvals and questions. Desktop and the phone operate on the same DotCraft sessions.

DotCraft.App composes one gateway inside the per-user Hub, following the native device gateway lifecycle. DotCraft.CodexGateway owns transport and projection and connects through the .NET SDK. Each workspace retains its own Hub-managed AppServer and Session Core. Hub management endpoints control the gateway; workspace RPC still goes directly through SDK connections to the owning AppServer.

Desktop exposes the gateway in Connections → Control this PC. The gateway does not embed an official Codex agent, copy session history, implement all Codex RPC, or replace DotCraft's paired-device relay.

## 2. Control, identity and authorization

Desktop and `codex-remote-control status|enable|disable|pair` control the same Hub-owned gateway. Enabled intent survives Hub restart. The host name is the machine name followed by ` · DotCraft`; identity and routing state live in the user's `.craft/codex-remote-control/`. Closing a Desktop window releases its UI subscriptions, not the gateway. Explicit Hub shutdown follows the existing native remote-access lifecycle.

Enabling authorizes all local projects in Hub's ProjectRegistry, including later additions. Project removal withdraws gateway access and observation without deleting data or interrupting source tasks. Remote-machine projects, the default Chats workspace and unregistered directories are not projects in this catalog. Canonical workspace identity follows host path semantics. A subdirectory cwd selects its owning project. Existing threads retain their workspace route, cwd and worktree execution context. Remote project/config writes cannot expand the catalog.

The application reuses its existing ChatGPT authentication for enrollment and pairing, independently of task-model credentials. Installation and enrollment identity are scoped to origin/account and survive restart. Relay tokens remain in memory. Hub observes the shared authentication store, including atomic replacement and deletion. Logout or account change releases the old host before any replacement starts; same-account token refresh does not restart it. Enabled intent remains while authentication is unavailable and resumes after sign-in. Only Hub owns the gateway state directory.

The authenticated Hub Local API exposes GET `/v1/codex-remote` and POST `/enable`, `/disable`, `/pairings` under that prefix. Status contains `state` (`off`, `connecting`, `on`, `failed`), `signedIn`, `hostName` and optional `failureCode`/`failureMessage`; `on` means relay-connected. Enabling and pairing require ChatGPT sign-in. Pairings return `pairingUrl`, nullable `manualCode` and `expiresAt`. Hub publishes `codexRemote.stateChanged` and `codexRemote.paired` on its existing SSE stream. Only the current unexpired invitation is checked for claimed status; replacement, expiry, disable and shutdown end that check.

Desktop keeps credentials in main/backend and shows the signed-out entry disabled with a provider sign-in action. CLI prints pairing details only on explicit request. Ordinary diagnostics record RPC methods and error categories, not tokens or input bodies. Disabling ends remote access, not cloud device enrollment or accepted server-owned tasks.

## 3. Enrollment and relay

The host uses the upstream backend's `wham/remote/control/server/enroll`, `refresh`, `pair`, `pair/status` and host WebSocket endpoints. Account credentials authenticate enrollment/refresh; the returned relay credential authenticates pairing and WebSocket transport. Returned server/environment identities must match the enrollment.

Protocol version 3 follows the pinned upstream transport: installation/server/name/version headers, optional subscription cursor, per-stream sequences, cumulative ACK, bounded replay buffers, approximately 100 KiB raw chunks and at most 150 KiB wire envelopes. WebSocket ping and virtual-stream liveness are distinct. Reconnection respects upstream retry guidance; only identified missing-host responses replace enrollment.

Each `(client_id, stream_id)` owns its handshake, RPC ids, pending reverse requests and SDK resources. Initialize replaces that virtual session. Reconnect deduplicates delivered messages and replays unacknowledged output within the live process. Process restart restores routing and session state, not old user input. There is no claim of cross-process exactly-once execution.

Request failures return structured RPC errors. A failed workspace connection affects its own requests and observations. An unrecoverable virtual-stream worker is retired with its resources; its fault must not escape sweep or disposal into the shared relay receive loop. Authentication and host transport failures remain host-owned. Relay framing and total-message limits apply independently of source AppServer message limits; an oversized response fails locally rather than truncating content or ending other clients.

## 4. Project discovery and routing

A gateway catalog assigns a stable project id to each registered local workspace. Both project/list/read and the client's global-state resource project the current Hub catalog without opening source connections. Project metadata comes from Hub registration, not transcript scans. Additions and removals become visible to existing clients without re-pairing; each request uses a consistent catalog snapshot. Client-maintained active/selected project state belongs to the gateway and can only reference current entries.

`fs/readFile` and the required selection-state writes operate on the single virtual resource `<codexHome>/.codex-global-state.json` using `dataBase64`. Its workspace roots, labels and project/selection fields follow the pinned client contract. These methods do not grant general filesystem access or read the official application's state.

New threads use explicit projectId/cwd when supplied, otherwise the first project in the catalog. Conflicting project/cwd choices fail. Existing thread ids resolve to their persisted source workspace; later parameters cannot move them to another runtime. An empty catalog still supports enrollment, pairing and initialization: discovery returns empty projects/threads/models, basic config returns its valid unbound shape, and operations requiring a project report `projectRequired`.

External project/thread/turn/item identities are stable across connections and gateway restart. Turn and item mappings include their source thread scope because source ids are not globally unique. Routing state stores identities and project selection, not session contents.

Enabling and project discovery do not start AppServers. Actual workspace operations ensure the required AppServer through the existing Hub/SDK flow. Cross-project conversation or model queries may connect every source in their query scope to return real aggregate results; there is no separate session index.

A virtual client owns an SDK connection per accessed workspace. Each connection retains its own source subscriptions and interactive callbacks, so unsubscribing one client cannot detach another. One backend fault is reported for its route without pretending that other workspaces disappeared.

## 5. Protocol and shared conversations

The gateway terminates Codex initialization, reporting its own identity, state directory and real platform. The fixed Codex wire version is 0.166.0, separate from DotCraft's product version. It honors experimental capabilities and notification opt-outs. Unsupported extensions return structured errors; core conversation methods are implemented rather than empty placeholders.

Authorized workspace history includes existing ordinary Desktop/CLI conversations as well as gateway-created conversations. Source internal, ephemeral and child-thread visibility rules remain authoritative.

Thread lists merge metadata across authorized workspaces, filter and globally sort before pagination. Gateway cursors bind project scope, filters, ordering and an anchored position. They are not concatenated source pages. Lists never hydrate whole transcripts.

Thread/read/resume, turns/items list/read, start, rename, archive/unarchive and unsubscribe preserve the reference response and lifecycle contract. History pages convert views, direction, exclusive item anchors and reverse cursors at the projected boundary. History, live events, current-state restoration and terminal snapshots use the same projector and ids.

Native input parts are authoritative for user-message rendering. Task notifications and mailbox drains do not become user bubbles. Commands, real file changes, MCP and dynamic tools use their native Codex types. Other user-relevant tools use dynamicToolCall with their actual arguments and results. Dedicated and generic source events correlate by scoped callId into one logical Item, preserving identity through start, completion, history pagination and restoration. Ordinary tool results are not standalone functionCallOutput or assistant messages. Web search and image generation use native presentations when their real fields are representable; remaining content stays tool content. The gateway does not invent tool success, reasoning, file diffs, media availability or child sessions.

A fixed compact projection uses trusted Core provenance and actual invocationOrigin/invocationSourceId relationships. CodeMode control wrappers and nested, purely model-context helpers do not each require a visible row. The source retains the complete history. Independent results, actual commands, mutations, external effects, failures, media and interactions remain visible; unknown or third-party tools are not hidden by name. Live, history and timeline use the same visibility and operation-identity rules, including error promotion. Real commandActions describe actual command execution, never synthetic shell commands for file APIs. Client grouping remains client-owned; no generic wire field guarantees that arbitrary dynamic tools collapse.

Assistant presentation preserves source-owned phase: commentary, partial_answer, final_answer, or unknown while streaming. Item completion alone does not imply partial_answer. The source resolves message purpose at a tool, answer or interruption boundary. Each logical assistant Item publishes one terminal item/completed; the gateway does not publish another completion at Turn end to change its phase. Started text is a baseline, deltas append once, and completed text replaces the whole snapshot. Turn completion does not substitute for a missing final Item notification.

Turn trigger, message phase and async delivery are independent. A normal main-assistant reply in a subagent-triggered Turn is still a normal reply. Only actual async delivery sets delivery=async; only genuine reasoning becomes reasoning. Tool failures and interaction status do not become fabricated model commentary. Source history and live classification agree without whole-answer buffering as the normal streaming path.

Codex summary contains only the first visible userMessage and last real agentMessage in stored order. notLoaded contains no Items; full contains the complete projected logical Items. DotCraft summary is a candidate view, not the external summary contract. Output deferral follows the Codex command/MCP deferOutput flags, outputDeferred marker and targeted items/read contract; it does not silently remove other tool content.

Thread and turn lifecycle transitions are source-owned. A steered turn does not produce a second turn/started. All terminal outcomes become turn/completed with the appropriate status, including interruption and failure. Background command completion remains observable after the turn ends.

## 6. Models, configuration and admission

The host catalog aggregates the authorized workspaces' real provider/model bindings, preserving their availability per workspace. Duplicate provider/model ids use shared supported capabilities. Config/read with cwd projects that workspace's effective defaults; the host catalog has one default derived from the first workspace. ModelProvider filters follow the Codex contract.

Explicit selections are validated against the target workspace. A model binding never selects the execution workspace, and an unavailable model never silently falls back. The phone must see truthful custom model ids and descriptions rather than an official model alias hiding another provider.

Thread creation, turn admission, supported config writes and collaborationMode share one resolver. Collaboration settings take precedence over top-level model/effort. Agent/Plan modes use existing DotCraft modes and instructions; omission, null and explicit settings follow the pinned reference where equivalent.

The existing atomic turn/start carries providerId/model/reasoning/speed, mode and workspace settings with input. Idle admission captures that configuration; active admission steers the existing turn and changes only future defaults. The gateway never approximates this with config/update followed by input.

The named permission profile dotcraft selects the existing DotCraft execution and approval chain without changing collaboration mode. permissionProfile/list exposes it as allowed; requirements select it through allowedPermissionProfiles and defaultPermissions, with danger-full-access as the available sandbox mode and user as the reviewer. Config reports default_permissions=dotcraft, sandbox_mode=danger-full-access and the effective approval policy. Thread start/resume return sandbox.type=dangerFullAccess and activePermissionProfile.id=dotcraft. No Codex outer sandbox is claimed. Selecting this profile does not auto-approve tools.

Agent/Plan map to DotCraft agent/plan independently of the profile. Prompt maps to on-request. AutoApprove and Deny both omit prompting but retain their opposite source behavior: an equivalent never roundtrip preserves the actual policy and never promotes Deny to AutoApprove. Effective defaults are resolved before returning thread settings. Named-profile and equivalent no-sandbox submissions are accepted; unsupported isolation selections are not mapped to unrelated working modes. Normal summary=none is accepted as no summary override, and null service tier has the defined standard-tier semantics. Model/effort conversion is identical in catalog, thread and nested collaboration settings; ordinary max does not enable a separate multi-agent mode.

Normal core client parameters receive explicit mappings. Unsupported tier, non-none summary, schema or other extensions are reported honestly rather than ignored. Compatible configuration writes change only defined settings; Codex config objects are not copied into DotCraft configuration files. Config/read publishes a virtual project layer under <codexHome>/projects/<projectId>/config.toml with the actual source workspace-layer version. A write's filePath routes back to that authorized workspace, then maps to its source workspace layer; the host-default virtual config path selects the first workspace.

## 7. Interactive requests

SDK connections enable approval and request-user-input support and install actual handlers. The gateway accepts downstream responses independently of ordinary request dispatch. JSON-RPC response ids, source transport ids and durable source logical request ids are distinct.

Shell approvals use commandExecution requests, genuine file mutations use fileChange requests, and file-access permissions use the corresponding native permission request when representable. Associations use actual operation/call identity supplied by the source; the latest visible tool is never guessed. Missing operation provenance is completed through the owning source contract rather than invented in the gateway.

Supported decisions are one-time accept, decline and cancel. Wider session/permanent grants or exec/network policy amendments are not converted to acceptAlways. Non-coding device/resource approvals without an honest mapping remain explicitly unsupported, not automatically accepted or disguised as commands.

Structured questions preserve question ids, options, secret/other flags, blocking semantics and answer structure. On source resolution, each connection closes its remaining pending request with serverRequest/resolved using that connection's outbound RPC id.

The first valid answer from Desktop or phone wins. Losing/late answers do not repeat execution. Disconnection abandons that callback transport, not the source logical request; resume restores outstanding requests with their stable logical identity. No fabricated approval or rejection is persisted merely because the phone disappeared.

## 8. Recovery and operation

Closing a stream, unsubscribing or stopping the gateway releases observation, not execution. Explicit interrupt targets the mapped source turn. SDK reconnection resumes observed threads at the source's ordered snapshot/feed boundary, restores current state and pending requests, and does not resubmit user input or replay historical text deltas.

Observation attachment is separate from history hydration. excludeTurns controls only thread.turns; an explicitly requested initialTurnsPage is independent and honors its view, direction and limit. Bootstrap Turn and Item cursors are independent of both fields. Nonempty history has real inclusive descending anchors; only empty scopes return null. A bootstrap Item cursor is a thread-wide historical upper boundary usable with different turnId filters; ordinary continuation remains bound to its own scope. Initial-page nextCursor is not a bootstrap substitute. Attachment never forces full history or rereads an already prepared page.

The declared wire profile supports both Turn/Item pagination and thread/timeline/list. Timeline requests take threadId, cursor and limit; nonnegative limits are normalized to the supported page range as in the reference; the newest page is selected first, entries within it are in canonical forward order, and nextCursor points earlier. Entries retain source-owned Item positions and Turn start/completion boundaries, including later background work. The source supplies bounded canonical reads; the gateway does not derive positions from timestamps, decode private cursors or store a second history. Unsupported voice/realtime history is not fabricated and activeRealtimeSessionAtPageStart is null. Timeline has no outputDeferred flag. includeTurns=true is not silently truncated to 20 Turns.

Active Item baselines and pending interaction identities are restored at the ordered boundary before their continuation is released. Detail uses actual Item pages and targeted reads, including for a running Turn; explicit full remains full rather than an undisclosed summary. Count limits do not imply byte limits. Source SDK receivers follow the AppServer server-message contract, not its smaller inbound-request limit.

Authentication availability is independent of source-data and protocol health. Local request/stream faults leave a connected host on and its signedIn state unchanged. Host failures use distinct authentication, relay and internal failure categories with accurate fallback text. Diagnostics include method, scoped identities, request view/limit/direction, excludeTurns and initial-page presence, response counts/cursor presence, elapsed time, byte counts and stable error categories. Lifecycle diagnostics contain Item type/identity/position, phase and delivery, not text or deltas. They never include credentials, request bodies or tool output.

Enrollment restart preserves the registered host. Local state and project routing survive process restart; unfinished request outcomes are recovered from source state, not assumed from relay ACK. Affected-route errors remain actionable.

The gateway and workspace AppServers run the current DotCraft contract. Starting the gateway does not restart existing Desktop or Hub processes.

Conversation content traverses the upstream relay, even with non-OpenAI inference. The implementation makes no unsupported claim of local-only transport, end-to-end encryption or upstream retention policy.

## 9. Acceptance

- One Hub-owned host, controlled by Desktop or CLI, exposes at least two registered local projects without cross-routing, including colliding source turn/item ids.
- Enable intent and authentication changes recover without a Desktop window; project discovery starts no AppServer, and project additions/removals take effect without re-pairing.
- Projects are selectable; existing Desktop conversations open on the phone, and phone-created conversations open in the same source runtime through other DotCraft clients.
- Input, truthful model selection, streaming tools, terminal state and explicit cancellation form one shared task lifecycle.
- Supported coding approvals and questions complete through Codex wire, including rejection, another client answering first and restoration of an outstanding request.
- Metadata aggregation, history pagination, source recovery and reconnect preserve identity without transcript duplication or repeated submissions.
- Client-shaped creation and repeated submission include the named profile, summary none, collaboration settings and null service tier; returned effective settings can be resubmitted without changing source authority.
- A multi-tool Turn preserves invocation position, one operation identity, final-answer phase and exact Codex summary across history, live and recovery views, including tool completion after the final response.
- History exceeding 4 MiB opens through lightweight restoration and real detail paging without repeated input or duplicated deltas; SDK reception satisfies the source message-size contract.
- A failed RPC or virtual stream does not stop a second stream, interrupt source work or turn a source failure into an authentication failure.
- Metadata-only resume with real history loads through its bootstrap without an extra unanchored query; excludeTurns plus an initial page works, and a thread-wide Item bootstrap can filter different Turns.
- Timeline and Turn/Item opening flows, earlier pages, exact reads and reconnect preserve the same meaningful content, boundaries and identity.
- A main-assistant reply after a subagent trigger remains a single normal Markdown answer, with one terminal Item notification and no copied reasoning or phase-upgrade duplicate. Compact process presentation retains important operations and interactions.
- Development-side verification uses real isolated AppServers/SDK connections plus focused client-shaped wire regressions; user phone verification confirms history opening, folding and answer placement after integrated delivery. Schema-valid output alone is not display acceptance.
