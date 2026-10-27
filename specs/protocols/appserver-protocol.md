# DotCraft AppServer Protocol Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.6 |
| **Status** | Living |
| **Date** | 2026-10-26 |
| **Parent Spec** | [Session Core](../architecture/session-core.md) (Section 20) |
| **Related Specs** | [Plugin Architecture](../architecture/plugin-architecture.md), [.NET Extension Runtime](../architecture/dotnet-plugins.md), [Context Compaction](../architecture/context-compaction.md), [Tool Architecture](../architecture/tools-architecture.md) |

Purpose: Define a language-neutral JSON-RPC wire protocol that exposes Session Core (`ISessionService`) and related AppServer capabilities to out-of-process clients, enabling them to create and resume threads, submit turns, stream events, participate in approval flows, and call server-level management methods through one transport-stable contract.

Serialized shapes are defined by the `DotCraft.Protocol` C# contracts and the generated [AppServer JSON Schema](../../src/DotCraft.Protocol/Artifacts/AppServer/schemas/appserver.schema.json), with method/type associations in the [contract manifest](../../src/DotCraft.Protocol/Artifacts/AppServer/appserver.manifest.json). This specification owns method names, field meaning where it is not obvious from the name, authorization, lifecycle, ordering, capability gating, and failure semantics. It does not repeat field lists; examples illustrate rules and are not a second schema.

## Table of Contents

- [1. Scope](#1-scope)
- [2. Protocol Fundamentals](#2-protocol-fundamentals)
- [3. Initialization](#3-initialization)
- [4. Thread Methods](#4-thread-methods)
- [5. Turn Methods](#5-turn-methods)
- [6. Event Notifications](#6-event-notifications)
- [7. Approval Flow](#7-approval-flow)
- [8. Error Handling](#8-error-handling)
- [9. Backpressure](#9-backpressure)
- [10. Notification Opt-Out](#10-notification-opt-out)
- [11. Extension Methods](#11-extension-methods)
- [12. Versioning and Compatibility](#12-versioning-and-compatibility)
- [15. WebSocket Transport](#15-websocket-transport)
- [16. Automation management](#16-automation-management)
- [18. Skills Management Methods](#18-skills-management-methods)
- [18B. Plugin and Marketplace Management Methods](#18b-plugin-and-marketplace-management-methods)
- [18A. Tool Catalog Methods](#18a-tool-catalog-methods)
- [19. Command Management Methods](#19-command-management-methods)
- [19A. Background Terminal Methods](#19a-background-terminal-methods)
- [19B. Remote Tool Host Routing Methods](#19b-remote-tool-host-routing-methods)
- [20. Channel Status Methods](#20-channel-status-methods)
- [21. Provider And Model Catalog Methods](#21-provider-and-model-catalog-methods)
- [22. MCP Management Methods](#22-mcp-management-methods)
- [22A. Hooks Management Methods](#22a-hooks-management-methods)
- [23. External Channel Management Methods](#23-external-channel-management-methods)
- [23A. Agent Profile Management Methods](#23a-agent-profile-management-methods)
- [24. SubAgent Profile Management Methods](#24-subagent-profile-management-methods)
- [25. Configuration Methods](#25-configuration-methods)
- [25A. Source Control Methods](#25a-source-control-methods)
- [25B. File System Methods](#25b-file-system-methods)
- [25C. Command Execution Methods](#25c-command-execution-methods)
- [26. Memory Management Methods](#26-memory-management-methods)
- [27. Dreams Management Methods](#27-dreams-management-methods)
- [27A. Usage Telemetry Methods](#27a-usage-telemetry-methods)
- [28. Protocol Ownership](#28-protocol-ownership)

Section numbers are stable identifiers referenced by clients and other specs; gaps in the numbering are intentional.

## 1. Scope

### 1.1 What This Spec Defines

The wire protocol — message formats, methods, notifications, and transport rules — that a DotCraft server exposes to external clients over stdio or WebSocket. It is the network-facing projection of the Session Core `ISessionService` API plus server-level management operations exposed on the same JSON-RPC surface.

### 1.2 What This Spec Does Not Define

- **Domain model semantics**: Thread, Turn, and Item lifecycle rules, persistence layout, and state machine invariants belong to [Session Core](../architecture/session-core.md).
- **Agent execution internals**: model orchestration, tool invocation, and hook execution.
- **Feature behavior**: features exposed through management methods own their behavior; this spec keeps only the wire-level contract (method, purpose, ordering, errors, gating).
- **Client UX**: how a client renders events, approvals, or status.
- **Serialized shapes**: owned by `DotCraft.Protocol` and the generated schema.

### 1.3 Contract Snapshot

| Bucket | Meaning |
|-------|----------|
| **Guaranteed** | The thread, turn, item, event, and approval surface in Sections 4-7, plus every management method whose capability flag the server advertises ([Section 3.2](#32-initialize)). |
| **Guaranteed with narrowed semantics** | `thread/list` is deterministic with optional cursor pagination; archived threads are excluded unless a filter includes them. `thread/read` returns only the Thread header; history is read through the paged methods in [Section 4.4](#44-threadread). |
| **Discoverable extensions** | Extension namespaces advertised in `serverInfo.extensions` and `capabilities.extensions` ([Section 11](#11-extension-methods)). Clients treat them as optional. |

With multiple connections, the broadcast thread notifications in [Section 6.1](#61-thread-notifications) keep thread lists and per-thread activity indicators synchronized without subscribing to every thread.

## 2. Protocol Fundamentals

### 2.1 JSON-RPC 2.0

The wire protocol is **JSON-RPC 2.0** with `"jsonrpc": "2.0"` on every message. Requests and notifications flow in both directions:

- Client → server requests: thread, turn, and management operations.
- Server → client notifications: the event stream.
- Server → client requests: approvals, user input, and client-hosted tool or channel calls.
- Client → server notification: `initialized`.

`id` values may be strings or integers; the server preserves type and value in responses.

### 2.2 Transports

| Transport | Wire Format | Status |
|-----------|-------------|--------|
| stdio | Newline-delimited JSON: one UTF-8 JSON-RPC message per line over stdin (client→server) and stdout (server→client). Diagnostics go to stderr. One client per server process. | Primary |
| WebSocket | One JSON-RPC message per text frame on `ws://HOST:PORT/ws`. Multiple independent connections, each with its own initialization state and subscriptions. See [Section 15](#15-websocket-transport). | Experimental |

### 2.3 Serialization Rules

- Property names and enum values are **camelCase**.
- Timestamps are **ISO 8601 UTC** strings.
- Nullable fields are omitted when `null` unless a method states otherwise.
- Wire DTOs are distinct from on-disk persistence models; the wire contract stays lossless and transport-stable.
- Session Core `item/delta` events project to specific wire methods (`item/agentMessage/delta`, `item/reasoning/delta`, `item/toolCall/argumentsDelta`, `item/commandExecution/outputDelta`, `item/commandExecution/terminalInteraction`). Delta notifications that can represent multiple logical kinds carry `deltaKind`.

## 3. Initialization

### 3.1 Handshake

The client sends `initialize` as the first message on a connection. Any other method before it fails with `-32002` (`Not initialized`); a repeated `initialize` fails with `-32003` (`Already initialized`).

After the `initialize` response the client sends the `initialized` notification. Until then ordinary requests are rejected with an invalid-request error saying the server awaits `initialized`. The server begins sending notifications only after `initialized`.

### 3.2 `initialize`

**Direction**: client → server (request)

**Params**: `clientInfo` (`name`, optional `title`, `version`) and `capabilities`. Client capabilities:

| Capability | Default | Meaning |
|-------|------|-------------|
| `approvalSupport` | `true` | Client handles server-initiated approval requests. |
| `requestUserInputSupport` | `false` | Client handles `item/tool/requestUserInput`. |
| `streamingSupport` | `true` | Client consumes `item/*/delta` notifications. |
| `commandExecutionStreaming` | `false` | Client consumes `commandExecution` items, `item/commandExecution/outputDelta`, and `item/commandExecution/terminalInteraction`. |
| `toolExecutionLifecycle` | `false` | Client consumes `toolExecution` items and `item/toolExecution/progress`. |
| `backgroundTerminals` | `false` | Client consumes `terminal/*` notifications. |
| `configChange` | `true` | Client receives `config/changed` (opt-out). |
| `mcpApps` | `false` | Connection hosts MCP Apps views and accepts `mcpApp/view/*`. |
| `visualizations` | `false` | Connection hosts assistant visualization views. |
| `generativeUi` | `false` | Connection renders Generative UI blocks and saves view state through `generativeUi/state/*`. |
| `mcpElicitation` | `false` | Client answers `mcpServer/elicitation/request`. Without a capable client owning the thread the MCP request fails with a client-unavailable result. |
| `optOutNotificationMethods` | none | Exact notification methods to suppress ([Section 10](#10-notification-opt-out)). |
| `channelAdapter` | omitted | Connection is the remote backend of one unified channel runtime ([Section 11.2](#112-unified-channel-runtime-remote-projection)). |
| `acpExtensions` | omitted | Client handles server-initiated `ext/acp/*` requests ([Section 11.4](#114-acp-tool-proxy)). |
| `nodeRepl` | omitted | With `browserUse` or `computerUse`, client handles `ext/nodeRepl/*` for thread-bound browser or desktop automation. `backend` names the runtime (Desktop reports `desktop-node`). |
| `browserUse` | omitted | Browser backends behind the Node REPL. |
| `computerUse` | omitted | Desktop application automation behind the Node REPL; `backend` names the runtime (Windows Desktop reports `cua-driver`). |
| `remoteHost` | `false` | The user's device is not the machine running this server, for example Desktop attached to a remote workspace. |

**Client surface**: the server resolves one client surface per connection from `initialize`, never from turn input:

| Condition | Surface id | Kind |
|-----------|------------|------|
| `channelAdapter` declared | adapter `channelName` | chat |
| `acpExtensions` declared, or `clientInfo.name` = `dotcraft-acp` | `acp` | editor |
| `clientInfo.name` = `dotcraft-desktop` | `dotcraft-desktop` | desktop app |
| `clientInfo.name` = `dotcraft-mobile` | `dotcraft-mobile` | mobile app |
| `clientInfo.name` = `dotcraft-cli` | `cli` | terminal |
| `clientInfo.name` = `oratorio` | `oratorio` | service |
| any other client | `api` | API client |

The surface id is the channel of every Turn the connection starts, enqueues, or steers (`turn.originChannel`, `initiator.channelName`). The tools run on the server's machine. That is the user's device unless the surface is chat or mobile, or the client declares `remoteHost`. What differs from the defaults the base instructions assume reaches the model as the `client_surface` WorldState section ([Prompt Composition §9](../architecture/prompt-composition.md#9-turn-input-layer)).

**`acpExtensions`**: `fsReadTextFile`, `fsWriteTextFile`, and `terminalCreate` declare support for the corresponding `ext/acp/*` families; `extensions` lists custom families (for example `["_unity"]`) for which the server may send `ext/acp/<family>/<method>`.

**`browserUse`**: `backend` is the preferred backend; `backends` optionally lists all supported backends (`desktop-iab`, `chrome-extension`); when omitted, `backend` is the only one. `protocolVersion` is the browser IAB protocol version (currently `2`). Optional flags declare cooperative cancellation of evaluations (`supportsCancel`) and of individual browser commands (`supportsCommandCancel`), typed tab finalization (`supportsTypedFinalize`), Chrome diagnostics, browser session metadata version, and result-size and timeout limits the client enforces.

**`channelAdapter`**:

- `channelName`: canonical external channel name (for example `telegram`, `feishu`); `displayName`: platform product name shown to the model and clients.
- `deliveryCapabilities`: what the adapter's `ext/channel/send` accepts. Fixed for the connection lifetime.
  - `text.format`: `plain` when the platform shows text as written; `markdown` when the sender writes Markdown and the platform renders it (natively or after adapter conversion). The adapter splits text too long for one platform message.
  - `targets.group`: whether the adapter can send to a group other than the bound chat. `targets.direct`: `any` (any user), `knownOnly` (only users who have messaged the bot), or `none`.
  - `media`: optional map keyed by delivery kind (`file`, `audio`, `image`, `video`); an absent kind is unsupported. Entries declare size, MIME, and extension limits and which sources (host path, URL, base64) and captions are supported.
- `channelTools`: optional channel tool descriptors (PascalCase names) with `inputSchema`, optional `outputSchema`, `requiresChatContext`, optional `approval`, and optional `deferLoading`.

`deferLoading` requests lazy exposure: with a provider that supports native deferred tool loading the server may omit the tool from the top-level tool list and expose it through the provider's deferred-search flow; otherwise it may use DotCraft's local tool search.

`approval` is a descriptive risk declaration; policy resolution stays server-owned:

- `kind`: server approval category — `file`, `shell`, or `remoteResource`. `remoteResource` targets non-local resources; the server asks once and does no path/command parsing.
- `targetArgument`: argument holding the approval target. Approval applies only when the call provides it as a non-empty string; an absent optional argument skips approval, while an absent required argument fails before dispatch.
- `operation` / `operationArgument`: static operation label, or the argument whose value is forwarded as the operation.

**Result**: `serverInfo` (`name` = `"dotcraft"`, `version`, `protocolVersion` per [Section 12.1](#121-protocol-version), optional `extensions`) and `capabilities`. Each server capability gates the listed surface; clients must check it before calling:

| Capability | Gates |
|-------|-------------|
| `threadManagement` | Thread CRUD. |
| `threadFork` / `sideChat` | `thread/fork`; side-chat forks through `thread/fork.sideChat`. |
| `threadSubscriptions` | Passive `thread/subscribe` observers. |
| `threadGoals` | The full goal contract: `thread/goal/*`, goal notifications, prompt-visible goal context, accounting, budget transitions, and goal tools. Automatic idle continuation still depends on server config. |
| `manualCompaction` | `thread/compact/start`. |
| `threadMaintenanceInterrupt` | `thread/maintenance/interrupt`. |
| `dynamicToolRebind` | `thread/resume.dynamicTools`. |
| `runtimeAdditionalContext` | `additionalContext` on `thread/start` and `thread/resume`. |
| `gitWorktrees` | `worktree/createAndFork`, `worktree/createAndStart`, `thread/worktree/handoff`, `worktree/list`, `worktree/status`, `worktree/remove`, `worktree/cancelCreate`, `worktree/runSetup`, `worktree/setEnvironment`. |
| `appBindingVersion` | App Binding control-plane version (`1`). |
| `appThreadInputEnqueue` | `app/threadInput/enqueue`. |
| `approvalFlow` | Server may send approval requests. |
| `requestUserInput` | Root-thread `RequestUserInput` tool and `item/tool/requestUserInput` to capable clients. |
| `modeSwitch` / `configOverride` | `thread/mode/set`; `thread/config/update`. |
| `skillsManagement` | `skills/*` methods. |
| `pluginManagement` | `plugin/list`, `plugin/view`, `plugin/skill/read`, `plugin/install`, `plugin/installLocal`, `plugin/remove`, `plugin/setEnabled`, `plugin/setTrusted`, and `plugin/snapshot/updated`. |
| `pluginConfiguration` | `plugin/config/get`, `plugin/config/mutate`. |
| `desktopPluginArtifacts` | `plugin/desktop/read`. |
| `pluginMarketplaces` | `marketplace/*` and marketplace grouping on `plugin/list`. |
| `hooksManagement` | `hooks/list`, `hooks/setState`, `hooks/trustPlugin`. |
| `toolCatalog` | `tool/list` (always `true`; no workspace dependency). |
| `commandManagement` | `command/list`, `command/execute`. |
| `channelStatus` | `channel/status`. |
| `providerManagement` | `provider/list`, `provider/test`, and provider authentication ([Section 21.6](#216-provider-authentication)). Providers are edited through configuration writes. |
| `modelCatalogManagement` | `model/list`. |
| `workspaceConfigManagement` | `config/schema`, `config/read`, `config/value/write`, `config/batchWrite`. |
| `sourceControlManagement` | `sourceControl/*` and provider extensions advertised by `sourceControl/get.capabilities`. |
| `fileSystem` | `fs/*` ([Section 25B](#25b-file-system-methods)). |
| `commandExec` | `command/exec` ([Section 25C](#25c-command-execution-methods)). |
| `memoryManagement` | `memory/reset`. |
| `dreams` | `dreams/*`. |
| `mcpManagement` | `mcp/list`, `mcp/get`, `mcp/upsert`, `mcp/remove`. |
| `mcpStatus` | `mcp/test`. |
| `mcpRuntime` | MCP runtime/control methods and notifications ([Section 22.8](#228-mcp-runtimecontrol-methods)). |
| `mcpServerOrigins` | MCP config/status DTOs carry `origin` and `readOnly`. |
| `mcpApps` | `mcpApp/view/*` ([Section 22.10](#2210-mcp-apps-opaque-view-methods)); the client must also advertise `mcpApps`. |
| `visualizations` | `visualization/view/*` and `visualization/widgetState/updated`; the client must also advertise it. |
| `generativeUi` | `generativeUi/state/*` and `generativeUi/state/updated`; reported only when the client advertises it. |
| `externalChannelManagement` | `externalChannel/*`. |
| `agentProfileManagement` | `agent/profiles/*`, including builder drafts and Agent package methods. |
| `subAgentManagement` | `subagent/profiles/*`, `subagent/settings/update`. |
| `subAgentSessions` | `subagent/children/list`, `subagent/sendMessage`, `subagent/followupTask`, `subagent/close`. |
| `usageTelemetry` | `usage/*` and `profile/insights`. Absent when tracing is disabled. |
| `remoteToolHost` | `remoteToolHost/*`. Absent when the workspace has no Remote Tool Host client. |
| `extensions` | Module capability registry keyed by extension name; `true` or extension-defined metadata means the extension is available (for example `welcomeSuggestions`). |

Extension entries with metadata:

- `extensions.dynamicWorkflows`: `{ version: 1, list, read, pause, stop, resume, notifications }`. Clients check it before using `workflow/run/*`. No method starts arbitrary scripts; workflow launch stays model-tool-owned.
- `extensions.agentImport`: `{ version: 1, sources: ["claude-code", "codex", "cursor"] }`; gates the `import/*` methods of [Section 11.7](#117-agent-import).

#### 3.2.1 Unified Channel Model

Built-in channels and external adapters share the delivery and tool-call contract. External adapters negotiate capabilities through `initialize` and serve `ext/channel/send` and `ext/channel/toolCall`.

### 3.3 `initialized`

**Direction**: client → server (notification). Params `{}`. Signals the client is ready to receive notifications.

## 4. Thread Methods

Thread methods correspond to `ISessionService` thread lifecycle operations in [Session Core, Section 5.1](../architecture/session-core.md#51-thread-lifecycle).

`thread/start`, `thread/resume`, and `thread/fork` return `{ thread, instructionSources }`. `instructionSources` is the required ordered array of absolute logical paths used by the thread's stable `AGENTS.md` snapshot (`[]` when no file contributes). Thread notifications and worktree lifecycle results do not carry it.

The `Thread` object carries lineage (`forkedFromId`), `ephemeral`, `worktree`, `gitInfo` (branch and credential-free `originUrl` last observed at a Turn start, [Session Core §4.1.1.6](../architecture/session-core.md#4116-git-context); `null` when unknown, omitted from summaries), `cwd`, `runtimeWorkspaceRoots`, and `effectiveWorkspacePath`. `cwd`/`effectiveWorkspacePath` is the root clients use for relative file, shell, Git, and editor surfaces; `runtimeWorkspaceRoots` is the full set of runtime content boundaries. Optional header state includes `queuedInputs`, `runtime`, `plan`, `contextUsage`, and `originApp` (see [Section 4.4](#44-threadread) and [Section 4.3](#43-threadlist)).

### 4.1 `thread/start`

Create a thread. The server generates the id and persists initial state unless the thread is ephemeral.

**Params**:

- `identity` (required): the `SessionIdentity` (`channelName`, `userId`, `channelContext`, `workspacePath`) that owns the thread; see [Session Core, Section 4.1.4](../architecture/session-core.md#414-sessionidentity).
- `config`: `ThreadConfiguration` ([Section 4.1.1](#411-threadconfiguration-wire-shape)); null means workspace defaults.
- `dynamicTools`: Runtime Dynamic Tools ([Section 4.1.0](#410-runtime-dynamic-tools)).
- `additionalContext`: Runtime additional context ([Section 4.1.0.2](#4102-runtime-additional-context)); requires `capabilities.runtimeAdditionalContext`.
- `chat`: channel chat binding; adapter connections only ([Section 4.1.0.3](#4103-channel-chat)).
- `historyMode`: `"server"` (default) or `"client"`.
- `displayName`: explicit display name.
- `ephemeral`: create a process-local thread that is never persisted, omitted from `thread/list` and `thread/search`, and hidden ([Section 6.1](#61-thread-notifications)). It ends when deleted or when the process exits.
- `spawnedFromThreadId`: thread that started this one on the user's behalf. Recorded as a non-subagent origin (`ThreadSource.kind` stays `"user"`) and mirrored into metadata, so the new thread is an ordinary sibling whose first message can link back. Self-references are ignored.
- `cwd`, `runtimeWorkspaceRoots`: sticky workspace roots; semantics in [Session Core thread configuration](../architecture/session-core.md#122-thread-configuration). Also accepted on `thread/resume`, `thread/fork`, and `turn/start`.

**Agent Profile resolution**: when `config.agentProfileId` is set, the server resolves the profile for the normalized workspace and compiles it into a `ThreadConfiguration` template. `agentProfileSource` and `agentProfileFingerprint` are server-resolved outputs and must not be sent. Only the runtime overlays `providerId`, `model`, `reasoning`, `speed`, and `developerInstructions` are applied on top; the resulting model configuration is normalized as one unit and persisted as a complete provider/model/reasoning/speed snapshot. A provider or model overlay reseeds omitted model options from that provider's effective preference (or capability-safe defaults). Capability or instruction overlays (tools, MCP, plugins, skills, approval policy, `agentInstructions`, `overrideBasePrompt`, workspace overrides, provenance fields) fail with `AgentProfileValidationFailed`; `data.detail` and `data.params.diagnostics` name the rejected overlays.

The server emits `thread/started` after the response. When any channel in a shared Session Core process creates a thread, the server broadcasts `thread/started` to connected clients; the initiating client may receive it from both the request handler and the broadcast and dedupes by thread id. Session-backed SubAgent child threads are always broadcast to the current connection, since they have no `thread/start` response.

#### 4.1.0 Runtime Dynamic Tools

`dynamicTools` exposes thread-scoped callback tools implemented by the AppServer client. They are bound to the connection that creates the thread, or that later resumes it with `thread/resume.dynamicTools`, and are invoked through `item/tool/call` ([Section 11.3](#113-itemtoolcall)). They are not plugin tools or a general remote tool transport; reusable services use MCP.

`DynamicToolDeclaration` is a tagged union: a top-level `function` (`name`, `description`, `inputSchema`, optional `approval`) or a `namespace` (`name`, `description`, `tools`) whose children are functions that may also set `deferLoading`. No other fields are accepted.

- Names use PascalCase operation names, must match `^[A-Za-z0-9_]+$`, and the flat form (`name` or `namespace__name`) must fit in 64 ASCII bytes. Invalid declarations are rejected, not sanitized.
- `inputSchema` must be a valid JSON Schema object.
- A top-level function has no namespace (no source-default namespace is assigned); a child maps to `(namespace, name)`. Qualified identities must be unique.
- `deferLoading: true` is valid only inside a namespace.
- `outputSchema`, `display`, `_meta`, `_meta.ui`, and `exposure` are invalid; interactive result UI uses MCP Apps.
- `approval` uses the channel-tool approval metadata ([Section 3.2](#32-initialize)); approval is evaluated before dispatch.
- Dynamic tools are not filtered by Agent Profile tool allow/deny lists; mode policy, approval policy, the owning connection lease, and Thread/Turn authority still apply.
- A native SubAgent full-history fork snapshots the parent's current declarations and owning connection onto the child before its first sampling, with an independent owner generation. Fresh and bounded forks do not inherit them.
- If the bound connection closes, calls fail with a failed `dynamicToolCall` item until a capable client resumes the thread with replacement `dynamicTools`.

#### 4.1.0.2 Runtime Additional Context

`additionalContext` attaches compact thread-bound runtime context, keyed by client-owned stable source identifiers (non-empty, at most 128 characters of letters, digits, `.`, `_`, `-`). Each entry has `kind: "application"` and a required model-visible `value` of at most 16 KiB.

The context is bound to the requesting client connection for the thread. It does not create Turns, Items, rollout records, or `ThreadConfiguration` updates. The server renders each entry inside `<app-context>...</app-context>` in a System prompt section; clients must not rely on a separate developer role.

#### 4.1.0.3 Channel Chat

`chat` describes the chat a channel adapter's thread speaks to; its delivery target is the thread's `channelContext`.

- `kind`: `"direct"`, `"group"`, or `"topic"`; optional `label` is the chat's display name.
- `membership`: `"shared"` when every forwarded member's messages arrive in the thread; `"sender"` when the thread holds one sender's messages in that chat.

A request carrying `chat` from a connection without `capabilities.channelAdapter` fails with `InvalidParams`. The binding lives in memory on the requesting connection, like runtime additional context, and ends when the connection closes; the adapter sends `chat` again when it next starts or resumes the thread. `thread/resume` without `chat` keeps the existing binding.

#### 4.1.1 `ThreadConfiguration` Wire Shape

`ThreadConfiguration` is the canonical thread-scoped configuration object. Field meaning not obvious from the name:

| Field | Semantics |
|-------|-------------|
| `agentProfileId`, `agentProfileSource`, `agentProfileFingerprint` | Provenance of the resolved Agent Profile snapshot. Runtime behavior reads the resolved fields, not the profile file. `agent/profiles/refreshThread` compares the fingerprint. |
| `agentBuilderTargetId`, `agentBuilderTargetSource` | Marks a conversational profile-builder thread editing that profile. The server exposes builder tools and a thread-scoped draft; the thread is excluded from ordinary listings. |
| `mcpServers` | Three-state: omitted/null inherits workspace and enabled plugin servers; `[]` disables those inherited origins; non-empty replaces them. Binding-origin MCP is independent and additive. |
| `mode` | Agent mode; default `agent`. |
| `extensions` | Active ACP extension prefixes. |
| `workspaceOverride` / `executionWorkspaceOverride` | Alternate workspace root / runtime execution root used for worktree-bound execution without moving thread state. |
| `cwd`, `runtimeWorkspaceRoots` | Sticky working directory and ordered roots. Omitted roots default to cwd; `[]` supplies no roots; a supplied array replaces the list. |
| `toolProfile`, `useToolProfileOnly` | Named tool profile; when `useToolProfileOnly`, only its tools. |
| `developerInstructions` | Instructions from the starting application (at most 32768 characters), rendered after role instructions as the final system prompt section, or as a developer-role item before the first user message on protocols with a developer role. Not inherited by native SubAgents. Accepted with `agentProfileId`. |
| `toolAllowList` / `toolDenyList` | Exact-name lists; deny wins. |
| `toolPolicy` | `allow`, `deny`, `agentControl`, `allowedAgentControlTools`. Omitted keeps runtime defaults. |
| `mcpPolicy` | `servers` filters by effective server name; `tools.allow`/`tools.deny` match canonical selectors (`name` or `namespace/name`, `*` wildcards), never provider flat names, source ids, or runtime names. |
| `pluginPolicy` | Source-aware `allow`/`deny` where metadata exists, falling back to tool-name denial. |
| `skillsPolicy` | `allow`/`deny` by skill name. Omitted `allow` means every enabled skill; excluded skills are left out of skill context and refused by `SkillView`. |
| `approvalPolicy` | `default`, `prompt`, `autoApprove`, or `deny` (below). |
| `reasoning` | Effort (`low` … `ultra`) and output (`none`, `summary`, `full`); absent falls back to workspace defaults. |
| `speed` | `standard` or `fast`. New threads capture the workspace value; absent means `standard`. Changes affect future and queued turns, not a running request. |
| `requireApprovalOutsideWorkspace` | `true` routes outside-workspace file/shell operations through approval; `false` rejects them without prompting; omitted uses workspace defaults. |

Approval policy:

- `default` consults the workspace default; if that is also unset or `default`, the interactive flow is used when the client supports approvals.
- `prompt` always uses the interactive flow for the thread, even when the workspace default is `autoApprove`.
- `autoApprove` accepts approval-gated operations.
- `deny` never prompts; the rejection is returned to the calling tool as its result and the turn continues. A persisted or configured `interrupt` is read as `deny`.

### 4.2 `thread/resume`

Resume an active (non-archived) thread; Session Core loads it and reconstructs the agent session. Params: `threadId`, plus optional `dynamicTools`, `additionalContext`, `chat`, `cwd`, and `runtimeWorkspaceRoots`. The server emits `thread/resumed`.

**Binding replacement**:

- `dynamicTools` (requires `capabilities.dynamicToolRebind`): omitted means no change, but only for the binding's current owning connection generation; a non-owning connection must submit a non-empty authorized replacement to take over. `[]` clears when the caller has thread authority. A non-empty array is validated completely and replaces atomically; failure leaves the previous binding. A successful change invalidates the next Turn snapshot.
- `additionalContext`: omitted keeps the binding; `{}` clears it; otherwise replaces it for this connection.
- `chat`: replaces the adapter chat binding.

**Cold-load recovery**: when resume cold-loads a persisted thread whose sole non-terminal turn is `running`, `waitingApproval`, or `waitingInput`, the prior process no longer owns it. Before returning or emitting `thread/resumed`, the server persists the Turn as `cancelled` and emits `turn/cancelled` with reason `"interrupted"`. If persistence fails, `thread/resume` fails and the thread is not cached. Existing Items, usage, and error data are kept; no response or error Items are created, and the Turn's interactive requests are not replayed. Threads already loaded in the process are unaffected.

**Pending request replay**: for a loaded thread paused in `waitingApproval` or `waitingInput`, the server re-delivers unresolved `item/approval/request` or `item/tool/requestUserInput` requests to the resuming connection when it declared the capability. The returned snapshot already contains completed assistant and reasoning Items emitted before the blocking call. Replay reuses the logical `requestId` (the JSON-RPC `id` may be fresh), is idempotent per connection for `method + threadId + turnId + requestId`, and sends multiple approvals for one thread serially so a reply timeout does not run while a prompt waits behind another.

### 4.2.1 `thread/fork`

Create a thread from a source thread's persisted history. Requires `capabilities.threadFork`.

**Params**: `threadId`; optional `path` (restricted diagnostic rollout path that must resolve under the workspace `.craft/threads` tree and match `threadId`); `forkPoint` (`turnId`, optional `itemId`, `position` `after` (default) or `before`); `identity` (omitted fields inherit); `config` overrides applied after copying the source configuration; `displayName` (defaults to the source display name, else the first retained user message); `ephemeral`; `promptSuggestion`; `sideChat`.

- `promptSuggestion` marks an ephemeral fork that predicts the next user message. It requires `ephemeral` and a full `after` boundary at the latest completed Turn with no `itemId`, and keeps the source's tool-planning role and provider cache routing identity.
- `sideChat` requires `capabilities.sideChat`, `ephemeral`, no `forkPoint`, no `promptSuggestion`, and a source that is not itself a side chat.

Semantics:

- The source is not mutated. The fork has a new id and `forkedFromId = threadId`, copies the selected history prefix, and terminates copied active or partial turns with an interrupted boundary.
- Forks do not inherit queued inputs, pending approvals or user-input requests, app bindings, active goals, or durable plan state.
- A persistent `systemNotice` item with `kind = "forked"` and `sourceThreadId` marks the copied-history boundary.
- The response and the `thread/started` broadcast contain only the new header; clients read copied history through the paged history methods.

**Side chats** are temporary conversations beside the source for questions and light exploration:

- Copies the source's complete history; when the source is running, the copy ends at an interrupted boundary while the source keeps running.
- Keeps the source's configuration, approval policy, tool-planning role, and provider cache routing identity so it reuses the cached prompt prefix.
- Hidden like other internal ephemeral threads: not listed or searched; workspace notifications reach only its subscribers.
- Before each Turn the server ensures the model-visible history contains a side-chat boundary (not a client-visible Item) that marks copied history as reference-only and asks for read-only help unless the user explicitly requests a change.
- `SpawnAgent` fails with a tool error.
- Lives until `thread/delete`, until its source is archived or deleted, or until process exit. Clients delete side chats they no longer show.

### 4.3 `thread/list`

List threads matching an identity.

**Params**:

- `identity` (required).
- `scope`: `"identity"` (default) applies Session identity and optional cross-channel matching; `"workspace"` returns every thread with the same exact `workspacePath` regardless of user, channel context, or origin, and ignores `crossChannelOrigins`. Unknown values return `InvalidParams`.
- `includeArchived` (default `false`).
- `includeSubAgents` (default `false`): include session-backed SubAgent children; children of archived parents stay hidden unless `includeArchived`. Per-parent views use `subagent/children/list`.
- `includeInternal` (default `false`): include DotCraft-owned helper threads (marked `dotcraft.internal` or with known internal origins). Diagnostics only.
- `crossChannelOrigins`: when an array, non-empty values also return threads whose `originChannel` is listed and whose `workspacePath` and `userId` match `identity`, ignoring `channelContext` ([Session Core §9.5](../architecture/session-core.md#95-cross-channel-resume-protocol)).
- `channelName`: case-insensitive post-filter on `originChannel`.
- `query`: case-insensitive text filter over id, display name, origin channel, status, and channel context.
- `limit` (1-100; 50 when only `cursor` is given; full list when both are omitted) and opaque `cursor` (invalid → `InvalidParams`).

**Result**: `{ data: ThreadSummary[], nextCursor, totalMatched }`. Summaries carry the same `worktree` and `gitInfo` as the Thread object. Ordered by `lastActiveAt` descending; filters apply before pagination; `totalMatched` counts after filters and before pagination.

- `runtime`: optional best-effort, process-local snapshot with the shape of `thread/status/changed`, for hydrating activity indicators after reconnect. Clients continue to consume `thread/status/changed` as the source of truth and treat omission as unknown, not idle.
- `originApp` (`appId`, `displayName`, optional `icon`, optional `memberId`): set when the summary's `originChannel` matches an installed App Binding app's declared origin channel. When the app declares origin members and `channelContext` matches one, the branding is the member's and `memberId` is set. Clients fall back to the generic origin badge when absent. The same attribution is attached to full Thread objects from `thread/read`, `thread/started`, `thread/updated`, `thread/resumed`, and `thread/rollback`.

### 4.3.1 `channel/list`

List discoverable origin channel names that may appear in thread metadata. Params: none.

**Result**: `{ channels: [{ name, category }] }`, where `name` is the canonical `originChannel` and `category` is `builtin`, `social`, `system` (server-defined) or `external` (configured adapters). Internal-only origins may be omitted. Sorted by category in that order, then by name (ordinal, case-insensitive).

### 4.3.2 `thread/search`

Search conversations of the server workspace's threads.

**Params**: `searchTerm` (trimmed; empty → `InvalidParams`), `limit` (default 50, max 100), `cursor` (valid only with the same other params), `sortKey` (`createdAt` default, or `lastActiveAt`), `sortDirection` (`descending` default), `archived` (when `true`, only archived threads; otherwise only non-archived).

**Result**: `{ data: [{ thread: ThreadSummary, snippet }], nextCursor }`.

The candidate set is what `thread/list` with `scope = "workspace"` finds, never including SubAgent or internal threads. A thread matches when a user or agent message contains the term as a case-insensitive fixed string. `snippet` is a whitespace-collapsed excerpt around the first match, with ellipses where it is cut.

### 4.4 `thread/read`

Read the current Thread header without resuming it. Params: `threadId`. Result: `{ thread }`.

`thread/read` is read-only: it does not load the thread, resume execution, start background services, apply execution-time configuration, or replay the rollout. The header omits historical `turns`; current state such as `queuedInputs`, `runtime`, `goal`, `plan`, and `contextUsage` is included when available. History is read only through `thread/turns/list` and `thread/items/list`.

- `plan`: the current persisted plan for the thread, in the `plan/updated` shape; clients use it to restore plan UI after switching threads.
- `contextUsage`: context-window occupancy, not cumulative billing. `tokens` (approximate occupancy), `contextWindow` (effective denominator after budget and reserve rules, not the raw catalog window), `autoCompactThreshold`, `warningThreshold`, `errorThreshold`, `percentLeft` (0.0-1.0), and optional `source` and `isEstimate`. It also appears on `thread/start` and `thread/resume` results and matching notifications. New threads report `tokens = 0`; a thread without persisted occupancy omits it. Accounting follows [Session Core](../architecture/session-core.md#52-turn-lifecycle); replacement estimates follow [Context Compaction](../architecture/context-compaction.md#context-usage).

### 4.4.1 `thread/turns/list`

Read one page of provider-neutral Turn metadata. Params: `threadId`, `cursor`, `limit` (default 20, max 100), `sortDirection` (default `descending`).

**Result**: `{ data: Turn[], nextCursor, backwardsCursor }`. Returned Turns have no `items`. `backwardsCursor` is anchored at and includes the first returned Turn, is valid with either direction so clients can read outward from any listed Turn, and is `null` for an empty page.

### 4.4.2 `thread/items/list`

Read one page of provider-neutral Items. Params: `threadId`, optional `turnId` (restrict to one Turn), `cursor`, `limit` (default 100, max 100), `sortDirection` (default `descending`).

**Result**: `{ data: [{ turnId, item }], nextCursor }`. Item position is stable; an update replaces the payload at its original ordinal.

### 4.4.3 History cursor and projection rules

- Cursors are opaque, versioned tokens bound to thread, scope (Turns, thread-wide Items, or one Turn's Items), optional Turn, a rollout ordinal, and (for `nextCursor`) direction. A mismatch, malformed token, or out-of-range limit returns `InvalidParams`.
- A cursor whose ordinal was removed by rollback remains a valid boundary; the query continues from surviving rows and never restores removed history.
- Pages apply the same connection-scoped presentation enrichment and filtering as live Item projection.
- Provider-native recovery records (such as OpenAI Responses raw history) are never returned.
- Fork, rollback, archive, unarchive, and worktree-fork results carry only Thread headers; clients invalidate affected cursors and reload pages.
- For a thread not loaded in the process, a persisted Turn still `running`, `waitingApproval`, or `waitingInput` has no execution owner: `thread/read` and history pages report it as `cancelled` and `runtime` as idle. Storage changes only when the thread is next loaded (cold-load recovery, Section 4.2).
- Ephemeral threads return `Unsupported`. If a consistent projection cannot be produced, the methods return `ThreadHistoryUnavailable`; there is no full-history fallback.

### 4.5 `thread/rollback`

Drop `numTurns` (≥ 1) turns from the end of a thread's history. The thread must not be archived or contain a `running` or `waitingApproval` turn. Only conversation history changes; workspace files and other side effects are not reverted. Result: `{ thread }` (header). Clients invalidate cursors and reload history.

### 4.6 `thread/subscribe`

Subscribe the connection to future events for a thread; multiple passive subscribers may observe one thread. Params: `threadId`, `replayRecent` (default `false`; when `true` the server may replay a small recent buffer of turn, item, and supplemental events). Result: `{}`.

Subscribing only registers the connection for the thread's live events. It does not load the thread, replay its rollout, build an agent, or run cold-load recovery; operations that execute or change the thread (`thread/resume`, `turn/start`, and similar) load it. A missing thread ends the subscription.

After subscribing, the connection may receive `thread/*`, `turn/*`, and `item/*` notifications for turns it did not originate. Lifecycle status changes, including `thread/archived` and `thread/unarchived`, are never replayed; reconnecting clients read current status from `thread/read` or `thread/list`.

If a thread loaded in the process is paused in `waitingApproval` or `waitingInput`, unresolved interactive requests are re-delivered with the same rules as `thread/resume`. Unsubscribing or switching threads never resolves, rejects, or answers an outstanding request.

### 4.7 `thread/unsubscribe`

Remove the connection's passive subscription. Params: `threadId`. Result: `{}`. Closing the connection implicitly unsubscribes all of its subscriptions.

### 4.8 `thread/archive`

Archive a thread (params `threadId`, result `{}`). Archived threads can be listed and read but not resumed or turned.

- Archiving releases the thread's channel App Bindings: accepted bindings are revoked and pending social bind-code requests are cancelled, so the conversation can bind to another thread. Social-channel resolve and accept flows also lazily revoke bindings that point at missing or archived threads.
- A top-level parent archives its full session-backed SubAgent subtree. Archiving a SubAgent child directly is invalid.
- The server emits `thread/archived`, plus `thread/appBindings/changed` for each affected binding.

### 4.9 `thread/unarchive`

Restore an archived thread to Active (params `threadId`, result `{}`), recursively restoring a parent's SubAgent subtree. The server emits `thread/unarchived`.

### 4.10 `thread/delete`

Permanently delete a thread, its session data, and its bound tracing sessions and events (params `threadId`, result `{}`). A top-level parent deletes its SubAgent subtree and graph edges; deleting a SubAgent child directly is invalid.

Deletion succeeds only after the persisted record and bound tracing data are removed. The server then broadcasts `thread/deleted` to all connections, including the caller (subject to opt-out), once per removed thread. The notification may arrive before or after the response; client cleanup on both paths must be idempotent.

### 4.11 `thread/mode/set`

Set the agent mode (for example `"plan"`, `"agent"`). Params `threadId`, `mode`; result `{}`. The server recreates the thread's execution context with the mode's tool set. After successfully persisting, it broadcasts `thread/updated` with the current configuration to eligible connections, including the caller, under the normal visibility and opt-out rules. A running Turn keeps the configuration and tool snapshot captured at its start; the new mode applies to queued and future Turns.

### 4.12 `thread/rename`

Set a thread's display name. Params `threadId`, `displayName`; result `{}`. After persisting, the server broadcasts `thread/renamed` to all connections. The same notification is used when Session Core names a thread from its first user message.

### 4.13 `thread/config/update`

Replace per-thread configuration. Params `threadId`, `config` (complete replacement; clients preserve unrelated fields); result `{}`.

- Provider changes carry non-empty `providerId` and `model` together; model-aware fields such as `reasoning` are validated against that pair before persisting.
- On success the server rebuilds the thread agent and compaction pipeline for queued and future Turns, persists, and broadcasts `thread/updated`. A running Turn keeps the configuration and tool snapshot captured at its start, except `approvalPolicy`, which applies to it at once: pending approval requests resolve as accepted under `autoApprove` and as rejected under `deny`, with the usual `item/approval/resolved` events ([Session Core](../architecture/session-core.md) §12.2).
- Replacement does not release terminal resources or revoke Runtime Dynamic Tool bindings.
- An MCP server keeps its saved OAuth client secret when the replacement names the same server and omits the secret or writes back its read mask, as for `mcp/upsert`.

### 4.14 Thread Goal Methods

AppServer projects the Session Core goal runtime. Clients check `capabilities.threadGoals`; otherwise servers return method-not-found or a capability error.

| Method | Params | Result |
|--------|--------|--------|
| `thread/goal/get` | `{ threadId }` | `{ goal?: ThreadGoal }` |
| `thread/goal/set` | `{ threadId, objective?, status?, tokenBudget? }` | `{ goal: ThreadGoal }` |
| `thread/goal/clear` | `{ threadId }` | `{ cleared: boolean }` |

`thread/goal/set` creates an active goal when none exists and updates the existing goal otherwise. `status` is `active`, `paused`, `blocked`, `usageLimited`, `budgetLimited`, or `complete`. `ThreadGoal.createdAt`/`updatedAt` are Unix seconds; the wire shape omits internal goal ids and per-source token breakdowns.

Notifications:

- `thread/goal/updated` `{ threadId, turnId?, goal }`: `turnId` is set when a running turn caused the update (accounting, or the model completing or blocking the goal).
- `thread/goal/cleared` `{ threadId }`: only when a goal was deleted.

`thread/read`, `thread/start`, `thread/resume`, and `thread/list` may include a `goal` snapshot for hydration; notifications remain the incremental source of truth.

### 4.15 `thread/compact/start`

Manually compact the model-visible context of an idle server-managed thread. Gated by `capabilities.manualCompaction`. Params `threadId`.

**Result**: `outcome` (`partial`, `skipped`, `failed`, or `cancelled`; `micro` is produced only by cold-cache tool-result clearing, never here), optional `message` (skip or failure reason), and optional updated `contextUsage`.

- Valid only for Active server-managed threads with history and no `running`/`waitingApproval` turn or active maintenance.
- The server emits `system/event` `compacting` followed by exactly one terminal event: `compacted`, `compactSkipped`, `compactFailed`, or `compactCancelled`.
- While running, `thread/status/changed` reports `maintenanceKind = "compacting"`; new input must be queued rather than submitted with `turn/start`.
- User interruption maps to `cancelled`/`compactCancelled`. Provider timeout, missing or overlong summaries, invalid provider-native output, and persistence failure map to `failed`/`compactFailed`, with the backend's machine-readable reason.

Backend selection and history replacement follow [Context Compaction](../architecture/context-compaction.md).

### 4.15.1 `thread/contextUsage/read`

Read what fills a thread's context window. Params `threadId`. Result: `{ breakdown }`, where `breakdown` is `null` until the thread has sampled a model request in this server process (including unknown or unloaded threads).

- `breakdown.contextUsage` is the thread's current `contextUsage` (Section 4.4), so client totals and categories agree.
- `breakdown.categories[]` is `{ id, tokens, onDemand, items? }`. `id` is a stable key the client localizes: `systemPrompt`, `memoryFiles` (AGENTS.md instructions, workspace bootstrap files, and the memory index in context), `skills`, `subagents`, `builtInTools`, `mcpTools`, `messages`. Categories with zero tokens are omitted. `mcpTools` carries one `{ name, tokens }` item per MCP server; `memoryFiles` carries one item per file, named by its absolute path and estimated from the file's current content.
- `onDemand = true` marks deferred tools the model can load later; they do not occupy the context window. `builtInTools` and `mcpTools` may each appear once loaded and once on demand.
- The categories with `onDemand = false` sum to `contextUsage.tokens`: instruction sections and tool schemas are local estimates from the thread's last sampled request, and `messages` is the remainder. When those estimates exceed `contextUsage.tokens`, they are scaled down to fit and `messages` is omitted.
- Compaction keeps the instruction and tool estimates; only `messages` follows the new `contextUsage` until the next sampled request.

### 4.16 `thread/maintenance/interrupt`

Interrupt active thread maintenance such as manual compaction. Gated by `capabilities.threadMaintenanceInterrupt`. With no active maintenance it is a no-op; otherwise the server cancels it and later emits the terminal `system/event` (`compactCancelled`). Completed turns and queued inputs are unaffected.

### 4.17 Worktree Methods

Gated by `capabilities.gitWorktrees`. These methods create, inspect, switch, and remove DotCraft-managed Git worktrees ([Session Core §4.1.1.2](../architecture/session-core.md#41112-worktrees)). Thread state (rollout, memory, goals, plans, app bindings, metadata) stays in the original workspace; the worktree is only the execution workspace. Managed worktree paths must resolve under `<workspace>/.craft/worktrees/`.

Creation methods share `branchName` (allocated from `Worktrees.BranchPrefix` when omitted; an existing branch is rejected), `baseRef` (defaults to the source execution workspace `HEAD`), `path`, and `copyDirtyChanges` (default `true`: tracked and non-ignored untracked changes). Creation is atomic: the thread is created or moved only after the worktree and its changes are in place, and a failure leaves no worktree or branch behind. `worktree/createAndStart` and `worktree/createAndFork` also accept `environmentPath` and `operationId` ([Section 4.17.7](#4177-environments)).

#### 4.17.1 `worktree/createAndFork`

Create a worktree, then fork `sourceThreadId` into it. Also accepts `forkPoint`, `identity` (must not move the state workspace), `config`, and `displayName` with `thread/fork` semantics. Result: `{ thread, worktree }`; the thread has `forkedFromId`, `worktree`, and `effectiveWorkspacePath` at the worktree. The source thread and working tree are not mutated.

#### 4.17.2 `worktree/createAndStart`

Create a worktree, then start a new empty thread in it. Accepts `identity` (required), `config`, `dynamicTools`, `additionalContext`, `historyMode`, and `displayName` with `thread/start` semantics. Agent Profile resolution happens before the worktree is created; a profile failure creates neither worktree nor thread. Result: `{ thread, worktree }`. The server emits `thread/started`.

#### 4.17.3 `thread/worktree/handoff`

Move an existing thread between its local workspace and a managed worktree without changing its id. Params: `threadId`, `mode` (`"worktree"`: local → worktree; `"local"`: worktree → local), the creation fields, and an optional client-chosen `operationId`. Result: `{ thread, mode, worktree?, dirtyHandoff? }`.

- The thread must be Active with no running turn, pending approval/input, queued input, or blocking maintenance.
- Local → worktree moves uncommitted changes by default (the local checkout is left without them) and sets `thread.worktree` and `executionWorkspaceOverride`. A `path` naming a managed worktree no thread is bound to reuses it on its own branch: its own uncommitted changes are stashed and kept, and `dirtyHandoff.targetStashed` is `true`. A `path` naming a managed worktree bound to another thread fails. Any other `path`, or none, creates a managed worktree.
- Worktree → local fails with `WorktreeHandoffConflict` and `params.conflictPaths` listing the local uncommitted paths when the local checkout has any uncommitted change or its state cannot be read. Otherwise it checks the worktree's branch out in the local checkout, applies the worktree's uncommitted changes there, clears `thread.worktree` and `executionWorkspaceOverride`, and removes the managed worktree. The local checkout stays on that branch.
- Worktree → local fails with `WorktreeInUse` and `params.path` when another program holds the worktree folder (for example a terminal opened in it), checked after the environment cleanup and before any other step.
- A failed step undoes the earlier ones, restoring both checkouts and their changes. Once the local checkout holds the branch and its changes, the handoff succeeds even if the worktree folder cannot be deleted; the folder is left on disk.
- While the handoff runs, the server emits `thread/worktree/handoffProgress` for each step. On success it emits `thread/updated`.

#### 4.17.4 `worktree/list`

List the managed worktrees of the workspace, including ones no thread is bound to. Params: none. Result: `{ data: ThreadWorktreeStatus[] }`; `threadId` is the bound thread, or `null`. Git worktrees outside the managed directory are not listed.

#### 4.17.5 `worktree/status`

Return `ThreadWorktreeStatus` for the worktree bound to `threadId`: a lightweight refresh path for worktree indicators (existence, whether it is a readable Git worktree, uncommitted changes, commits ahead of the recorded base and their count). Full file, diff, and commit UX uses the thread's `effectiveWorkspacePath`.

#### 4.17.6 `worktree/remove`

Remove a managed worktree and its branch. Params: `path`, `discardChanges` (default `false`), `skipCleanup` (default `false`). Refused when an Active thread is bound to it and, unless `discardChanges` is `true`, when it has uncommitted changes or commits beyond its base. A bound archived thread loses its worktree. Result: `{}`.

`ThreadWorktreeInfo` records branch, path, `baseRef`, resolved `baseHead`, current `head`, optional `ownerKind`/`ownerId`, optional `environmentPath`, and `createdAt`. Automation run worktrees set `ownerKind = "automationRun"` and `ownerId = runId`.

#### 4.17.7 Environments

A worktree can be created with a [local environment](../features/local-environments.md), whose setup script prepares it and whose cleanup script runs before it is removed ([Session Core §4.1.1.2](../architecture/session-core.md#41112-worktrees)).

- **`environmentPath`** on `worktree/createAndStart` and `worktree/createAndFork` is the absolute path of an environment file: a `.json` file directly in a `.craft/environments` folder of the source workspace or a parent up to the repository root, that parses as a valid environment. Otherwise the request fails with `-32106` `environmentInvalid` before anything is created. Without it, no setup runs and the worktree records no environment.
- **Setup.** With `environmentPath`, the server runs the setup script after the worktree is created and its files are copied, and before the thread is created. With an `operationId`, it reports `worktree/createProgress` steps and streams `worktree/setupOutput`. The result adds `setup: { status, exitCode?, outputTail }` where `status` is `succeeded`, `failed`, or `skipped` (no script for this platform). A failed setup still creates the thread and keeps the worktree.
- **`worktree/cancelCreate`** `{ operationId }` → `{}` stops a creation or `worktree/runSetup` with that id. A creation can be cancelled until its thread is saved; it then fails with `-32106` `creationCancelled` and leaves no worktree, branch, or thread. A cancel that arrives later is ignored and the creation succeeds. A cancelled `worktree/runSetup` returns `setup.status = "failed"`. Unknown or finished ids are ignored.
- **`worktree/runSetup`** `{ threadId, operationId? }` → `{ setup }` runs the setup script of the environment recorded on the thread's worktree again, as it is on disk now. It fails with `environmentInvalid` when nothing is recorded or the file is missing or invalid.
- **`worktree/setEnvironment`** `{ path, environmentPath }` → `{ worktree }` changes the environment recorded on a managed worktree; `environmentPath` is validated as above, or `null` to record none. It runs no script.
- **Cleanup.** Before removing a worktree that records an environment, the server runs its cleanup script: in `worktree/remove`, `thread/archive`, `thread/delete`, and `thread/worktree/handoff` to local (as the `cleanupEnvironment` step, before any other step). A missing file or an empty script skips it. If it fails, `worktree/remove` and the handoff fail with `-32106` `cleanupFailed` and change nothing; `worktree/remove` with `skipCleanup = true` removes without running it. Archiving or deleting still completes and keeps the worktree on disk.

### 4.18 Thread Recovery Methods

A trusted adapter surface under `capabilities.threadManagement`. The snapshot is a versioned DotCraft-owned JSON document; clients transfer it without interpreting or rewriting its Session fields. Packages live directly under the workspace-local restricted `.craft/recovery-staging` directory; arbitrary paths are rejected.

#### 4.18.1 `thread/recovery/export`

Flush and export a terminal server-managed Thread. Params: `threadId`. Result: `packagePath` (valid only for local handoff), `threadId`, `terminalTurnId` (the durable boundary actually captured), `formatVersion` (1), `byteLength`, and lowercase `sha256` of the file.

The server loads the Thread, enters maintenance under its Turn-start lock, and flushes persistence before capture. It rejects ephemeral or client-managed Threads, active Turns or maintenance, and a non-terminal newest Turn. The package contains the executable Session snapshot defined by Session Core.

#### 4.18.2 `thread/recovery/restore`

Validate and atomically install a snapshot. Params: `packagePath`, `expectedThreadId`. Result: `{ threadId }`.

Restore validates package version, Session and provider-history schemas, original Thread id, normalized workspace path, and terminal boundary before changing durable state, and succeeds only when the target Thread does not exist. Failure leaves no partial Thread. The restored state is persisted but not resumed and no synthetic Item or Turn is emitted; callers then use `thread/resume`.

Errors use JSON-RPC code `-32097` with `error.data.code` `ThreadRecoveryPackageInvalid`, `ThreadRecoveryPackageIncompatible`, `ThreadRecoveryWorkspaceMismatch`, or `ThreadRecoveryTargetExists`. A missing export source returns `ThreadNotFound`; a busy source returns `TurnInProgress`.

## 5. Turn Methods

Turn methods correspond to `ISessionService` turn lifecycle operations in [Session Core, Section 5.2](../architecture/session-core.md#52-turn-lifecycle).

### 5.1 `turn/start`

Submit user input and begin agent execution. The server creates a Turn, records the input as a `userMessage` Item, and starts the agent. Before starting, it loads the thread from persistence if needed and applies the persisted `thread.configuration`, so a cold-loaded thread never silently runs with workspace-default tooling.

The response returns immediately with the initial Turn (`running`, empty `items`); output then streams as `turn/started`, `item/*` events, and a terminal `turn/completed`, `turn/failed`, or `turn/cancelled`.

**Params**:

- `threadId`: an Active thread with no running/waiting turn or blocking maintenance; otherwise `TurnInProgress`. During an active regular Turn interactive clients use `turn/steer`; during blocking maintenance, or when steering is not accepted, `turn/enqueue`.
- `input` (`InputPart[]`): an empty array starts a Turn that continues from committed history without a new user message (used to retry a failed Turn).
- `sender`: `SenderContext` for group sessions.
- `messages`: required when the thread uses `historyMode = "client"`, forbidden for `"server"`.
- `sentAsGoal`: submit the message as the thread goal; projected onto the resulting `userMessage` item.
- `cwd`, `runtimeWorkspaceRoots`: sticky for this and subsequent turns.
- `clientUserMessageId`: also accepted by `turn/enqueue` and `turn/steer`. Clients using optimistic submission send a fresh UUID per submission (queue updates keep the existing id); user-message payloads and queued inputs echo it so clients correlate acknowledgements without content matching. Channels without optimistic presentation omit it.

**Execution ownership**: for persisted server-managed threads the AppServer, not the submitting transport, owns a started turn. If the connection drops, the server keeps consuming the turn so it completes or fails normally; the client recovers by reconnecting, subscribing, and reloading the header and history.

**Interaction with `thread/subscribe`**: when the calling connection already subscribes to the thread, turn notifications are delivered through the subscription rather than a separate inline path (the response still precedes `turn/started`). The server keeps an internal drain for the submitted turn so connection loss neither stops execution nor strands approvals. Clients that render from subscription events should subscribe before `turn/start` and should not merge inline, replayed, and local streaming state without a stable identity to deduplicate. See [Section 6.10](#610-notification-delivery-guarantees).

#### `InputPart`

| Type | Semantics |
|------|-----------|
| `text` | Literal user text only; commands, skills, and file references use their structured parts. |
| `commandRef` (`name`, `argsText`, `rawText`) | Custom-command reference. Materialized before execution; both the native reference and the materialized prompt snapshot are persisted. Built-in slash commands (`/new`, `/stop`, `/help`, `/debug`, `/automate`, …) are invalid here (`InvalidParams`) and go through `command/execute`. |
| `skillRef` (`name`) | Skill reference, materialized into a model-visible `<skill>` block with the skill name and path; instructions are not inlined (the agent loads them through `SkillView` or by reading `SKILL.md`). Skills chosen from a slash picker are still `skillRef`. |
| `fileRef` (`path`, `displayPath`) | File reference. `path` is canonical (workspace-relative or local absolute); the model sees `@path`. `displayPath` is used only for history rendering. Referencing an outside path grants no access; reads still follow file-tool approval. |
| `image` (`url`) | Base64 `data:image/...` URL, decoded payload ≤ 64 MiB. Other media types, malformed or oversized payloads → `InvalidParams`. HTTP(S) URLs → `InvalidParams` with `error.data.code = "RemoteImageUrlNotSupported"`; clients download remote images themselves. |
| `contextRef` (`context`) | Structured context record: `pastedText`, `pageReference`, `responseAnnotation`, `diffAnnotation`, `visualizationChanges` (a visualization's `path`, `title`, and non-empty client-rendered `text` listing the user's design-control changes, quoted as reference data), or `threadReferences` (a client-rendered prompt block in `text` that materializes to exactly that text, keeping model-only framing for mentioned threads out of the message's display text). Context identity, source snapshot, comment, and owned image reference persist in native input; materialization expands the record into text and image parts once. Preview data URLs are not part of the record. |
| `localImage` (`path`, `mimeType?`, `fileName?`) | Local image file; optional metadata is for UI rehydration. The model sees the image framed by `<image name=[Image #N] path="<path>">` … `</image>` (N numbers the message's local images from 1), so it can refer to the file by path. |

The user message's display text is built from text, file, command, and skill parts only. The server normalizes, validates, and locally resolves `InputPart[]` before starting the agent or persisting queued input, persists the native and materialized snapshots, and passes resolved content to Session Core. Validation is identical for `turn/start`, `turn/enqueue`, and `turn/steer`.

#### `QueuedTurnInput`

Queued inputs carry `nativeInputParts`, `materializedInputParts`, `displayText`, optional `sender`, `status` (`queued` or `guidancePending`), `createdAt`, and `readyAfterTurnId` (the active turn when queued). Trigger fields mark input synthesized by a server or app mechanism:

- `triggerKind`: for example `goal`, `automation`, `app`, `team`, `subagentFollowupTask`, `subagentMailbox`, `subagentInput`, or a task notification kind `terminal`, `subagent`, or `workflow` (a model-facing notice that a background task ended; `triggerRefId` is the task id; clients do not present it as user input).
- `triggerLabel`, `triggerRefId`: display label and stable source id for click-through or audit.
- `triggerStatus` (task notifications only): the terminal's final status or `waitingForInput` for a stall; the child Turn status for `subagent`; the run status for `workflow`.

A `userMessage` produced from a queued input preserves the trigger fields.

Queued-input materialization never dereferences image URLs. A persisted HTTP(S) image part is replaced with the model-visible text `image content omitted because remote image URLs are not supported` and the rest of the input is consumed normally.

#### `SenderContext` and provenance

`SenderContext` carries `senderId`, `senderName`, `senderRole`, `groupId`, and optional `directTarget`: the sender's direct chat in the adapter's `ext/channel/send` target syntax without channel prefix. When present, the Turn's runtime reminder states `SenderAddress: <channelName>:<directTarget>`.

`thread.originChannel` is the channel that created the thread; `turn.originChannel` is the client surface id of the connection that initiated the turn (§3.2). Each persisted Turn records an `initiator` (`channelName`, `userId`, `userName`, `userRole`, `channelContext`, `groupId`) for cross-channel replay and audit.

### 5.2 `turn/interrupt`

Request cancellation of a turn. Params `threadId`, `turnId`; result `{}`. Cancellation is asynchronous; `turn/cancelled` signals completion.

- Shutdown stops foreground external work owned by the active tool invocation, but not terminal sessions already detached to the background; those use terminal stop or cleanup operations.
- Before emitting `turn/cancelled`, the server resolves the turn's pending approvals and user-input requests ([Section 7.6](#76-pending-requests-and-multiple-connections)), finalizes streaming agent/reasoning items with their accumulated text, and persists the cancelled turn. Later turns on server-managed threads include the cancelled turn's input and partial assistant output in model context.
- After the cancelled turn is persisted, steers it had not admitted return to the queue. The queue then stays paused: nothing in it starts automatically until `turn/queue/start` resumes it or a new turn completes normally.
- Interrupting a SubAgent child's active turn stops the child as the model's `TaskStop` does: the spawn edge stays open and the parent receives a task notification ([Background Tasks](../architecture/background-tasks.md) §5).
- The server cold-loads a thread not yet in the process. If cold-load recovery finds the requested Turn non-terminal, it persists it as `cancelled`, emits `turn/cancelled` with reason `"interrupted"`, and the request succeeds idempotently.

### 5.2.1 `turn/enqueue`

Persist input in the thread FIFO queue; used during blocking maintenance or when the current execution cannot accept steering. Params `threadId`, `input` (at least one part), `sender`. Result: `{ queuedInput, queuedInputs }`.

After enqueue, remove, reorder, update, or dequeue the server emits `thread/queue/updated`.

### 5.2.2 `turn/steer`

Add input to an active regular Turn without creating a Turn. Params `threadId`, `expectedTurnId`, `input` (at least one part), `sender`. Result: `{ turnId }`.

- Succeeds only when `expectedTurnId` matches the active regular Turn and it still accepts guidance. Maintenance Turns, internal threads, and SubAgent children are rejected. A mismatch is terminal for the request; clients keep the draft and report the failure rather than enqueueing it or retargeting it.
- The input is stored atomically as a `guidancePending` queue record. No Turn is created and no `turn/started` is emitted.
- Before the next model sample after a tool round or final answer (after any compaction), every pending guidance input for the Turn is admitted in queue order. Each runs the `UserPromptSubmit` hook; an admitted input becomes a `userMessage` with `deliveryMode = "guidance"` and its queue record is removed under the same lock. A blocked input is removed without an Item and reported through `system/event` `guidanceBlocked`.
- With `InstantInterruptEnabled` (default on), a steer that arrives while the current model request is streaming, including during reasoning or assistant text, or while a failed stream awaits retry, makes the server abandon and reissue that request with the steer admitted. Output already streamed stays in the Turn, and its complete tool calls run before the reissue.

### 5.2.3 `turn/queue/remove`

Remove one queued input without starting a Turn. Params `threadId`, `queuedInputId`. Result: `{ queuedInputs }`.

### 5.2.4 `turn/queue/reorder`

Replace the queue order. Params `threadId`, `orderedQueuedInputIds` (must be exactly the current set: no missing, duplicate, or unknown ids). Result: `{ queuedInputs }`.

### 5.2.5 `turn/queue/update`

Set a queued input's delivery status before admission. Params `threadId`, `expectedTurnId`, `queuedInputId`, `status` (`queued` or `guidancePending`). Result: `{ queuedInputs }`.

- `guidancePending` requires an active regular Turn matching `expectedTurnId`; the change is made under the per-thread queue lock and broadcast. Repeating it is idempotent. Admission follows `turn/steer`, rechecking status and target Turn under the lock.
- `queued` is idempotent for an already-queued item. A `guidancePending` item returns to its queue position when its bound Turn matches `expectedTurnId` (the Turn need not still be active). If admission already won the lock, the update fails with not-found. Admitted input is never retracted.
- When the Turn ends before admission, its pending items return to `queued`.

### 5.2.6 `turn/queue/start`

Start one queued input as a new Turn now. Params `threadId`, optional `queuedInputId` (default: the first `queued` item). Result: `{ turnId }`.

- Fails with `-32012` while a Turn or blocking maintenance is active, leaving the queue unchanged, and with not-found for an unknown `queuedInputId`.
- A queue paused because the latest Turn was cancelled resumes: after this Turn completes, the rest of the queue starts in order. `thread/read` reports the pause as `runtime.queuePaused`.

### 5.3 `workspace/commitMessage/suggest`

Suggest a source-control summary from the source thread's recent conversation plus provider change context for the given paths. The server runs an internal hidden ephemeral thread (dedicated identity, commit-suggest-only tool), so it does not contend with the source thread and clients receive no notifications for it.

**Params**: `threadId` (in the server's workspace), `paths` (workspace-relative, non-empty), `provider` (`git` default, or `perforce` using the server-side Perforce binding), optional `maxDiffChars` (cap on change context; the server may truncate further).

**Result**: `{ message }` — a commit message (subject line, optional body) for Git, or a changelist description for Perforce.

**Errors** include: thread not found, paths outside the workspace, unsupported provider, Git or Perforce context unavailable, empty change context, model did not emit the suggestion tool, timeout.

### 5.4 `welcome/suggestions`

Return advisory welcome-screen prompt suggestions for the workspace. Gated by `capabilities.extensions.welcomeSuggestions`; otherwise `-32601`.

**Params**: `identity` (with required `workspacePath`), `maxItems` (default 4; may be clamped).

**Result**: `items` (`title`, `prompt` to prefill, optional `reason`), `source` (`dynamic` for workspace-specific suggestions, `none` when the server intentionally returns none; `items` may then be empty and client defaults stay out of band), `generatedAt`, and `fingerprint` (stable for unchanged evidence, so clients can skip redundant refreshes).

Suggestions derive from the workspace memory store, never from conversation history, and none are produced when memory is disabled. The method serves a persisted workspace snapshot of the latest successful result without synchronous model generation; refresh runs asynchronously after successful turns and failed refreshes keep the previous snapshot.

**Errors** include missing `identity.workspacePath`, unsupported capability, invalid `maxItems`, and unavailable workspace.

## 6. Event Notifications

Event notifications are server-initiated JSON-RPC notifications that stream the turn lifecycle. They correspond to the `SessionEvent` types in [Session Core, Section 6](../architecture/session-core.md#6-event-model).

### 6.1 Thread Notifications

A thread is **hidden** when it is ephemeral or internal (DotCraft-owned helper threads marked with `dotcraft.internal` metadata or a known internal origin, and conversational Agent Builder threads). The workspace-level broadcasts below — `thread/started`, `thread/updated`, `thread/renamed`, `thread/deleted`, `thread/archived`, `thread/unarchived`, and `thread/status/changed` — reach a hidden thread's events only on connections subscribed to it; the creating connection already has the thread from its result. SubAgent child threads are not hidden. Turn and item notifications keep subscription rules.

| Notification | Params | Semantics |
|--------------|--------|-----------|
| `thread/started` | `{ thread }` | A thread was created. Sent to the initiating client after `thread/start` and broadcast to connections when any channel in the process creates one, including the connection whose other request (such as an extension request) created it as a side effect. SubAgent child creation is broadcast so lists can show the child while the parent turn runs. |
| `thread/worktree/handoffProgress` | `{ threadId, operationId?, step, status }` | A `thread/worktree/handoff` step changed state. `step` is `cleanupEnvironment`, `createWorktree`, `reuseWorktree`, `stashSourceChanges`, `stashTargetChanges`, `detachWorktreeBranch`, `checkoutLocalBranch`, `applyChanges`, `moveThread`, or `rollback`; `status` is `started`, `completed`, `skipped`, or `failed`. Steps that do not apply report `skipped`. |
| `worktree/createProgress` | `{ operationId, step, status }` | A `worktree/createAndStart` or `worktree/createAndFork` step carrying an `operationId` changed state. `step` is `createWorktree`, `copyFiles`, or `setupEnvironment`; `status` as above. |
| `worktree/setupOutput` | `{ operationId, delta }` | Output of a setup script run for `operationId` (creation or `worktree/runSetup`), stdout and stderr interleaved as produced. |
| `thread/updated` | `{ thread }` | Compact metadata changed without create, delete, rename, or status change (for example after `thread/worktree/handoff`, `thread/config/update`, or a Turn start that changed `gitInfo`). The payload may omit history; clients merge it rather than treating omitted history as deleted. |
| `thread/renamed` | `{ threadId, displayName }` | Display name changed by `thread/rename` or automatic naming. Broadcast to all connections; duplicate deliveries are allowed. |
| `thread/deleted` | `{ threadId }` | Thread permanently deleted. Broadcast to all connections, including the initiator, regardless of which entry point removed it; no subscription required; may precede or follow the RPC response. Duplicates are ignored. |
| `thread/resumed` | `{ thread, resumedBy }` | Thread resumed through `thread/resume`; `resumedBy` is the channel name. |
| `thread/archived` | `{ threadId }` | Archived directly or as part of its parent. |
| `thread/unarchived` | `{ threadId }` | Restored to Active. |

#### `thread/status/changed`

A workspace-level broadcast of a thread's aggregated **runtime snapshot**, delivered to every initialized connection that has not opted out, subscribed or not (hidden threads excepted). It is a summary channel for thread-list UIs and does not replace turn-scoped notifications.

**Params**: `{ threadId, runtime }`, where `runtime` contains:

- `running`: a turn is executing.
- `activeTurnId`, `activeTurnStartedAt`: one atomic snapshot of the current non-terminal Turn, set and cleared together, so summary UIs can show elapsed time. Omitted when no Turn is active.
- `waitingOnApproval`, `waitingOnInput`: unresolved approval or model-initiated user-input requests exist.
- `waitingOnPlanConfirmation`: the latest completed turn ran in plan mode with a successful `CreatePlan` call (a matching successful tool result in the same Turn) and the next `turn/start` has not yet cleared it. Later tool calls or output in that Turn, such as `TaskStop` cleanup, do not clear it.
- `busy`: the thread cannot start a new turn because a turn, approval, input request, or blocking maintenance is active.
- `maintenanceKind`: current blocking maintenance (`"compacting"`), omitted when none.
- `queuePaused`: the latest Turn was cancelled, so queued input waits for `turn/queue/start` ([5.2.6](#526-turnqueuestart)).

Emitted when the snapshot changes because a turn starts or ends, an approval or user-input request is created or resolved, plan confirmation is set or cleared, or maintenance starts or completes. The server should emit only on effective change; duplicates are allowed and the latest payload replaces the cached snapshot. Clients ignore unknown `runtime` fields.

### 6.2 Turn Notifications

| Notification | Params | Semantics |
|--------------|--------|-----------|
| `turn/started` | `{ turn }` | Turn began, after the `turn/start` response. Includes the `userMessage` item. |
| `turn/completed` | `{ turn }` | Turn finished successfully, with `completedAt` and `tokenUsage`. Responses truncated by the provider output-token limit are failures, not completions. |
| `turn/failed` | `{ turn, error }` | Unrecoverable error, including reaching the provider output-token limit before the answer completed. `turn.status = "failed"` and `turn.error` is set. |
| `turn/cancelled` | `{ turn, reason }` | Cancelled by `turn/interrupt`, client disconnect, or cold-load recovery (reason `"interrupted"`, published only after the terminal Turn is durable). |

Terminal Turn notifications project included items with the same connection-scoped filtering as `item/completed` and `thread/items/list`.

#### `turn/diff/updated`

Carries the **complete latest snapshot** of the Turn's aggregated diff of tool-mediated file edits: `{ threadId, turnId, diff }`, where `diff` is a Git-style unified diff from each touched file's content before its first edit in the Turn to its current content.

- Only `WriteFile` and `EditFile` edits are tracked, including those of in-process SubAgents during the Turn; shell-made changes are not.
- Emitted right after the `item/completed` of the `toolResult` that changed the aggregate, and once more before `turn/completed` or `turn/cancelled` if it changed since the last emission. Identical snapshots are not re-sent.
- An empty `diff` means the edits cancel out or the aggregate is unavailable (edit on a Remote Tool Host, size cap exceeded, tracking invalidated). Clients then fall back to per-call `fileChange` diffs in `toolResult.structuredContent` ([Session Core, ToolResult](../architecture/session-core.md#toolresult)).
- Not persisted; reloads rebuild changes from per-call diffs, composing each file's sequential diffs into one. Included in `thread/subscribe` replay.

#### `thread/queue/updated`

`{ threadId, queuedInputs }`: the complete queue after input is enqueued, removed, reordered, updated, dequeued, or restored after a failed dequeue start.

### 6.3 Item Notifications

Items follow `item/started` → zero or more deltas → `item/completed` ([Session Core, Section 5.3](../architecture/session-core.md#53-item-lifecycle)). `item/started` and `item/completed` carry `{ threadId, turnId, item }`; on `item/completed` the item is final.

Canonical payload schemas are in [Session Core, Section 4.2](../architecture/session-core.md#42-item-payload-schemas). `item.type` is the discriminator. Wire-specific notes:

| `item.type` | Wire-specific notes |
|-------------|---------------------|
| `userMessage` | `text` is a display field. `nativeInputParts` is authoritative for history rendering; `materializedInputParts` is the exact model snapshot. `deliveryMode` is `normal`, `queued`, `guidance`, or `subagentMailbox`. `sentAsGoal` marks goal submission. Trigger fields (`triggerKind` — `automation`, `goal`, `app`, `mcpApp`, `workflow`, `subagentFollowupTask`, `subagentMailbox`, `subagentInput`, `terminal`, `subagent` — plus `triggerLabel`, `triggerRefId`, `triggerStatus`) mark synthesized input. Task notifications and `subagentMailbox` items are model-visible and must not render as user bubbles. SubAgent `triggerRefId` values are agent paths, not thread ids. |
| `agentMessage` | Text streams through `item/agentMessage/delta`. `deliveryMode = "async"` marks a model-initiated message during a running Turn: delivered immediately, does not complete the Turn, excluded from final-text aggregation, not restored into model-visible history. |
| `reasoningContent` | Text streams through `item/reasoning/delta`. |
| `toolCall` | Optional canonical `namespace`, local `toolName`, required `providerFlatName`, `arguments`, `callId`, plus optional definition and source provenance. Argument construction may stream through `item/toolCall/argumentsDelta`. |
| `commandExecution` | Shell projection (`command`, `workingDirectory`, `source`, `status`, `aggregatedOutput`, `exitCode`, `durationMs`, `callId`, `sessionId`). Gated by `commandExecutionStreaming`. |
| `toolExecution` | Runtime lifecycle projection of a tool invocation. Gated by `toolExecutionLifecycle`. |
| `imageGeneration` | Self-contained lifecycle of the image generation tool (no `toolCall`/`toolResult`): `status` `inProgress`/`completed`/`failed`, optional revised prompt, base64 result, media type, saved path, error, and backend request/generation ids. |
| `webSearch` | Self-contained lifecycle of the web tool: `status`, display `query` summary, nullable `action` describing the first command (`search`, `openPage`, `findInPage`, or `other`), nullable `results` (opaque search-service objects; tolerate unknown fields), optional raw `arguments`, and optional `output` (the text returned to the model and replayed into history; may contain private-use citation markers and is not display text). The started item has an empty `query`. |
| `mcpToolCall` | Self-contained MCP lifecycle item (no `toolResult`): canonical identity, `providerFlatName`, runtime `server`, `origin`, source tool id, binding identities and revisions, `callId`, arguments, status, duration, normalized and raw content, `structuredContent`, sanitized `_meta`, error fields, and optional `mcpAppResourceUri`. View handles, HTML, CSP, and availability are never persisted. |
| `dynamicToolCall` | Self-contained Runtime Dynamic Tool lifecycle (no `toolCall`/`toolResult`) with `inProgress`/`completed`/`failed` status, content, `structuredContent`, nullable terminal success, and error fields. |
| `toolResult` | Paired with `toolCall` by `callId`: model-safe `result`/`contentItems`, client-only `structuredContent` (including the `fileChange` shape from [Session Core, ToolResult](../architecture/session-core.md#toolresult)), sanitized host-only `_meta`. Provider history never includes `structuredContent` or `_meta`. |
| `approvalRequest` / `approvalResponse` | Canonical fields; decisions serialize as wire strings. |
| `userInputRequest` / `userInputResponse` | Paired with `item/tool/requestUserInput`; the request puts the turn in `waitingInput`. |
| `sleep` | Runtime wait with requested `durationMs`, optional `actualDurationMs`, and `status` `inProgress`/`completed`/`interrupted`. Steer or mailbox input for the active Turn may interrupt it. |
| `error` | Item-level errors, separate from JSON-RPC errors. |

Clients must treat `imageGeneration`, `webSearch`, `mcpToolCall`, and `dynamicToolCall` as self-contained lifecycle items and render `inProgress` as non-terminal. Ordinary `toolCall`/`toolResult` pairs may be merged into one row. `structuredContent` and `_meta` are never shown as model-history text by default.

**MCP App candidates**: a terminal `mcpToolCall` whose `mcpAppResourceUri` is an absolute `ui://` URI is an MCP App candidate. Projections never inspect the live runtime to decide availability; clients that advertised `capabilities.mcpApps` open candidates through `mcpApp/view/open`, which is authoritative, and show the tool result when it rejects the item.

#### Text deltas

`item/agentMessage/delta` (`deltaKind = "agentMessage"`) and `item/reasoning/delta` (`deltaKind = "reasoningContent"`) carry `{ threadId, turnId, itemId, deltaKind, delta }`. Clients concatenate `delta` values in order.

#### `item/toolCall/argumentsDelta`

Streams tool-argument construction for a `toolCall` item: `{ threadId, turnId, itemId, deltaKind: "toolCallArguments", toolName?, callId?, delta }`.

- Sequence: `item/started` (with `arguments` omitted/null while construction is in progress, never an empty object), zero or more deltas, then `item/completed` with the complete arguments.
- `delta` is a raw JSON text fragment, not parseable mid-stream. `toolName` and `callId` usually appear only on the first chunk. Clients merge by `itemId` in arrival order for preview only. Empty deltas are suppressed.
- The final `item/completed` arguments are authoritative. An empty `arguments` object there means a known-empty argument set and must not replace an active preview before completion.
- Deltas are presentation-only and may omit the namespace; the server never dispatches from a delta.
- Deltas are emitted for non-external tools by default (built-in, module, plugin, MCP); a tool may opt out server-side. Clients must not assume which tools stream and should render a generic placeholder, not raw JSON, for tools they do not recognize.

#### `item/commandExecution/outputDelta`

`{ threadId, turnId, itemId, delta }` for a `commandExecution` item: `item/started` (`status = "inProgress"`), zero or more output deltas, then `item/completed` with final status and `aggregatedOutput`.

- Clients with `backgroundTerminals` use `terminal/started`, `terminal/outputDelta`, and `terminal/completed` as the primary live shell output source; `commandExecution` (with `commandExecutionStreaming`) is the projection for persistence, history, and fallback. The underlying `toolCall`/`toolResult` still exist. Clients consuming both merge by `callId` and avoid double rendering. Clients without either capability rely on `toolCall`/`toolResult`.
- An `Exec` that outlives its yield time returns its tool result while the process keeps running. Its `commandExecution` item completes when the process exits, possibly after `turn/completed`; clients apply that late `item/completed` to the completed turn. Later output arrives only through `terminal/*`.

#### `item/commandExecution/terminalInteraction`

`{ threadId, turnId, itemId, sessionId, stdin }`: `WriteStdin` wrote to (or, with empty `stdin`, polled) the process of a `commandExecution` item, which may belong to an earlier turn and may be completed. Sent only with `commandExecutionStreaming`; not persisted — the `WriteStdin` `toolCall`/`toolResult` remain the durable record, with a `sessionId` argument matching the command item. Clients show non-empty input on that command and do not render polls or successful `WriteStdin` calls as separate cards (failed calls keep theirs).

#### `toolExecution` lifecycle

With `capabilities.toolExecutionLifecycle`, the server may emit a `toolExecution` item for each non-plugin tool invocation so a card can update when that invocation finishes while parallel calls continue: `item/started` (`inProgress`), zero or more `item/toolExecution/progress`, then `item/completed` with `status` (`completed`, `failed`, `cancelled`), `success`, `durationMs`, and optional `resultPreview`/`errorMessage`.

- It does not replace `toolCall` (final arguments) or `toolResult` (authoritative result). `resultPreview` is sanitized, may be truncated to 4096 characters, and is replaced by `toolResult.result` when that arrives.
- Runtime Dynamic Tools emit no `toolExecution`; `dynamicToolCall` represents them.

`item/toolExecution/progress` is a live, non-persistent snapshot correlated by `itemId` and `callId`, with a `progress` object discriminated by `kind`. The `remoteFileTransfer` kind reports `stage` (`preparing`, `transferring`), `direction`, local and remote paths, host, optional totals, transferred bytes for the current file, and files/bytes confirmed by atomic commit. It never conveys terminal success. Gated by `toolExecutionLifecycle`, not by `streamingSupport`.

### 6.4 Approval Notifications

#### `item/approval/resolved`

`{ threadId, turnId, item }` with the `approvalResponse` item, emitted when an approval resolves — by client response, non-interactive fallback ([Section 7.4](#74-clients-without-approval-support)), or end of turn ([Section 7.6](#76-pending-requests-and-multiple-connections)). It precedes the `item/completed` for the same item.

### 6.5 SubAgent Notifications

#### `subagent/progress`

Sideband snapshot of all SubAgents tracked during a Turn while `SpawnAgent` calls are active: `{ threadId, turnId, entries }`. Each entry has `label` (the `agentNickname` passed to `SpawnAgent`), `currentTool` (`null` while waiting for the model), cumulative `inputTokens`/`outputTokens`, and `isCompleted`.

- Emitted periodically; clients must not depend on the interval. Each notification is the complete set, replacing local state.
- Emission stops after a final snapshot in which all entries are completed.

#### `subagent/graph/changed`

`{ parentThreadId, childThreadId }`: a session-backed parent/child edge was created or changed status. Clients refresh `subagent/children/list` for the parent.

### 6.6 Usage Notifications

#### `item/usage/delta`

Emitted once per LLM iteration with non-zero usage, carrying that iteration's **incremental** usage for the main agent: `inputTokens`, `outputTokens`, `cachedInputTokens` (cache read), `cacheWriteInputTokens`, `freshInputTokens` (`max(0, input − cached − cacheWrite)`), and `reasoningOutputTokens`. Optional fields:

- `llmCallDelta`: `1` when the delta starts a new LLM request.
- `turnInputTokens`, `turnOutputTokens` (alias `totalOutputTokens`), `turnLlmCalls`: cumulative billing totals so far in the Turn.
- `contextInputTokens` (alias `totalInputTokens`): the latest request's input snapshot — context occupancy, not billing.
- `contextUsage`: the full context snapshot; its `tokens` may include output/cache pressure and exceed `contextInputTokens`.
- `requestTiming`: `{ firstOutputLatencyMs, generationMs }` for the LLM request this delta belongs to, measured by the host on the provider stream. `firstOutputLatencyMs` runs from sending the successful attempt to the provider's first output event; `generationMs` runs from that event to this usage report. Provider events that only acknowledge the request (Responses `response.created`, `response.queued`, `response.in_progress`) are not output events. A retried attempt restarts both. When a request reports usage more than once, each delta carries the timing so far and the last one is final. Output token throughput is `Σ outputTokens / Σ generationMs`.

Rules:

- Providers that report cumulative snapshots within a request are normalized to deltas by Session Core.
- The sum of a Turn's deltas equals the main-agent billing portion of `turn/completed.tokenUsage`, which is a final snapshot, not another delta. Cache-hit deltas sum the same way.
- Billing totals and context occupancy are separate: with request input snapshots `12000 | 20000 | 41000`, `turnInputTokens` reaches `73000` while `contextInputTokens` stays `41000`.
- SubAgent usage is reported through `subagent/progress` and `thread/tokenUsage/updated`, not here.

#### `thread/tokenUsage/updated`

`{ threadId, turnId, tokenUsage: { total, last, modelContextWindow? } }` after each LLM iteration of any thread, SubAgent children included. `total` covers every Turn so far including the running one; `last` is the request that produced the notification. Breakdowns contain `totalTokens` (input + output), `inputTokens`, `cachedInputTokens`, `cacheWriteInputTokens`, `outputTokens`, and `reasoningOutputTokens`.

- Derived from the same usage as `item/usage/delta` but delivered independently; clients do not rely on their relative order. An external SubAgent runtime reports once, when its Turn completes.
- After a Turn ends, `total` equals the sum of `tokenUsage` over persisted Turns (a running Turn's `tokenUsage` holds usage so far). Clients start from that sum and replace it with each `total`.
- Delivered to subscribers of the thread and, for a SubAgent child, to subscribers of its parent.

### 6.7 System Notifications

#### `system/event`

Maintenance and stream events that affect session state but are not conversational output.

**Params**: `threadId`; `turnId` (null for thread-scoped maintenance such as manual compaction); `kind`; localization via `messageKey`, `params`, and English `fallbackText` (`message` is an alias; clients prefer the key). The server never translates user text, model output, or tool output. Compaction events may carry `percentLeft`, `tokenCount`, and, on terminal events, a full `contextUsage` that clients prefer over the scalar fields; `compacting` start events must not update context UI from projected estimates.

| Kind | Meaning |
|------|---------|
| `compactWarning` | Usage crossed the warning threshold. Advisory. |
| `compactError` | Usage crossed the error threshold; auto-compaction is imminent. Advisory. |
| `compacting` | A compaction attempt (auto, reactive, or manual) is starting. |
| `compacted` | Compaction succeeded. |
| `compactSkipped` | Evaluated but not executed (below threshold, nothing new, or circuit breaker open). |
| `compactFailed` | Attempted and failed; repeated failures trip the backend's circuit breaker. |
| `compactCancelled` | Manual compaction interrupted by the user. |
| `streamError` | A provider stream disconnected or idled out before the request completed and the server is retrying. `params` carry one-based `attempt`, `max`, classified `providerError`, and `httpStatus` when known. |
| `guidanceBlocked` | A `UserPromptSubmit` hook blocked steering input before admission; the input was removed without a `userMessage`. `messageKey = system.guidanceBlocked`, `params.reason` carries the hook reason. |

Rules:

- Turn-scoped compaction events are emitted during post-processing, before `turn/completed`. Auto-compaction is a synchronous pair: `compacting` then one of `compacted`, `compactSkipped`, `compactFailed`.
- Reactive compaction runs on the error path when the provider rejects a request as context overflow. The Turn still fails, but `compacting` and its terminal event come first so clients know history was repaired before retry.
- `streamError` is transient and not persisted. Only attempts that have produced no visible output (assistant text, reasoning, tool call, or argument delta) are retried; after visible output a stream failure is a normal failed Turn with partial state preserved. Retry or failure must surface promptly.
- A successful `compacted` also persists a `SystemNotice` item (`kind = "compacted"`) in the current or latest completed turn with the normal `item/started`/`item/completed` pair, giving a timeline marker that survives reload. Cold-cache tool-result clearing (`micro`) is transient and adds no notice. Fork creation places a `SystemNotice` with `kind = "forked"`. See [Session Core](../architecture/session-core.md#systemnotice).

### 6.8 Plan Notifications

#### `plan/updated`

Complete plan snapshot `{ threadId, title, overview, content, todos }` emitted each time `CreatePlan` or `TodoWrite` succeeds; clients replace local plan state. `title` is empty when the thread has only a to-do list. `todos` entries have `id`, `content`, `priority` (`high`, `medium`, `low`), and `status` (`pending`, `in_progress`, `completed`, `cancelled`). Missing `content` means empty.

The notification is sent outside the `SessionEvent` stream to all connections. Clients ignore snapshots for a thread other than the selected one and restore plans from `thread/read.thread.plan` when switching.

### 6.9 Job Result Notifications

#### `system/jobResult`

Delivers a server-managed automation run's result out of band: `source` (`"automation"`), `jobId`, `jobName`, `threadId` (readable with `thread/read` and the history methods), `result` (the agent's full text, null on failure or no text), `error`, and `tokenUsage`. Sent to eligible initialized connections; hosts that route job results elsewhere may omit it. `automation/run/updated` records execution and delivery status independently.

### 6.10 Notification Delivery Guarantees

The server delivers each event notification **at most once per connection**, however many delivery paths exist. When a connection subscribed to a thread calls `turn/start` on it, the subscription dispatcher is the sole delivery path for all turn-scoped notifications (`turn/*`, `turn/diff/updated`, `item/*` including deltas and usage, `subagent/progress`, `system/event`). The `turn/start` response still arrives before the first `turn/started`.

Workspace broadcasts (`thread/started`, `thread/renamed`, `thread/deleted`, `thread/archived`, `thread/unarchived`, `thread/status/changed`) are outside this rule.

Delivery is best-effort per connection. A transport write failure stops writes to that client but never stops the server from draining an already-started persisted turn. Passive subscriptions are cancelled with their connection; the internal active-turn drain continues. Outstanding interactive requests belong to their thread and resolve only through a client response, the non-interactive fallback for a turn started by a client without the capability ([Section 7.4](#74-clients-without-approval-support), [Section 7.5](#75-model-initiated-user-input-requests)), or the end of their turn ([Section 7.6](#76-pending-requests-and-multiple-connections)); a write failure, disconnect, or `thread/unsubscribe` never answers them. Returning clients recover through `thread/read`, fresh history pages, `thread/list`, fresh subscriptions, and server replay of unresolved requests on `thread/subscribe` or `thread/resume`.

## 7. Approval Flow

When the agent hits an operation that requires consent (file write, shell command, and other approval kinds), the server sends a **server-to-client request** and the client must respond.

### 7.1 Sequence

`item/started` (`approvalRequest` item) → `item/approval/request` (server request) → client response → `item/approval/resolved` → `item/completed` for the approval response, after which the gated tool call proceeds or is rejected. The turn is `waitingApproval` while the request is pending. The request has no timeout; [Section 7.6](#76-pending-requests-and-multiple-connections) defines how it resolves.

### 7.2 `item/approval/request`

**Direction**: server → client (request)

**Params**: `threadId`, `turnId`, `itemId` (the `approvalRequest` item), `requestId` (logical correlation id, stable across replay), and:

| Field | Semantics |
|-------|-------------|
| `approvalType` | `shell`, `file`, `computerUse`, `channelMessage`, or another resource kind contributed by a tool. |
| `operation` | Shell: the command. File: `read`, `write`, `edit`, `list`. Channel message: `send`. |
| `target` | Shell: working directory. File: path. Computer use: target application identity reported by the client backend. Channel message: `<channelName>:<target>`. |
| `targetLabel` | Optional display label for `target`. |
| `scopeKey` | Session cache key applied when the client returns `acceptForSession`. Shell uses `"shell:" + approvalKey` (this command, shell, and directory only); channel messages use `"channelmessage:send:" + target` (later sends to the same address). |
| `reason` | Human-readable explanation. |
| `shell` | For shell approvals: `reasons`, `rememberedPrefixes`, `remembersExactCommand` as defined by [Shell Command Safety](../architecture/shell-command-safety.md) Section 9. |

### 7.3 Client Response

The result is `{ decision }`:

| Value | Meaning |
|-------|---------|
| `accept` | Approve this operation. |
| `acceptForSession` | Approve this and matching operations (by `scopeKey`) for the rest of the thread's lifetime. |
| `acceptAlways` | Approve permanently; the server persists the approval and suppresses prompts for the current session. For `computerUse` the server records only the session scope; the client owning the computer-use runtime persists the permanent grant. |
| `decline` | Reject; the tool receives a rejection and the agent may try another approach. |
| `cancel` | Reject and cancel the turn, like `turn/interrupt`. |

The persisted `approvalResponse` item carries both `approved` (boolean convenience) and the exact `decision`.

### 7.4 Clients Without Approval Support

The server never sends `item/approval/request` to a connection that declared `approvalSupport = false`. When such a client started the turn, nothing can prompt for it, so each approval resolves immediately from server-owned policy:

- `autoApprove` → `accept`.
- `deny` → `decline`; the tool receives the rejection and the turn continues.
- `default` → the workspace default policy; if that is also `default` or unset, the non-interactive default decision, `decline`.
- `prompt` → the same non-interactive default as `default`.

This is the only non-interactive fallback. Approvals in any other turn, including server-started turns, stay pending under Section 7.6 even while no connection holds them.

### 7.5 Model-Initiated User Input Requests

The root-thread `RequestUserInput` tool asks the client for structured input; a SubAgent call is rejected before any request reaches the client. The sequence mirrors approvals: `item/started` (`userInputRequest`) → `item/tool/requestUserInput` (server request) → client response → `item/tool/requestUserInput/resolved`. The turn is `waitingInput` meanwhile.

**Params**: `threadId`, `turnId`, `itemId`, `requestId`, `questions` (one to three), and required `isBlocking`.

- Each question has a stable snake_case `id` (the answer key), `header`, `question`, two or three mutually exclusive `options` (`label`, `description`; a recommended option comes first, labelled `(Recommended)`), `isOther` (offer free-form "Other", default `true`), and optional `isSecret` (mask free-form text).
- `isBlocking` is `true` for Plan-mode requests and `false` for Agent-mode requests. It is persisted with the item and replayed unchanged, is not a model parameter, and does not change the server-side wait. A client may auto-answer `{ answers: {} }` when `isBlocking = false`; when `true`, dismissing the UI interrupts the turn rather than fabricating an empty response.

**Result**: `{ answers: { <questionId>: { answers: string[] } } }`.

The server never sends the request to a connection without `requestUserInputSupport`. When such a client started the turn, the request resolves immediately with empty answers. Otherwise it stays pending under Section 7.6.

### 7.6 Pending Requests and Multiple Connections

An interactive `item/approval/request` or `item/tool/requestUserInput` belongs to its thread, not to the connection that started the turn; several connections (for example a desktop window and a phone) may watch one thread.

- Every subscribed connection with the matching capability receives the pending request, live or through replay on `thread/subscribe` or `thread/resume`. Replay keeps the original `requestId`. A connection receives each request at most once, unless it answered with an error or an invalid result (which answers nothing), in which case its next subscription receives it again. Replayed approvals of one thread are sent one at a time.
- The request stays pending until the first valid response from any connection, until the thread's effective approval policy changes to `autoApprove` or `deny` ([Section 4.13](#413-threadconfigupdate)), or until its turn completes, fails, or is cancelled. It is never resolved because no connection holds it — not when none is subscribed, not when the last holder disconnects, and not on a write failure. A later subscriber can still answer it.
- When a turn ends with requests pending, approvals resolve as `cancel` (the operation does not run) and user-input requests resolve with empty answers.
- On every resolution the server stops waiting on all connections, and each subscriber receives `item/approval/resolved` or `item/tool/requestUserInput/resolved` and must dismiss its prompt. A late response is ignored without error.
- The only exception is the non-interactive fallback of Sections 7.4 and 7.5.

## 8. Error Handling

### 8.1 JSON-RPC Error Response

Errors use the JSON-RPC 2.0 error object. `error.message` is always an English diagnostic fallback. `error.data` carries a stable string `code`, localization fields `messageKey`, `params`, and `fallbackText`, and an optional `detail`. UI clients localize from `messageKey`/`params`/`fallbackText` and fall back to `error.message` only when structured data is absent.

### 8.2 Standard Error Codes

| Code | Name | When |
|------|------|------|
| `-32700` | Parse error | Malformed JSON. |
| `-32600` | Invalid request | Missing required fields or constraint violation. |
| `-32601` | Method not found | Unknown method, or a module method whose module is unavailable. |
| `-32602` | Invalid params | Params do not match the expected schema. |
| `-32603` | Internal error | Unexpected server failure. |

### 8.3 DotCraft-Specific Error Codes

| Code | Name | When |
|------|------|------|
| `-32001` | Server overloaded | Backpressure; retryable. |
| `-32002` | Not initialized | Method before `initialize`. |
| `-32003` | Already initialized | Repeated `initialize`. |
| `-32010` | Thread not found | Unknown `threadId`. Other missing ids inside an existing thread use their own code or `-32600`. |
| `-32011` | Thread not active | Operation requires an active thread but it is archived. |
| `-32012` | Turn in progress | A turn is running or waiting on the thread. |
| `-32013` | Turn not found | Unknown `turnId`. |
| `-32014` | Turn not running | `turn/interrupt` on a turn not in progress. |
| `-32030` | Channel rejected | Channel adapter name not registered in configuration. |
| `-32031` | Automation not found | Unknown automation id. |
| `-32040` | Skill not found | Skill name not found in any source. |
| `-32051` | Task not found | `automation/*`: unknown task. |
| `-32052` | Task invalid status | `automation/*`: operation invalid for the task's status. |
| `-32054` | Task already exists | `automation/create`: duplicate id. |
| `-32055` | Thread binding invalid | `automation/create` / `automation/update`: target thread missing or archived. |
| `-32060` | Command not found | `command/*`: command not registered. |
| `-32061` | Command permission denied | `command/execute`: admin-only command. |
| `-32062` | Command service unavailable | `command/execute`: backing service unavailable. |
| `-32070` | MCP server not found | Unknown MCP server name. |
| `-32072` | MCP server validation failed | Config invalid for the transport. |
| `-32073` | MCP server test failed | `mcp/test` probe failed. |
| `-32074` | MCP server name conflict | Case-insensitive logical key conflict. |
| `-32075` | MCP server read-only | Write targeted a plugin-origin server. |
| `-32080` | External channel not found | Unknown external channel. |
| `-32081` | External channel validation failed | Payload invalid for the transport. |
| `-32082` | External channel name conflict | Conflicts with an existing key or native channel. |
| `-32083` | SubAgent profile not found | Profile or workspace override missing. |
| `-32084` | SubAgent profile validation failed | Invalid or incompatible profile. |
| `-32085` | SubAgent profile protected | Targets a protected profile such as `native`. |
| `-32086` | Agent Profile not found | Unknown id/source. |
| `-32087` | Agent Profile validation failed | Markdown/frontmatter invalid, or an unsupported overlay with `agentProfileId` on `thread/start`/`worktree/createAndStart`. |
| `-32088` | Agent Profile protected | Write or remove of a built-in profile. |
| `-32089` | Agent Profile source unavailable | Source unsupported or not readable/writable here. |
| `-32091` | Agent Profile conflict | Id conflicts with the frontmatter name or a protected source. |
| `-32093` | Marketplace source invalid | Source, reference, sparse paths, or resolved document not acceptable. |
| `-32094` | Marketplace fetch failed | Could not fetch or materialize. |
| `-32097` | Thread recovery failed | See `error.data.code` ([Section 4.18](#418-thread-recovery-methods)). |
| `-32099` | Plugin configuration | `plugin/config/*`; see `error.data.code`. |
| `-32100` | Remote Tool Host unavailable | `remoteToolHost/connect`: not paired, offline, authentication failed, or incompatible; `error.data.code` carries the host error code. |
| `-32101` | Remote workspace busy | `remoteToolHost/connect`: another Agent Host holds the folder lease; `error.data.params.owner` is `self` or `other`. |
| `-32103` | File system | `fs/*`: blocked, missing, wrong kind, or too large; `error.data.code` names the case, `error.data.params.path` the path. |
| `-32104` | Command execution | `command/exec`: program or directory missing, cannot start, or directory blocked. |
| `-32105` | Dreams store changed | `dreams/apply`: memory store changed after the run started (`error.data.code = storeChanged`). |
| `-32106` | Worktree environment | Worktree methods ([Section 4.17.7](#4177-environments)): `error.data.code` is `environmentInvalid`, `creationCancelled`, or `cleanupFailed`; `cleanupFailed` carries `error.data.params.path`, `exitCode`, and `outputTail`. |
| `-32107` | Worktree in use | `thread/worktree/handoff` to local: another program holds the worktree folder; `error.data.params.path` is the folder. |

Automation task methods (`automation/*`) share this error space; their params, results, and persisted shapes are owned by the automations feature.

### 8.4 Turn-Level Errors

Agent execution errors arrive as `turn/failed`, not as an error response to `turn/start` (the request succeeded; the asynchronous run failed). `turn.error` holds the description. An `error` item created during the turn appears in `items` and is emitted through `item/started`/`item/completed` before `turn/failed`.

Provider failures also set `turn.providerError` — `contextWindowExceeded`, `usageLimitExceeded`, `rateLimitExceeded`, `serverOverloaded`, `internalServerError`, `unauthorized`, `badRequest`, `httpConnectionFailed`, `responseStreamConnectionFailed`, `responseStreamDisconnected`, `responseTooManyFailedAttempts`, or `other` (error frames inside a successful stream are classified like HTTP failures) — and `turn.httpStatus` when known. Clients may retry transient classifications with a `turn/start` whose `input` is empty.

## 9. Backpressure

### 9.1 Server-Side Queuing

The server uses bounded queues between transport ingress, request processing, and outbound writes. When inbound capacity is saturated, new requests fail with `-32001` (`Server overloaded; retry later.`); clients retry with exponential backoff and jitter.

### 9.2 Client-Side Considerations

Clients must consume notifications promptly. A client that falls behind reading stdout or WebSocket frames may be buffered up to a limit and then disconnected.

## 10. Notification Opt-Out

Clients suppress server-to-client notifications per connection by listing exact method names in `initialize.params.capabilities.optOutNotificationMethods`.

- Matching is exact; no wildcards or prefixes. Unknown names are ignored.
- Applies only to notifications, not requests or responses.
- Fixed for the connection's lifetime.

Typical targets are streaming deltas (`item/agentMessage/delta`, `item/reasoning/delta`, `item/toolCall/argumentsDelta`), broadcasts a client does not display (`thread/started`, `thread/renamed`, `thread/deleted`, `thread/archived`, `thread/unarchived`, `thread/status/changed`), and auxiliary streams (`turn/diff/updated`, `subagent/progress`, `item/usage/delta`, `thread/tokenUsage/updated`, `system/event`, `plan/updated`, `system/jobResult`, `plugin/snapshot/updated`). Clients that opt out recover the same state from final items, `turn/completed.tokenUsage`, per-call `fileChange` data, or the corresponding list methods.

## 11. Extension Methods

The core protocol (Sections 3-10) covers the `ISessionService` surface. Modules may expose **extension methods** for capabilities that are not intrinsic to Session Core.

### 11.1 Design Rules

- Core methods are owned by the AppServer runtime; module methods are contributed by loaded modules and routed by method name. A module method whose module is not loaded or cannot operate in the workspace returns `-32601`.
- Module methods must not reuse a core method name. Method ownership is validated from one authoritative namespace view before dispatch; conflicts and duplicate extension ownership fail before invocation.
- Server-to-client extension families use the `ext/<namespace>/...` prefix (for example `ext/acp/...`). Client-to-server module methods may use stable product namespaces and are standard protocol extensions.
- Built-in and extension capabilities are aggregated and reported consistently during `initialize`; capability aggregation is part of protocol readiness.
- This spec is the source of truth for documented extension method names and semantics; implementation location is not wire-visible.

### 11.2 Unified Channel Runtime (Remote Projection)

External channel adapters are served through **server → client** requests under `ext/channel/*`, following the same request/response pattern as approvals. Capabilities are negotiated through `capabilities.channelAdapter` ([Section 3.2](#32-initialize)):

- Every adapter implements `ext/channel/send`; `deliveryCapabilities` describes what it accepts, including which media kinds and source forms.
- `channelTools` are validated and registered once per connection and may be injected into threads whose origin channel matches, for the lifetime of the connection. The declared set is immutable for the connection; registration comes from the handshake, not from static configuration. Thread tool construction only filters visibility by origin channel and reserved names.
- Each injected binding retains the exact connection that declared it. Disconnect or replacement revokes dispatch immediately, and an invocation from an older Turn snapshot is never routed through a newer connection. While no connection is attached, the last connection's declarations stay visible and fail dispatch as unavailable.

#### 11.2.1 `ext/channel/send`

Structured delivery of text and media. **Direction**: server → client (request).

**Params**: `target` (adapter target syntax), `deliveryId`, `message`, optional `metadata`.

- `deliveryId` (required) identifies the delivery. The adapter treats a repeated id as already delivered and returns the first delivery's result without posting again; remembering recent ids in memory suffices. The id of a `SendChannelMessage` part is derived from the Turn, tool call, and part index, so a retried call does not post twice; other deliveries carry a fresh id.
- `message.kind` is `text`, `file`, `audio`, `image`, or `video`, with optional `text`, `caption`, `fileName`, `mediaType`, and `source`.
- `source.kind` (`hostPath`, `url`, `dataBase64`, `artifactId`) is authoritative; adapters ignore unrelated source fields.
- If a media kind advertises `maxBytes`, the server rejects sources it cannot validate against the limit, including remote URLs it does not fetch.

**Result**: `delivered`, optional `remoteMessageId`, `remoteMediaId`, and on failure `errorCode`/`errorMessage`. Standard error codes: `UnsupportedDeliveryKind`, `UnsupportedTarget` (unreachable or not in the adapter's syntax), `UnsupportedMediaSource`, `MediaTooLarge`, `MediaTypeNotAllowed`, `MediaArtifactNotFound`, `MediaResolutionFailed`, `AdapterDeliveryFailed`, `AdapterProtocolViolation`.

#### 11.2.2 `ext/channel/toolCall`

Invocation of an adapter-declared channel tool. **Direction**: server → client (request).

**Params**: `threadId`, `turnId`, `callId`, `tool`, `arguments`, and `context` (`channelName`, `channelContext`, `senderId`, `groupId`). **Result**: the same envelope as `item/tool/call` ([Section 11.3](#113-itemtoolcall)).

- The server calls only tools the connection declared in `initialize`; an adapter that declares tools must handle their calls.
- When a descriptor declares `approval`, the server may gate execution before sending the request, resolved from the same server-owned thread and workspace policy as built-in tools. The metadata never defines adapter-local policy.
- Channel tools project as standard `toolCall` + `toolResult` items with channel provenance.

### 11.3 `item/tool/call`

Invocation of a Runtime Dynamic Tool declared through `dynamicTools`. **Direction**: server → client (request).

**Params**: `threadId`, `turnId`, `callId` (the original provider call id, preserved on the callback and lifecycle item; the Session item id is separate), optional `namespace`, `tool`, and `arguments` validated against `inputSchema`.

**Result**: only `success`, `contentItems`, `structuredContent`, and on failure `errorCode`/`errorMessage`.

- `contentItems` accepts validated text and image items; an image has `mediaType` and exactly one of a non-data `url` or `dataBase64`. A successful model-visible call needs at least one useful text item.
- `_meta`, `_meta.ui`, `structuredResult`, and UI resource fields are invalid.
- The call has no timeout: the server waits for the client's response until the client answers, its connection closes, or the Turn is cancelled. A client may keep a call open while it asks the user.
- Standard error codes: `DynamicToolUnavailable`, `DynamicToolDisconnected`, `DynamicToolCancelled`, `DynamicToolUnauthorized`, `DynamicToolProtocolError`, `DynamicToolResultInvalid`. Disconnect, cancellation, or an invalid result complete the `dynamicToolCall` item as failed with the corresponding category.

#### 11.3.1 Interactive Tool UI boundary

Runtime Dynamic declarations cannot carry `_meta.ui` and results cannot carry `_meta`. MCP Apps resource rendering, AppBridge messages, and view-initiated tool calls use the MCP source and session authority model, not this callback protocol.

### 11.4 ACP Tool Proxy

The ACP (Agent Client Protocol) integration lets agent tools use the IDE client's filesystem, terminals, and custom extension methods through **server → client** `ext/acp/*` requests.

- **Capability gating**: the server sends only what `capabilities.acpExtensions` advertised: `fsReadTextFile` and `fsWriteTextFile` gate the file methods, `terminalCreate` gates the terminal methods, and each custom family `<family>` gates `ext/acp/<family>/<method>` (for example `ext/acp/_unity/scene_query`).

  | ACP method (IDE) | Wire extension method |
  |------------------|----------------------|
  | `fs/readTextFile` | `ext/acp/fs/readTextFile` |
  | `fs/writeTextFile` | `ext/acp/fs/writeTextFile` |
  | `terminal/create` | `ext/acp/terminal/create` |
  | `terminal/getOutput` | `ext/acp/terminal/getOutput` |
  | `terminal/waitForExit` | `ext/acp/terminal/waitForExit` |
  | `terminal/kill` | `ext/acp/terminal/kill` |
  | `terminal/release` | `ext/acp/terminal/release` |

- **Per-thread binding**: a thread created by a connection that declared `acpExtensions` is bound to it; tool calls during its turns route to that connection. If the connection closes before a request completes, the request fails.
- **`threadId`**: every `ext/acp/*` request carries `threadId` in `params` alongside method fields, so a single-connection bridge can route concurrent requests to the right IDE session. Bridges strip it when the IDE protocol does not define it.

**ACP bridge runtime tools**: an ACP client may declare DotCraft runtime tool descriptors through the private ACP extension `clientCapabilities._meta.dotcraft.runtimeTools` (`version` must be `1`, plus `tools`). A missing or unsupported version, or an invalid descriptor, fails ACP `initialize` with `-32602`. Custom descriptor methods start with `_`; `fs/*` and `terminal/*` descriptors stay gated by standard ACP capabilities. The bridge derives custom families from validated methods, must not add custom fields to the root ACP capability object, and translates descriptors into `dynamicTools` on `thread/start` and `thread/resume`. Callbacks return the `item/tool/call` result envelope inside the ACP JSON-RPC `result`, validated with the Runtime Dynamic rules; a callback's non-empty `errorCode`/`errorMessage` are preserved, and a JSON-RPC `error` maps to the corresponding Runtime Dynamic failure category.

### 11.5 Node REPL Runtime

Browser and computer-use tools run through a **server → client** Node REPL backend. Requests go only to the thread-bound client that declared `capabilities.nodeRepl` with `browserUse` or `computerUse`; the client selects a declared backend. A native SubAgent full-history fork snapshots the parent's live Node REPL binding onto the child before its first sampling (fresh and bounded forks do not); evaluations use the child's identity and later parent rebinding does not propagate. The binding is ephemeral and is re-established through thread resume after process recovery.

#### `ext/nodeRepl/evaluate`

**Direction**: server → client (request). **Params**: `threadId`, optional `turnId`, `evaluationId` (for cancellation and late-result suppression), `code` (JavaScript evaluated in the thread's persistent REPL), optional `timeoutMs` (client may clamp), and optional `browserSession` (`protocolVersion` 1, `sessionId` — the browser isolation key, normally the thread id — plus `threadId`, `turnId`, and `evaluationId` for backends that receive only the session object).

**Result**: optional `text`, `resultText` (serialized final expression), `images` (`mediaType`, `dataBase64`), `logs`, `error` (user-readable), and `surface` (`computer` when the evaluation called `dotcraft.computer`).

The client should return before `timeoutMs`, using shorter internal timeouts for browser sub-operations and returning a readable `error` rather than leaving the request pending. The deadline does not advance while an `ext/nodeRepl/requestApproval` for the evaluation is pending.

#### `ext/nodeRepl/cancel`

**Direction**: server → client (request). **Params**: `threadId`, `evaluationId`. **Result**: `{ ok }` — `false` when no matching evaluation is in flight. Best-effort: the client aborts pending browser operations, rebuilds the REPL context if needed, and ignores late results.

#### `ext/nodeRepl/requestApproval`

**Direction**: client → server (request). Asks the user, through the turn that issued an in-flight evaluation, to approve a client-runtime operation (computer use uses it for application authorization).

**Params**: `threadId`, `evaluationId`, `approvalType` (currently `computerUse`), `operation` (computer use sends `use`), `target` (application identity), optional `targetLabel`. **Result**: `{ approved }`.

The server resolves it with that turn's approval service, so policy, hooks, session scopes, approval items, and replay behave as for any tool approval. The evaluation deadline pauses until resolution. The request fails with a JSON-RPC error when the evaluation is not in flight on this connection or the thread does not match.

### 11.6 Dynamic Workflow Control

The Dynamic Workflows module exposes a trusted control and projection surface under `workflow/run/*`, gated by `capabilities.extensions.dynamicWorkflows`. It does not expose workflow definition CRUD or arbitrary script execution; the workflow feature owns lifecycle, replay, and progress semantics.

Requests addressing one run require both `threadId` and `runId`. A missing run and a run belonging to another thread return the same `workflow_run_not_found`, preventing cross-thread enumeration.

**Projections**:

- `WorkflowRunSummary`: identity, owning thread, workflow name and description, `status` (`running`, `paused`, `stopped`, `succeeded`, `failed`, `interrupted`), timestamps, optional `resumedFromRunId`, aggregate `totals` (Agent operation counts by state, including `replayedCount` for operations satisfied from a replay source, plus token and tool-call totals), and `controls` (`canPause`, `canStop`, `canResume`). Clients use `controls` rather than recreating the state machine.
- `WorkflowRunView`: the summary plus `phases` (each with `name`, bounded `detail`, `status`, and its agents in declaration order), `unphasedAgents`, and, once available, opaque `result` and `error`. It never contains model, effort, script path or contents, or invocation arguments.
- Agent views carry a run-local deterministic `operationId`, label, `status` (`queued`, `running`, `completed`, `failed`, `stopped`, `replayed`), optional phase and child thread (live or retained replay source), usage, tool-call count, timestamps, and `replayed`.

**Methods**:

| Method | Semantics |
|--------|-----------|
| `workflow/run/list` | `{ threadId, limit?, cursor? }` → `{ runs, nextCursor? }`. Sorted by `createdAt` then `runId`, descending; limit 1-200 (default 50); a cursor is valid only for the same thread and ordering. |
| `workflow/run/read` | `{ threadId, runId }` → `{ run }`. The authoritative projection for mounting, reconnect, and refresh after invalidation. |
| `workflow/run/pause` | Returns the paused `run` after active children and the worker are cancelled and the state is persisted. Idempotent. |
| `workflow/run/stop` | Accepts running or paused runs; returns after the stopped state and terminal event persist. Idempotent. The parent's exactly-once continuation proceeds independently and reports the run as stopped by the user. |
| `workflow/run/resume` | `{ threadId, runId, args? }` → `{ sourceRunId, run }`. Accepts `paused`, `stopped`, `failed`, or `succeeded` sources created by the current AppServer instance; creates a new run in the same thread (omitted `args` reuse the source's) and never mutates the source. |

**`workflow/run/updated`** (notification): `{ threadId, runId, reason }`, with open `reason` values such as `created`, `progress`, `control`, and `terminal`. Emitted only after the observable state is persisted. It is an invalidation signal: clients coalesce duplicates and call `workflow/run/read`.

**Errors** (stable `errorCode` with structured data): `workflow_run_not_found` (`runId`); `workflow_run_state_conflict` (`runId`, `currentStatus`, `allowedStatuses`); `workflow_resume_unavailable` (interrupted source or another AppServer instance; `runId`, `currentStatus`). Malformed identifiers, limits, cursors, or arguments use `invalid_params`. Failed control requests do not mutate the run or emit a notification.

### 11.7 Agent import

The `agent-import` module, gated by `capabilities.extensions.agentImport`, imports configuration and sessions from other agents (`claude-code`, `codex`, `cursor`) on the connected workspace's host. The import feature owns conversion, scope, and sync semantics.

| Method | Kind | Semantics |
|--------|------|-----------|
| `import/detect` | request | Optional `sources`; read-only. Returns source availability, candidate `items`, and importable counts. |
| `import/run` | request | `sources`, `items` (source, sourceId, fingerprint), `selection`, optional `offered` categories; returns `importId`. |
| `import/settings/get` | request | Sync state, sources, selection, interval, last sync, workspace opt-out, and whether imports exist. |
| `import/settings/set` | request | Optional `syncEnabled`, `sources`, `selection`; omitted fields keep their values. |
| `import/history/list` | request | Recent `imports` and actionable `attention` outcomes. |
| `import/progress` | notification | `importId`, `source`, `completed`, `total`. |
| `import/completed` | notification | `importId`, `trigger` (`manual`/`sync`), timestamps, outcomes. |

- Candidates carry source, category, user/workspace scope, paths, fingerprint, title, `state` (`new`, `changed`, `current`, `deferred`, `unsupported`), and a diagnostic reason; session candidates add cwd, updatedAt, and turn count.
- `selection` has `all` (which includes future categories), user and workspace category arrays (`skills`, `instructions`, `commands`, `hooks`, `mcp`, `plugins`; plugins are user-only), and `sessions`.
- Outcome statuses: `imported`, `appended`, `attention`, `existing`, `unsupported`, `deferred`, `skipped`, `failed`. Diagnostics never contain configuration secrets.
- Execution revalidates identities and fingerprints and reports `import_source_changed`; overlapping manual passes return `import_busy`; a failed item does not cancel others.

## 12. Versioning and Compatibility

### 12.1 Protocol Version

`serverInfo.protocolVersion` is `"1"`.

### 12.2 Compatibility rules

Unknown methods return `-32601`; clients ignore unknown optional fields. Session tool payloads use canonical `namespace`/`toolName` plus `providerFlatName`; Runtime Dynamic declarations and results have one canonical shape across all protocol consumers.

### 12.3 Negotiation

`initialize` advertises capabilities; it does not select among alternate wire shapes. An invalid request fails rather than silently changing shape.

## 15. WebSocket Transport

### 15.1 Overview

The WebSocket transport serves clients that are not co-located with the server process. Message shapes are identical to stdio; only framing and connection lifecycle differ.

### 15.2 Endpoint

The upgrade endpoint is `ws://HOST:PORT/ws`. The same HTTP server serves health probes:

- `GET /healthz`: `200 OK` with `{"status":"ok"}` while the process is alive.
- `GET /readyz`: `200 OK` once protocol startup is complete. Readiness does not wait for MCP servers; their state is reported through `mcpServerStatus/list` and `mcpServer/startupStatus/updated`.

The default listen address is `127.0.0.1`. Binding to a non-loopback interface must be configured explicitly and requires authentication.

### 15.3 Connection Lifecycle

Each connection starts unauthenticated and uninitialized with fresh state, performs the standard handshake ([Section 3.1](#31-handshake)), and on close (either side) has all of its thread subscriptions cancelled.

### 15.4 Authentication

With a configured bearer token, the client passes it in the upgrade URL: `ws://HOST:PORT/ws?token=<token>`. The server validates it before completing the upgrade and rejects a missing or invalid token with HTTP `401`, so the client never reaches `initialize`.

- Comparison is constant-time.
- An empty configured token disables authentication.
- Tokens are URL-safe (alphanumerics, `-`, `_`, `.`); others must be percent-encoded.
- On loopback-only binding authentication is optional; on a non-loopback binding the server refuses to start without a token.

### 15.5 Multi-Connection Behavior

Each connection has its own handshake, subscriptions, and backpressure gate (32 concurrent in-flight requests, as for stdio). Session state is shared: threads started on one connection are visible to others, a subscriber receives events triggered by other connections, and interactive requests follow [Section 7.6](#76-pending-requests-and-multiple-connections).

There is no per-connection identity isolation. Callers with different privilege levels use separate server processes or enforce identity through `SessionIdentity`.

### 15.6 Framing

Each JSON-RPC message is exactly one complete text frame; messages are never split or combined, and binary frames are not used. The server accepts client messages up to 4 MB; larger messages close the connection with code `1009`. Clients accept server messages up to at least 128 MiB, and a message over a client's limit ends the connection like any other transport failure.

### 15.7 Reconnection

There is no transport-level session resumption. After reconnecting, a client performs the full handshake again and re-registers its subscriptions. Turns in progress keep running; events emitted while disconnected are not replayed unless `thread/subscribe` uses `replayRecent`. Pending interactive requests stay pending ([Section 7.6](#76-pending-requests-and-multiple-connections)) and are replayed on resubscription.

Clients reconnect with exponential backoff with jitter (1 s to 30 s), including when they start before the server is reachable, and complete the handshake before issuing normal requests.

### 15.8 Native WebSocket Ping/Pong

The server pings every 30 seconds and closes connections that do not answer with a pong within 10 seconds.

### 15.9 Differences from Stdio

On stdio, disconnect means process exit, which cancels running turns and their approvals. On WebSocket, turns continue after disconnect, pending requests wait for another or reconnected client, and diagnostics are available only in server logs.

## 16. Automation management

The optional `automations` capability provides one automation definition and run lifecycle through `automation/*` methods and the `automation/run/updated` notification. Their models, version checks, events, scheduling, and delivery behavior are owned by the automations feature; they share the error codes in [Section 8.3](#83-dotcraft-specific-error-codes).

## 18. Skills Management Methods

### 18.1 Scope

These methods expose skill discovery and control. Skill sources, priority, and deduplication, the shared `~/.agents/skills` root, requirements metadata, authoring, and installation are defined in [Skills](../architecture/skills.md). On the wire, `source` is `workspace`, `plugin`, `builtin`, or `user`; the user-data root and the shared root both report `user` and differ only by `path`.

Gated by `capabilities.skillsManagement` (otherwise `-32601`).

### 18.2 `SkillInfo` Wire DTO

Skill-returning methods use `SkillInfo`. Non-obvious fields:

- `name`: skill directory name and identifier. `description` falls back to `name`.
- `displayName`, `shortDescription`, `defaultPrompt`, `iconSmallDataUrl`, `iconLargeDataUrl`: interface metadata from `agents/openai.yaml`. Icons resolve inside the skill's own `assets/`; a plugin skill may also use the owning plugin's root `assets/`. Invalid, oversized (over 512 KiB), or unsupported icons are ignored without discarding other metadata.
- `enabled`: `false` when disabled through `skills/setEnabled`.
- `path`: absolute path of the source `SKILL.md`.
- `metadata`: frontmatter key-value pairs.
- Plugin skills also carry `pluginId` and `pluginDisplayName`.

### 18.3 `skills/list`

Params `{}`. Result `{ skills: SkillInfo[] }`: all skills merged by source priority.

### 18.4 `skills/read`

Params `name`. Result `{ name, content, metadata }`: the resolved source `SKILL.md` including frontmatter, and parsed frontmatter (`null` when absent). Unknown skill → `-32040`.

### 18.5 `skills/view`

Params `name`. Result `{ name, content }`: the resolved `SKILL.md` body with frontmatter stripped, as `SkillView` returns it. It omits the source path and metadata.

### 18.6 `skills/setEnabled`

Params `name`, `enabled`. Result `{ skill }`. Persists the preference; disabled skills stay installed but are excluded from agent context. Setting the current state is a no-op. On change the server emits `config/changed` with `source: "skills/setEnabled"` and `regions: ["skills"]`. Unknown skill → `-32040`.

### 18.7 `skills/uninstall`

Params `name`. Result `{ name, uninstalled, source, removedSourcePath }`.

Only `workspace` skills and `user` skills under the user-data `skills/` root are uninstallable. `builtin`, `plugin`, shared-root skills, and paths outside the expected root are rejected with `-32602` (clients offer `skills/setEnabled` instead). Success also clears the disabled-state record and emits `config/changed` with `source: "skills/uninstall"` and `regions: ["skills"]`. Unknown skill → `-32040`.

### 18.8 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32040` | `SkillNotFound` | The skill name does not exist in any source. |

## 18B. Plugin and Marketplace Management Methods

### 18B.1 Plugin Management Methods

`capabilities.pluginManagement` gates the `plugin/*` methods below and `plugin/snapshot/updated`. It does not gate `plugin/config/*` ([Section 18B.2](#18b2-plugin-configuration-methods)); `plugin/desktop/read` additionally requires `capabilities.desktopPluginArtifacts`. Plugin structure, bundled MCP servers, and contained skills are defined in [Plugin Architecture](../architecture/plugin-architecture.md); the .NET runtime lifecycle, trust model, states, and unload semantics are defined in [.NET Extension Runtime](../architecture/dotnet-plugins.md). These methods project them.

`installed`, configured `enabled`, `dotnetRuntime.state`, and `dotnetRuntime.trustStatus` are separate facts. A `dotnet` plugin's runtime state affects only its in-process contributions, never its Skills, MCP or LSP servers, Hooks, Apps, Desktop module, Workflows, or interface metadata. Listing or viewing a plugin never loads plugin code.

#### `plugin/list`

Returns discovered plugins, including disabled installed plugins and installable catalog entries (bundled or from configured marketplaces). Params: `includeDisabled` (default `true`; `false` yields a partial view). Result: `plugins`, `diagnostics`, `snapshotRevision`, `workspacePath` (the server's absolute workspace path, used with the client's remote source identity for Desktop module grants and cache ownership; never authorizes reading that path on the client), and, with `capabilities.pluginMarketplaces`, `marketplaces`.

`MarketplaceInfo`: `name` (from the marketplace document), optional `displayName`, `sourceType` (`git`, `local`, `archive`), resolved `source`, `ref` and `sparsePaths` (git only), `marketplacePath`, materialized `root`, `lastUpdated`, `revision`, `removable` (`false` for the host-provided default marketplace), and `pluginIds` in marketplace order. Plugins contributed by a marketplace carry `marketplaceName`, kept while the DotCraft-managed install remains; bundled, workspace-local, and user-owned plugins omit it.

#### `plugin/view`

Params `id`. Result `{ plugin, snapshotRevision }`.

`PluginInfo` semantics beyond the obvious:

| Field | Semantics |
|-------|-----------|
| `installed` | The plugin exists in a discovered local root and can contribute runtime behavior. |
| `installable` | A known bundled or registry catalog entry that is not installed. |
| `removable` | A directory under `<craft-home>/plugins/<id>` or the workspace `.craft/plugins/<id>` that DotCraft can remove. |
| `functions` | Metadata for in-process Tools published by the current active .NET generation; empty otherwise. |
| `skills` | Declared skills (`name`, `description`, `displayName`, `shortDescription`, `enabled`, optional icons from that skill's own interface metadata, available for catalog previews too). Enablement is managed through `skills/setEnabled` on the matching `skills/list` entry. |
| `workflows` | Safe workflow summaries (`name`, namespaced `command`, `description`, optional `whenToUse`); never script source or paths. |
| `apps` | App Binding descriptors (`appId`, `displayName`, `developerName`, `description`, optional `category`, `icon`, `nativeApplication`). Catalog metadata only: execution fields (`toolNamespace`, `scopes`, `toolCatalog`, `dynamicToolCatalog`) are invalid; binding capabilities come solely from the binding-scoped MCP session. |
| `desktop` | Validated Desktop module metadata: optional `description`, manifest-relative `entry` and `styles`, and executable content `revision`. |
| `hooks` | Hook `key` (correlates with `hooks/list`) and `eventName`. |
| `mcpServers` | Bundled MCP declarations: `name`, `runtimeName` (connection/routing identity, usually `{pluginId}:{name}`; never a model-visible namespace), `transport`, `enabled`, `active` (installed, enabled, and not shadowed), optional `shadowedBy` (`workspace` or `plugin`). Not editable workspace config. |
| `diagnostics` | Safe discovery, admission, and preflight diagnostics for this plugin. |
| `dotnet` | Present when the manifest's `dotnet` block passes structural admission: optional `displayName`/`description` (clients fall back to plugin text), manifest-relative `entryAssembly`, `entryType`, `exportedApiAssemblies`, and `minHostVersion` (a hard admission floor; a lower host keeps the plugin `blocked` with `PluginHostVersionUnsatisfied`). |
| `dependencies` | Present (possibly `[]`) exactly when `dotnet` is present, sorted by provider id. Each has `id`, `requiredVersion`, `observedVersion` (`null` when missing or unversioned), and `availability`. |
| `dotnetRuntime` | Present once the runtime has accepted a structurally admitted snapshot for the id (preflight failure projects as `blocked`, not omission). Uninstalled catalog entries omit it, but still report `dotnet` and `dependencies` from non-executing manifest inspection so clients can disclose in-process code before install. |

`availability` is `missing`, `versionUnsatisfied`, `disabled`, `unavailable`, or the provider's runtime state, evaluated in that precedence: no effective installed provider; no canonical version, too old, or outside the compatible release line; configured disabled; enabled but no observed runtime state; otherwise its state. Only `active` satisfies a dependency.

`dotnetRuntime` (`PluginDotnetRuntimeInfo`): `state` (`stopped`, `blocked`, `activating`, `active`, `deactivating`, `faulted`, `reclaiming`), `generationId` (opaque, diagnostic only, never accepted as authority), `blockers`, `leakedGenerations`, `restartRecommended`, and `trustStatus` (`untrusted`, `trusted`, `modified`).

- `blocked` is non-terminal admission: activation was not attempted and is re-evaluated when its cause can change. `faulted` means activation was attempted and failed. Both are retried by disabling and re-enabling.
- `reclaiming` is observability, not a fault: the plugin is deactivated and routes nothing while memory release finishes; it blocks nothing. Clients suggest a restart only when `restartRecommended`.
- Any `trustStatus` other than `trusted` keeps the plugin `blocked` and is the cue to offer trust confirmation. `modified` means a grant exists for different bytes, so confirmation must be re-obtained.

Blockers and diagnostics share `code`, English `message`, and `parameters` (a shallow object of scalars or scalar arrays; never nested objects, credentials, exceptions, stack traces, or private paths). `PluginDiagnostic` adds `severity`, `pluginId`, and `path`. Clients localize by `code` and `parameters`; the server never localizes.

Lifecycle codes (diagnostics on `PluginInfo`; errors also appear as blockers): `PluginDotnetAdmissionFailed`, `PluginHostVersionUnsatisfied`, `PluginEntryAssemblyMissing`, `PluginEntryAssemblyInvalid`, `PluginDependencyManifestMissing`, `PluginTargetFrameworkMismatch`, `PluginEntryTypeInvalid`, `PluginApiExportInvalid`, `PluginUntrusted`, `PluginTrustModified`, `PluginDependencyUnsatisfied` (`reason` is `missing`, `versionUnsatisfied`, `disabled`, or the provider state), `PluginDependencyCycle`, `PluginApiAssemblyConflict`, `PluginServiceExportMissing`, `PluginActivationFailed`, `PluginActivationTimeout`, `PluginDrainTimeout`, `PluginCleanupFailed`, `PluginToolContributionInvalid`.

Operation-scoped codes (only in `PluginOperationResult.diagnostics`): `PluginRuntimeNotDeclared`, `PluginCandidateInvalid`, `PluginOperationIncomplete`, `PluginTrustNotPersisted`, `PluginContributionQuiesceFailed`, `PluginFilesystemCommitFailed`.

##### Snapshot revision

`snapshotRevision` is a non-negative integer (≤ 2^53 − 1) scoped to the workspace runtime instance: the revision of the complete plugin-management snapshot. It advances once per committed batch that changes any `PluginInfo` field, plugin membership, the marketplace projection, or the accepted .NET snapshot identity (even when no wire field changes). One atomic batch shares one revision; the sequence never decreases on a connection.

- Only an unfiltered `plugin/list` is a complete snapshot that may advance a client's complete-list baseline. Filtered lists, `plugin/view`, and mutation results refresh only the records they contain.
- Clients track the greatest revision observed from any result or notification. A lower-revision result never overwrites a record, absence decision, or baseline established at a higher one; an unfiltered list advances the baseline only if its revision is at least the greatest already observed (otherwise the client requests another).
- Clients reset their baseline after `initialize`.

#### `plugin/desktop/read`

Downloads an installed, enabled plugin's Desktop module over the authenticated transport. Params `{ id, revision, offset }`; result `{ totalBytes, dataBase64 }`.

- Offset zero opens (or replaces) the connection's current download; later offsets read the same plugin and revision in order, at most 1 MiB per chunk. The final chunk closes the transfer; a negative offset cancels it.
- The server resolves the root from its own discovery, checks the revision, and serves an immutable temporary ZIP of `.craft-plugin/plugin.json` (identity and desktop entry/styles) plus the complete `desktop/dist` tree. Eligibility and revision are revalidated after any plugin mutation and before completing; changes, cancellation, replacement, and disconnect dispose the artifact. Errors carry a stable `data.code`.
- Clients validate archive paths and limits and verify the revision before caching; no remote path is treated as local, and executing the cached module requires an authorized current workspace target.

#### `plugin/skill/read`

Params `id`, `name`. Result `{ id, name, content }`: the source Markdown of a skill declared by a discovered plugin, installed or catalog. The server resolves the file from the discovery snapshot; clients cannot supply paths. Unknown plugins and skills are invalid params. Reading never installs, enables, loads, or executes anything.

#### `PluginOperationResult`

Every plugin mutation returns:

- `outcome`: `applied` (the filesystem, configuration, or runtime commit occurred; the resulting .NET state may still be `blocked` or `faulted`, and a committed bundle is never rolled back for that), `noChange` (already current; nothing written), or `notApplied` (no commit; installed root and configuration unchanged; `diagnostics` explains).
- `plugin`: final projection of the selected plugin, or `null` when no local or catalog entry remains (typically after removal).
- `affectedPlugins`: final `{ id, installed, enabled, dotnetRuntime }` projections of every other plugin the batch changed (the reverse-dependency closure), sorted by id. No operation implicitly changes another plugin's installed state or enablement intent.
- `diagnostics`: operation-scoped diagnostics only.
- `snapshotRevision` after the operation.

Dependency blockers, untrusted plugins, and activation failures after a committed mutation are typed results, not JSON-RPC errors. Requests that cannot be attempted at all — unknown id, uninstalled target, non-removable root, ownership mismatch — fail with `-32602`.

Successful mutations other than `plugin/setTrusted` refresh plugin-contributed skill sources, reconcile MCP, LSP, and hooks runtime state, and emit `config/changed` with `source` set to the method name and `regions: ["plugins", "skills", "mcp", "lsp", "hooks"]`.

#### `plugin/install`

Params `id`. Installs a known catalog plugin for the user (available in every workspace) by copying it to `<craft-home>/plugins/<id>` with a source fingerprint marker, and clears the workspace's disabled record for the id. Bundled entries require configured bundled source roots; registry entries require a loadable registry source. An installed id returns `noChange`. Install never installs, enables, updates, or fetches dependencies and never grants trust, so a new `dotnet` plugin returns `applied` with its runtime `blocked` on `PluginUntrusted`.

#### `plugin/installLocal`

Params `path` (absolute path of a plugin root containing `.craft-plugin/plugin.json`). The manifest is validated first; a missing directory, non-plugin root, manifest error, or an id already installed in the user-global root fails with `InvalidParams` and writes nothing. On success the directory is copied to `<craft-home>/plugins/<id>` as a user-owned, enabled, removable plugin.

#### `plugin/remove`

Params `id`. Deletes only removable directories inside `<craft-home>/plugins` (removing it from every workspace) or the workspace `.craft/plugins`, including managed catalog installs and `installLocal` plugins; explicit external roots are rejected. An uninstalled catalog entry returns `noChange`.

Before deleting, the server quiesces the plugin's in-process generation and its consumers (consumers first) and its root-backed contributions (MCP and LSP processes, hooks, workflows). If quiescing or deletion fails, the root is unchanged, the runtime is reconciled back, and the result is `notApplied` with `PluginContributionQuiesceFailed` or `PluginFilesystemCommitFailed`.

#### `plugin/setEnabled`

Params `id`, `enabled`. Turns an installed plugin on or off for the active workspace by writing the workspace layer's `Plugins.DisabledPlugins`; an uninstalled plugin is rejected, and repeating the configured value returns `noChange`.

Enabling a `dotnet` plugin activates it only when every other precondition holds; it never grants trust. Disabling persists the intent and tears down the live closure consumers-first; it always applies, and a generation whose memory is not yet reclaimed projects `enabled: false` with `state: "reclaiming"`.

#### `plugin/setTrusted`

Params `id`, `trusted`. Grants or revokes the trust confirmation that lets a `dotnet` plugin run in-process code with the host's full privileges.

- Clients must present the confirmation at least as prominently as the plugin-hook trust flow and state the full-process consequence in it.
- The client sends only the id; the server binds the grant to the fingerprint of the bundle it accepted, so a client cannot confirm one set of bytes and authorize another. Any byte change moves the plugin to `modified` and requires a fresh confirmation.
- Granting persists the grant and replans the closure, so a plugin blocked only on trust becomes `active` in the same operation. If the grant cannot be persisted, nothing activates and the result is `notApplied` with `PluginTrustNotPersisted`.
- Revoking stops running code: the closure is torn down and returns to `blocked`; affected consumers appear in `affectedPlugins`.
- A plugin the runtime has not accepted returns `notApplied` with `PluginRuntimeNotDeclared`; repeating the current state returns `noChange`.
- It does not change enablement and emits no `config/changed`; it advances `snapshotRevision` and emits `plugin/snapshot/updated`.

#### `plugin/snapshot/updated`

**Direction**: server → client (notification). `{ snapshotRevision, pluginIds }`: an invalidation signal after a committed batch advanced the revision. `pluginIds` lists, sorted and unique, the ids whose projection changed; it may be empty when only marketplaces or list-level diagnostics changed.

- Per connection, notifications arrive in non-decreasing revision order; duplicates are idempotent. There is no ordering between a mutation response and this notification, so clients apply the snapshot-revision merge rule to both.
- On a revision newer than its baseline, a client marks the complete list stale and recovers with an unfiltered `plugin/list` (`plugin/view` cannot prove absence). Reconnects and gaps are recovered the same way. Clients may coalesce invalidations.
- A .NET runtime-only transition emits this notification without `config/changed`.

### 18B.2 Plugin Configuration Methods

Gated by `capabilities.pluginConfiguration` alone. Configuration is schema-backed and stored in the personal and workspace `plugin-config.json` documents defined by [Plugin Architecture](../architecture/plugin-architecture.md).

#### `plugin/config/get`

Params `id`. Result: `schema` (the validated manifest-owned schema), `personal` and `workspace` (the exact validated namespace values from each document, or `{}`), `value` (the recursively merged effective object), and `writableScopes` (`personal` when a user data path is configured, `workspace` when a workspace data path is available).

#### `plugin/config/mutate`

Params `id`, `scope` (`personal` or `workspace`), `operations` (`set` with a valid `value`, or `unset` to remove that scope's override). Atomically changes one plugin namespace in one scope and returns the new snapshot.

- Keys match declared schema fields case-insensitively and are stored with canonical spelling. Operations apply in order and the complete resulting namespace must validate before the file is replaced.
- Mutations are serialized with plugin lifecycle changes. An active .NET closure is quiesced before writing, reconciled after success, and restored if the write fails.
- Success emits `config/changed` with `source: "plugin/config/mutate"` and `regions: ["plugins.config"]`; there is no finer-grained configuration notification.

Errors use `-32099` with `error.data.code`: `PluginConfigurationNotDeclared`, `PluginConfigurationScopeUnavailable`, `PluginConfigurationDocumentInvalid`, `PluginConfigurationNamespaceInvalid`, `PluginConfigurationMutationInvalid`, `PluginConfigurationWriteFailed`. Invalid persisted configuration also appears as a safe plugin diagnostic and does not disable the AppServer or unrelated contributions. The protocol has no revision, etag, conflict payload, secret field, or migration endpoint for plugin configuration.

### 18B.3 Marketplace Management Methods

Gated by `capabilities.pluginMarketplaces` (which also gates `plugin/list.marketplaces`). Source kinds, syntax, fetch requirements, and security boundaries are defined in [Plugin Registry](../architecture/plugin-registry.md).

- Marketplace sources live in user-global configuration and are available in every workspace. Adding one never installs a plugin; its entries become installable catalog items.
- These methods are the only place a marketplace fetch happens; discovery reads existing materialized roots and caches.
- Committed changes visible in an unfiltered `plugin/list` advance `snapshotRevision` and emit `plugin/snapshot/updated`, in addition to `config/changed` with `source` set to the method name and `regions: ["plugins"]`.

#### `marketplace/add`

Params: `source` (repository shorthand, repository URL, or local directory), optional `ref` and `sparsePaths` (repository sources only; `ref` overrides one embedded in `source`), optional `marketplacePath` (default `.craft/plugins/marketplace.json`). Result: `{ marketplace, alreadyAdded }`.

The server fetches into staging when materialization is required, validates the document, takes the marketplace name from the document (never from the client), atomically replaces the installed root, records the entry, and refreshes discovery. `alreadyAdded` is `true` when a configured marketplace with the same kind, source, ref, and sparse paths still has a valid document; the server then returns without fetching. Nothing is written on failure.

#### `marketplace/remove`

Params `name`. Result `{ name, removedRoot? }` (`removedRoot` only when a materialized root was deleted). Local marketplaces are unlinked without touching the user's directory. Installed plugins stay until `plugin/remove`. The host-provided default marketplace is not removable; it is controlled by `Plugins.DisableDefaultPluginRegistry`.

#### `marketplace/refresh`

Params optional `name` (omitted refreshes all). Result `{ marketplaces, errors }`, where each error has `name`, `code`, and `message`. One marketplace's failure does not fail the others; the request fails only when a named marketplace does not exist.

### 18B.4 Error Codes

`-32093` (`MarketplaceSourceInvalid`) and `-32094` (`MarketplaceFetchFailed`) carry structured data with a stable `code`, `messageKey`, and `fallbackText`:

| `code` | Meaning |
|--------|---------|
| `MarketplaceSourceInvalid` | Empty, malformed, unsupported scheme, embedded credentials, or ref/sparse paths on a non-repository source. |
| `MarketplaceNameConflict` | Name already configured from a different source. |
| `MarketplaceNotFound` | Named marketplace not configured. |
| `MarketplaceNotRemovable` | Host-provided marketplace. |
| `MarketplaceDocumentMissing` | No valid marketplace document at the resolved path. |
| `MarketplaceVersionControlUnavailable` | No usable version control executable on the host. |
| `MarketplaceRefNotFound` | Requested ref does not exist. |
| `MarketplaceAuthenticationFailed` | Credentials required but not available non-interactively. |
| `MarketplaceFetchTimeout` | Fetch exceeded its time budget. |
| `MarketplaceFetchFailed` | Fetch or root replacement failed otherwise. |

## 18A. Tool Catalog Methods

### 18A.1 Scope

`tool/list` exposes the server's **built-in tools** — every profile-managed tool the server's own assemblies and compiled modules can register, independent of workspace, thread, MCP, or plugin configuration — so clients can offer pickers for Agent Profile `tools.allow`/`tools.deny`. Tools registered only under conditions, such as image generation or ChatGPT web search, are listed whether or not the condition currently holds. Runtime-managed tools, which Agent Profiles do not filter, are not listed. MCP tools, plugin tools, and skills are listed by their own methods. Gated by `capabilities.toolCatalog`.

### 18A.2 `ToolInfo` Wire DTO

- `name`: the tool's provider-facing flat name, exactly as matched by `tools.allow`/`tools.deny` (for example `image_gen__imagegen` for the namespaced tool `image_gen.imagegen`).
- `description`: may be empty. For a conditionally registered tool it states the condition.
- `source`: always `builtin`.
- `planMode`: `true` when the tool is allowed in Plan (read-only) mode, including conditionally restricted tools (for example `Exec`, read-only commands only, and `WriteStdin`, empty input only); `false` for tools Plan mode hard-denies (for example `WriteFile`, `EditFile`).

### 18A.3 `tool/list`

Params: optional `mode` (`plan` returns only `planMode` tools; omitted, `agent`, or unknown returns the full catalog). Result `{ tools }`, sorted by name (ordinal), deterministic for a server build, with no workspace, thread, or connection state.

## 19. Command Management Methods

### 19.1 Scope

Gated by `capabilities.commandManagement`. `command/list` returns server-registered command metadata; `command/execute` runs a slash command through the server pipeline. Resolution and execution are server-authoritative; client-local UX commands are outside this registry.

### 19.2 `CommandInfo` Wire DTO

`name` (canonical slash name), `aliases`, `descriptionKey` (localization key; empty for custom commands without one), `fallbackDescription` (English; `description` is an alias), `category` (`builtin` or `custom`), and `requiresAdmin`.

### 19.3 `command/list`

Params: optional `includeBuiltins` (default `true`). Result `{ commands }`. Composer pickers that insert `commandRef` parts pass `includeBuiltins = false` to get the custom-command catalog.

### 19.4 `command/execute`

Execute one slash command. Built-in commands must be invoked here (or by dedicated controls), never as `commandRef`.

**Params**: `threadId` (execution context), `command` (for example `"/stop"`), optional `arguments` (otherwise parsed from `command`), optional `sender` (permission and audit).

**Result**: `handled`; `message` and `isMarkdown`; `expandedPrompt` (when non-null, the command expanded to a prompt the caller may submit with `turn/start`); `thread` (the conversation's new current thread, when the command changed it); `choices` (follow-ups `{ label, command }`).

- When `thread` is present the caller switches its current thread immediately (`/new` and `/resume` return it). A thread created by `/new` may not be materialized until its first Turn.
- On a thread with a non-revoked App Binding channel binding, `/new`, `/resume`, and `/sessions` change nothing and answer that the chat must first be unbound in DotCraft.
- A choice's `command` is a complete slash command executed through `command/execute` as if typed; choices accompany `message`.
- Each built-in command has a busy behavior while the thread has an active Turn: `run`, `interruptFirst` (cancel the Turn, then execute), or `reject` (busy reply). The in-chat command set and busy behaviors are owned by the channel messaging feature.
- Admin-only commands fail with `CommandPermissionDenied` unless `sender.senderRole` is `"admin"`; a request without `sender` from a non-adapter connection acts as admin.

### 19.5 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32060` | `CommandNotFound` | Command not registered. |
| `-32061` | `CommandPermissionDenied` | Admin-only command without permission. |
| `-32062` | `CommandServiceUnavailable` | Backing service unavailable. |

## 19A. Background Terminal Methods

### 19A.1 Scope

Server-managed, pipe-based host shell processes that may outlive the `Exec` call that started them. Clients can list, read, write stdin, stop, and clean a thread's sessions; PTY behavior and sandbox process persistence are out of scope. Gated by `capabilities.backgroundTerminals` (otherwise `-32601`).

### 19A.2 `BackgroundTerminal` Wire DTO

A terminal snapshot carries `sessionId`, owning `threadId`, originating `turnId` and `callId` (correlating it to the `Exec` `toolCall` item that shows its output), `command`, `workingDirectory`, `source`, `status` (`running`, `completed`, `failed`, `killed`, `timedOut`, `lost`), bounded `output`, `outputPath` (complete log file), `exitCode`, timestamps, `wallTimeMs`, `originalOutputChars` (decoded UTF-16 characters excluding trailing line endings, saturating at `Int32.MaxValue`), `truncated`, and `backgroundReason` (for example `runInBackground`, or a hand-over after the yield time; both correlate to their `Exec` item the same way).

Snapshots retain at most 1 MiB of the latest output, including after server restart. `maxOutputChars` can shrink the preview but not lift the bound; nonpositive values use the retained preview. An output write failure terminates the process and reports a failed terminal rather than discarding output.

### 19A.3 Requests

| Method | Params | Result |
|--------|--------|--------|
| `terminal/list` | `threadId?` | `{ terminals }` |
| `terminal/read` | `sessionId`, `waitMs?`, `maxOutputChars?` | `{ terminal }` |
| `terminal/write` | `sessionId`, `input`, `yieldTimeMs?`, `maxOutputChars?` | `{ terminal }` |
| `terminal/stop` | `sessionId` | `{ terminal }` |
| `terminal/clean` | `threadId` | `{ terminals }` |

`terminal/read.waitMs` bounds how long the server waits for an updated snapshot or exit; on timeout it may return the current `running` snapshot.

### 19A.4 Notifications

With the client's `backgroundTerminals` capability the server emits `terminal/started`, `terminal/outputDelta`, `terminal/completed`, `terminal/stalled`, and `terminal/cleaned`, each carrying the terminal snapshot. Desktop-style clients use them as the live output source for Shell tools, including foreground `Exec` calls, scoped by `threadId` and `turnId`.

- `terminal/outputDelta` also carries the delta text, decoded without added line endings, at most 8 KiB per delta on character boundaries. Writes may be coalesced; clients concatenate in order and must not depend on line boundaries.
- Each terminal publishes at most 10,000 data deltas and at most `Tools.Shell.Background.OutputMaxBytes` bytes (64 MiB default); a slow consumer may also exhaust its bounded queue. At a limit, data deltas stop while the process continues and `outputPath` stays complete. `terminal/completed` is always published after accepted output is flushed and carries the final bounded snapshot.
- `terminal/stalled` reports a running terminal whose output stopped at what looks like an interactive prompt ([Tool Architecture](../architecture/tools-architecture.md#74-shell-execution)); it is published once until more output arrives.

## 19B. Remote Tool Host Routing Methods

These methods let a client route a thread's eligible native tools to a paired remote machine ([Remote Tool Host](../architecture/remote-tool-host.md)). They are the client counterpart of the model tools `RemoteToolHost.List`, `RemoteToolHost.Connect`, and `RemoteToolHost.Disconnect`, drive the same per-workspace client, and obey the same lease, catalog, and safety rules. A client route change is not a turn and is not recorded as tool use. Gated by `capabilities.remoteToolHost`.

Machine enrollment (pairings, invitations, revocation) uses the Hub Local API ([Hub Architecture](../architecture/hub-architecture.md) §6); `hostId` values are Hub peer ids so both surfaces share one identifier.

Invariants:

- Routes are runtime-only and never persisted; a thread resumed after a cold start is disconnected until reconnected.
- The wire never carries lease ids, Host instance ids, endpoints, tokens, certificates, or credential references.
- A failed connect is a JSON-RPC error; either the route is published or nothing changed.

#### `remoteToolHost/list`

Params: optional `threadId`. Result: `hosts` and, when `threadId` was given, that thread's `route` (or `null`). Never opens a remote session.

- Host entries: `hostId`, `displayName`, `online` (live control connection to Hub), exported `workspaces`, optional `errorCode` explaining why the machine cannot be used.
- Workspace entries: `workspaceId`, `displayName`, `available` (no other Agent Host holds the lease), `busyOwner` (`self`, `other`, or `null`), `leaseExpiresAt`.
- Route: `threadId`, `hostId`, `workspaceId`, `status` (`connected` or `leaseLost`), optional `environment` summary (`hostName`, `operatingSystem`, `userName`, `workspacePath`).

#### `remoteToolHost/connect`

Params `threadId`, `hostId`, `workspaceId`. Negotiates tools and file transfer, then publishes the route and broadcasts `remoteToolHost/route/changed` (`connected`). Setup failure keeps the previous route.

**Result**: `route`, `matchedTools` (now executed remotely), `unavailableTools` (unavailable while routed because the machine does not export them or their contract differs), and `alreadyConnected` (no transition emitted).

**Errors**: `-32100` with `error.data.code` one of `remote_host_not_registered`, `remote_host_offline`, `remote_authentication_failed`, `remote_protocol_mismatch`, `remote_satellite_offline`, `remote_satellite_session_failed`, `remote_workspace_not_found`, `remote_lease_lost`, `remote_hub_unavailable`; `-32101` with `remote_workspace_busy` and `params.owner`; `-32012` while a turn is in progress; `-32010` unknown thread; `-32602` malformed params. `messageKey` is `error.remoteToolHost.` plus the camel-case code (for example `error.remoteToolHost.remoteWorkspaceBusy`).

#### `remoteToolHost/disconnect`

Params `threadId`. Result `{ disconnected, previousRoute }`. Returns the thread to local execution and broadcasts `remoteToolHost/route/changed` (`disconnected`).

#### `remoteToolHost/route/changed`

Notification `{ threadId, reason, initiator?, route }`; honors opt-out.

- `reason`: `connected`, `disconnected`, or `leaseLost`. `initiator`: `client` (a `remoteToolHost/*` request), `agent` (a model tool), or `system` (the Agent Host itself, for example thread release or lease heartbeat loss). `route` is `null` after a disconnect.
- Every transition is forwarded from one per-workspace event, whichever surface caused it, so clients see model-driven changes exactly like their own, in one order.
- On lease heartbeat failure the server emits `leaseLost` once per affected thread and keeps the route (with `status: "leaseLost"`); there is no automatic local fallback. `remoteToolHost/list` and the next turn's runtime context report the same state. Only an explicit disconnect clears the route.

## 20. Channel Status Methods

### 20.1 Scope

`channel/status` reports configuration and runtime activity of social and external channels; `channel/list` reports only discoverable origin names. Gated by `capabilities.channelStatus` (otherwise `-32601`).

### 20.2 `ChannelStatusInfo` Wire DTO

- `name`: canonical channel name (as in `channel/list`); `category`: `social` or `external`.
- `enabled`: configuration state. `running`: server-observed activity.
- `runtimeState`: `stopped` (disabled or stopped), `starting` (an enabled external adapter is attaching or retrying), `running` (adapter handshake complete), or `failed` (automatic startup retries abandoned). Clients that understand it prefer it over inferring lifecycle from `enabled`/`running`, and preserve unknown values.
- `failureCode`: stable classification when available; an external adapter that reaches its consecutive-startup-failure limit reports `externalChannelStartFailed`. Diagnostics remain in channel logs.

### 20.3 `channel/status`

Params: none. Result `{ channels }`: only channels configured for status reporting, sorted by category (`social`, then `external`) and name (ordinal, case-insensitive); empty when there is no status data.

## 21. Provider And Model Catalog Methods

### 21.1 Scope

These methods list and probe model providers and discover models. `provider/list` and `provider/test` are gated by `capabilities.providerManagement`; `model/list` by `capabilities.modelCatalogManagement`; provider edits by `capabilities.workspaceConfigManagement`.

Provider records are personal configuration under `Providers`, created, edited, and deleted only through configuration writes in the user layer ([Section 25.4](#254-configuration-writes)); workspace configuration only selects a provider id and model id. A configuration write that changes `Providers` emits `config/changed` whose `regions` include `Providers` and the domain tag `providers`.

With a remote [model service](../architecture/model-service.md), provider records come from that service, carry `managedBy: "modelService"` and `isAuthenticated`, and expose no upstream credentials. Listing, testing an existing provider, model selection, and authentication status and usage remain available; draft testing and provider login/logout return InvalidRequest naming the management location, and writes under `Providers` fail with `configValidationError`.

### 21.2 `ProviderInfo` Wire DTO

Non-obvious fields:

- `protocol`: `openai-chat-completions`, `openai-responses`, or `anthropic`; a backend instance reports its backend's protocol.
- `backendId`: present for [model backend](../architecture/model-backend-plugins.md) instances.
- `isAuthenticated`: present when the provider's configured method supports sign-in or a model service manages it.
- `apiKey`: a redacted marker when a key exists; raw secrets are never returned. `hasApiKey` reports presence.
- `endPoint`: empty means the protocol's official endpoint.
- `streamMaxRetries` (default 5, range 0-100) and `streamIdleTimeoutMs` (default 300000): provider-specific stream reconnection settings; `networkTimeoutSeconds` overrides the timeout.
- `webSearchBackend`: optional built-in search backend (`"deepseek"` for official DeepSeek search); remote providers require service-advertised support.
- `supportsImageGeneration`: the provider serves the OpenAI Images API; `Tools.ImageGeneration.Enabled` must also be on before the image generation tool is exposed.
- `capabilities`: provider-neutral flags (streaming, tool calling, model listing, token usage, prompt-cache shaping, extended thinking, tool-choice controls, metadata passthrough, `responsesApi`, and `nativeDeferredToolLoading`, which is valid for OpenAI Responses and Anthropic beta tool-reference providers).

### 21.3 Provider Methods

`provider/list` returns configured personal providers (or the remote service's providers) with secrets redacted and defaults resolved: a missing display name becomes the id, the protocol is canonical, and `supportsImageGeneration` is effective. The server never synthesizes a provider that no configuration or service defines. With a remote model service, the result itself reports `managedBy: "modelService"`, even when no provider is currently available. Clients creating a provider check existing ids and may pass `expectedVersion` to avoid overwriting a concurrent edit.

`provider/test` probes a persisted provider (`{ providerId }`) or an unsaved draft (`protocol`, `apiKey`, `endPoint`, timeout and stream settings) by attempting model listing; it never sends a hidden chat request. The result mirrors `model/list` (`success`, `protocol`, `models`, `errorCode`, `errorMessage`) and never includes raw credentials. `EndpointNotSupported` is a normal outcome: clients still allow saving the provider and entering a model id manually.

### 21.4 `ModelCatalogItem` Wire DTO

- `id`: the model id used in provider preferences and thread configuration. `ownedBy` may be empty.
- `displayName`: optional name clients show instead of `id`.
- `reasoning`: optional server-authored capability metadata; clients use it rather than hardcoding compatibility. A model backend's models carry only the reasoning their backend reports, and preference repair and reasoning validation never apply built-in protocol catalogs to them; built-in providers use the merged model catalog. `supportsDisable` (an Off choice is allowed), `supportedEfforts` (`effort` among `low`, `medium`, `high`, `extraHigh`, `max`, `ultra`, plus a display `label`; `ultra` is advertised only when the model supports `max` and the Dynamic Workflow runtime is available), `defaultEffort`, `supportedOutputs` (`none`, `summary`, `full`), `defaultOutput`.
- `speed`: optional `supportedModes` (Fast-capable models list `standard` and `fast`) and `defaultMode` (`standard`). When missing, clients must not offer Fast. Model backend models never carry it.
- `contextWindow`: raw model capacity from the merged model catalog, independent of compaction limits.
- `isDefault`: exactly one `true` in a non-empty list — the model of the provider's effective preference (workspace over personal, as `thread/start` resolves it), or the first item when there is no preference or its model is not listed. Since `thread/start` without a model fails when the provider has no preference, a client starting on a default it did not read from a preference names `providerId` and `model` explicitly.

### 21.5 `model/list`

Params: optional `providerId` (omitted or null uses the workspace-selected provider). Result: `success`, `providerId`, `protocol`, `models`, and on failure `errorCode`/`errorMessage`.

- Provider or configuration errors still return a successful JSON-RPC response with `success: false` and a provider-neutral `errorCode` (for example `MissingApiKey`); clients continue to allow manual model entry.
- Results come from a cache keyed by resolved provider identity (id, protocol, endpoint, credentials), reused for five minutes. A failed refresh returns the last catalog for the same identity when one exists; an identity without a cached catalog is re-queried at most every thirty seconds while failing. Editing credentials or endpoint creates a new identity. `provider/test` always queries the endpoint.

### 21.6 Provider Authentication

`provider/backend/list` and `provider/auth/*` sign providers in and out and report account status and usage for built-in providers and model backends alike. [Provider authentication](provider-auth.md) defines them.

## 22. MCP Management Methods

### 22.1 Scope

Two surfaces:

- Configuration management — `mcp/list`, `mcp/get`, `mcp/upsert`, `mcp/remove` (gated by `capabilities.mcpManagement`) and `mcp/test` (gated by `capabilities.mcpStatus`).
- Source-aware runtime and control — the fixed method names in [Section 22.8](#228-mcp-runtimecontrol-methods), gated by `capabilities.mcpRuntime`. Runtime status uses `mcpServerStatus/list` and `mcpServer/startupStatus/updated`.

### 22.2 `McpServerConfig` Wire DTO

`McpServerConfig` has `name`, `enabled`, `transport` (`stdio` or `streamableHttp`), transport-specific fields, optional startup and tool timeouts, `toolApprovalMode` (`auto`, `approve`, or `prompt`; see [Tool Architecture](../architecture/tools-architecture.md#123-approval)), and, with `capabilities.mcpServerOrigins`, `origin` (`kind`, `pluginId`, `pluginDisplayName`, `declaredName`) and `readOnly`.

Validation:

- `name` is the logical primary key, compared case-insensitively.
- `stdio` allows only `command`, `args`, `env`, `envVars`, and `cwd`. `streamableHttp` allows only `url`, `bearerTokenEnvVar`, `httpHeaders`, `envHttpHeaders`, and optional `oauth`.
- `oauth` holds optional `clientId`, `clientSecret`, `clientSecretEnvVar`, `callbackUrl`, and `callbackPort`. A secret requires a client id; an environment reference takes precedence over a literal and must resolve to a non-empty value. OAuth cannot be combined with explicit Bearer/Authorization headers or with stdio.
- Callback URLs are HTTP loopback URLs without credentials, query, or fragment. An explicit URL port and `callbackPort` must agree; a configured port must be available and is never substituted. Without one, login uses an ephemeral port.
- Read projections mask a literal `clientSecret` and expose `hasClientSecret`. On `mcp/upsert`, omitted `oauth` keeps the existing configuration and `null` removes it; within an OAuth object, an omitted secret or the read mask keeps the stored secret (the mask is never stored), while `null` or empty removes it. Switching to stdio removes OAuth.
- `toolApprovalMode` accepts only `auto`, `approve`, or `prompt`; reads always report it. On `mcp/upsert`, an omitted mode keeps the existing server's mode, so a client that does not edit it never resets it, and a new server defaults to `auto`.
- Workspace-origin servers are editable. Plugin-origin servers are read-only runtime entries derived from plugin `.mcp.json`. Runtime status additionally reports thread and binding origins.

### 22.3 `mcp/list`

Result `{ servers }`: the effective servers for the workspace — workspace servers plus active plugin-bundled servers. Workspace MCP wins a runtime-name conflict; shadowed plugin servers appear only as declaration metadata on `plugin/list`/`plugin/view`.

### 22.4 `mcp/get`

Params `name`. Returns one configured server.

### 22.5 `mcp/upsert`

Params: required `scope` (`user` or `workspace`) and `server`. Creates or replaces the full logical entry of a non-plugin server in that scope; user-origin entries stay editable in their own scope, and listing reports the effective origin. Clients must not send plugin-origin servers; a name that resolves to a plugin-origin server returns `McpServerReadOnly`. Success emits `config/changed` with `source: "mcp/upsert"` and `regions: ["mcp"]`.

### 22.6 `mcp/remove`

Params: required `scope` and `name`. Removes a non-plugin server definition. Removing a plugin-origin server returns `McpServerReadOnly`; plugin-bundled MCP is controlled through the plugin lifecycle. Success emits `config/changed` with `source: "mcp/remove"` and `regions: ["mcp"]`.

### 22.7 Runtime identity and origin

Runtime operations resolve an effective server by optional `threadId`. Origins are `workspace`, `plugin`, `thread`, or `binding`. Status carries both `declaredName` and the collision-safe `runtimeName` with safe provenance. `runtimeName` selects the effective connection; it may contain source delimiters and must never be exposed as or parsed into a model tool namespace. Model-visible identity is the separately normalized tool name, while dispatch keeps the exact `runtimeName` and raw source tool id. Binding servers are additive and cannot shadow another runtime name. Thread `mcpServers` follows the three-state contract in [Section 4.1.1](#411-threadconfiguration-wire-shape).

### 22.8 MCP runtime/control methods

These method names and base shapes are fixed; DotCraft may add optional safe provenance fields but never renames them.

#### 22.8.1 `mcpServerStatus/list`

Params: optional `threadId` (omitted reads the latest workspace/plugin configuration), `cursor`, `limit`, `detail` (`full` default, or `toolsAndAuthOnly`). Result `{ data, nextCursor }`; each status includes `name`, nullable `serverInfo`, tools, resources, resource templates, `authStatus`, `origin`, `declaredName`, and `runtimeName`. MCP startup is asynchronous and never blocks AppServer readiness.

- `authStatus`: `unsupported`, `notLoggedIn`, `bearerToken`, or `oAuth`. Streamable HTTP alone does not imply OAuth; `notLoggedIn` is reported only after the OAuth challenge/discovery path establishes that login is required, and unknown discovery failures report `unsupported` with the startup error preserved. Clients offer Authenticate only for `notLoggedIn`.
- `failureReason = reauthenticationRequired` only when stored OAuth credentials can no longer be used.
- `toolsAndAuthOnly` skips resource enumeration and returns empty collections. In `full`, resource enumeration failure or timeout does not turn a ready server into failed.

#### 22.8.2 `mcpServer/resource/read`

Params `threadId?`, `server`, `uri`; result `{ contents }`. Thread and source authority are resolved before reading. No tool item is created.

#### 22.8.3 `mcpServer/tool/call`

Params `threadId`, `server`, `tool`, `arguments?`, `_meta?`. The call resolves the thread's effective snapshot and exact raw tool identity and goes through the same authority, schema, approval, hook, normalization, and result-limit dispatcher as model calls; it never invokes the MCP tool directly. The result keeps the raw MCP shape (`content`, `structuredContent`, `isError`, `_meta`); only normalized content ever enters model history.

#### 22.8.4 `mcpServer/oauth/login`

Params `name`, `threadId?`, `scopes?`, `timeoutSecs?`; result `{ authorizationUrl }`. Rejected unless the effective runtime's `authStatus` is `notLoggedIn`. Clients open only HTTPS or explicit loopback URLs. Completion arrives as `mcpServer/oauthLogin/completed`.

- Credentials are stored only in the user-scoped protected MCP auth store, partitioned by origin, runtime name, and endpoint identity — never in workspace config, Session items, or logs. Expired credentials that cannot refresh report `reauthenticationRequired`; clients restart login.
- Pre-registered client credentials are configuration, distinct from issued tokens, and are never copied into the token cache. Tokens issued to a different configured client id are unusable for access and refresh; updating the secret for the same client preserves the issuer binding and refresh. Without a configured client id, dynamic registration applies.
- `success: true` is emitted only after an authorization URL was returned, the loopback callback supplied a code with matching state, and issuer validation completed. Missing or mismatched state or issuer, or a callback error, fails the login. A server that starts without requesting OAuth fails the login request itself, with no contradictory success notification and no clearing of unrelated credentials.

#### 22.8.5 `config/mcpServer/reload`

Params none; result `{}`. Marks loaded threads' next tool snapshot dirty without reconnecting unchanged server generations.

#### 22.8.6 `mcpServer/startupStatus/updated`

Notification `{ threadId, name, status, error, failureReason, transport?, authStatus? }`, with `status` `starting`, `ready`, `failed`, or `cancelled`. The optional transport and auth fields let clients update a status row without guessing defaults.

#### 22.8.7 `mcpServer/oauthLogin/completed`

Notification `{ name, threadId, success, error? }`.

#### 22.8.8 `mcpServer/elicitation/request`

**Direction**: server → client request, requiring `capabilities.mcpElicitation`. Fields: `threadId`, nullable `turnId`, `serverName`, `mode`, nullable `_meta`, `message`. Result: `{ action: accept | decline | cancel, content, _meta }`; only `accept` may carry content.

- Form mode adds a standard MCP `requestedSchema`; the server validates accepted content against it. Unsupported schema shapes are safely declined.
- URL mode adds an HTTPS-or-loopback `url` and `elicitationId`; the client opens it only after explicit user action and does not copy page data into `content`.
- A disconnected or incapable client yields a stable client-unavailable failure, never approval. Elicitation may interrupt a tool call but creates no tool item.

### 22.9 `mcp/test`

Validates and probes a temporary configuration without persisting it. Result `{ success, toolCount }`; failures return a successful response with `success: false`, `errorCode` (`McpServerTestFailed`), and `errorMessage`.

### 22.10 MCP Apps opaque View methods

These methods implement the MCP Apps `io.modelcontextprotocol/ui` `2026-01-26` host boundary. Both server and client must advertise `mcpApps`. They are separate from App Binding methods and from the runtime methods in Section 22.8.

- Only a terminal `mcpToolCall` whose persisted UI association still matches the current MCP registration and authority is eligible; `view/open` loads the thread when needed and rejects other items.
- Every `viewHandle` is random and connection-owned, and internally binds thread, Turn, Item, server and origin, MCP generation, binding identity, authority revision, raw source tool identity, and resource URI. None of these appear as request parameters.
- Disconnect, thread archive/delete, successful `thread/rollback`, MCP generation replacement, binding revoke, plugin disable, configuration replacement, and close invalidate affected handles immediately; a reconnected same-named server cannot revive them.

#### 22.10.1 `mcpApp/view/open`

Params `threadId`, `turnId`, `itemId`. The server validates the item, persisted association, current registration and authority, resource declaration, and runtime; reads the declared resource with an independent bounded timeout; and returns a new `viewHandle`, the `resource` (absolute `ui://` URI matching the declaration, MIME `text/html;profile=mcp-app`, exactly one of `text` or `blob`, and `_meta.ui` with CSP, permissions, domain, and border preference), `toolInput`, and `toolResult`. No previous handle or view state is restored. A resource timeout reports `resource_timeout` without changing the server's ready state.

#### 22.10.2 `mcpApp/view/resource/read`

Params `viewHandle`, `uri`; result `{ contents }`. Resolved through the handle's exact server generation; cross-server and cross-generation reads are rejected. UI documents get the same validation as `view/open`.

#### 22.10.3 `mcpApp/view/tools/list`

Params `viewHandle`; result `{ tools }`: only current App-visible tools from the handle's server generation (raw name, description, schemas, annotations). Model-only, hidden, stale, and cross-server tools are omitted.

#### 22.10.4 `mcpApp/view/tool/call`

Params `viewHandle`, `tool`, `arguments?`. The raw name maps to the exact registration in the current thread snapshot; the server creates an `app_<guid>` call id and invokes the common dispatcher with App audience and no Turn, with lease, authority, schema, policy, hooks, approval, timeout, normalization, and result limits unchanged. The result keeps the bounded MCP shape. The call creates no Turn, Session item, or provider-history entry (it may emit a sanitized trace). At most four concurrent calls and 60 starts per minute per view.

#### 22.10.5 `mcpApp/view/message`

Params `viewHandle`, `message` (`role: "user"` with exactly one non-empty text block). The server starts a source-marked MCP App Turn when the thread is idle, or queues the input while a Turn is active. The input carries safe server, tool, and source-item provenance and cannot forge channel or user identity. The result says whether it started or was queued, with the turn or queue id.

#### 22.10.6 `mcpApp/view/modelContext/update`

Params `viewHandle`, `content` (null or empty clears); result `{}`. Each live view holds one last-write-wins pending value. A view message consumes its own view's value; an ordinary user Turn consumes all pending values for the thread. Consumed values are injected once as untrusted transient context, never persisted. Unknown content shapes reject the update.

#### 22.10.7 `mcpApp/view/openLink`

Params `viewHandle`, `url`; result `{ url }`. Returns a normalized URL only for HTTPS, `mailto`, or explicit loopback HTTP; `file`, `data`, `javascript`, and custom schemes are rejected. The client opens only the returned URL.

#### 22.10.8 `mcpApp/view/close`

Params `viewHandle`; result `{}`. Idempotent; clears authority, pending context, rate-limit state, and listeners.

#### 22.10.9 `mcpApp/view/status/updated`

Notification `{ viewHandle, status, reasonCode, fallbackText? }` with `status` `offline`, `revoked`, or `closed`, emitted when a handle is invalidated.

#### 22.10.10 Limits and errors

Limits: message and model-context content 16 KiB; resource or raw tool result 2 MiB; eight active views per thread and 32 per connection. Stable error categories: `McpAppViewNotFound`, `McpAppViewStale`, `McpAppViewOffline`, `McpAppViewRevoked`, `McpAppUnauthorized`, `McpAppApprovalRejected`, `McpAppInputInvalid`, `McpAppTimeout`, `McpAppProtocolError`, `McpAppResultTooLarge`.

### 22.10A Visualization Views

Both peers must advertise `visualizations`. These methods render current HTML fragments referenced by visualization content references in a completed agent message ([Tools Architecture §14.2](../architecture/tools-architecture.md#142-assistant-visualization-boundary)). They do not change Session shapes or persist visualization content; the only persisted state is the widget state snapshot.

#### 22.10A.1 `visualization/view/open`

Params `threadId`, `turnId`, `itemId`, `path` (the absolute path from the reference). Result `{ viewHandle, fragment, mimeType: "text/html", widgetState }`.

The server reloads authoritative thread state, verifies the completed item references the same normalized path, resolves it directly inside the thread workspace's `.craft/visualizations/<threadId>/` (never a user-global directory, regardless of execution or worktree overrides), reads the current file (so a rewritten visualization opens at its latest version), and returns a connection-owned handle bound to thread, turn, item, and path. `widgetState` (`modelContent`, `privateContent`) is the stored snapshot for the thread and path, or `null`.

#### 22.10A.2 `visualization/view/message`

Params `viewHandle`, `prompt`, `context?`; result `{ queuedInputId }`. Starts or queues a user turn with trigger kind `visualization`; the view cannot choose thread, identity, or channel. Non-null `context` is at most 32 KiB serialized and is appended to the message as a labelled fenced `json` block, so it stays visible in history.

#### 22.10A.3 `visualization/view/widgetState/update`

Params `viewHandle`, `modelContent?`, `privateContent?`; result `{}`. Replaces the snapshot for the handle's thread and path (omitted fields become `null`; both `null` removes it); at most 16 KiB serialized. Never starts or queues a turn. Model delivery of `modelContent` is defined by [Tools Architecture §14.2](../architecture/tools-architecture.md#142-assistant-visualization-boundary).

#### 22.10A.4 `visualization/view/close`

Params `viewHandle`; result `{ closed }`. Idempotent.

#### 22.10A.5 `visualization/widgetState/updated`

Notification `{ threadId, path, widgetState }` sent after a successful save to every other connection that advertised `visualizations` and subscribes to the thread; the saving connection does not receive it. `widgetState` is `null` when the save removed it.

Stable reason codes: `not_available`, `not_referenced`, `not_found`, `unsafe_path`, `stale_view`, `state_too_large`, `context_too_large`.

### 22.10B Generative UI State

Both peers must advertise `generativeUi`. These methods store the view state of Generative UI blocks — fenced `dotcraft-ui` blocks a client renders from a completed agent message — identified by thread, turn, item, and zero-based `blockIndex` among the item's blocks. The state is client view data: never sent to the model, never starting or queuing a turn, deleted with its thread, and not projected onto `agentMessage` items (clients read it once per thread).

#### 22.10B.1 `generativeUi/state/read`

Params `threadId`. Result `{ states }`, each `{ turnId, itemId, blockIndex, state }`, ordered by turn, item, then block index. Unknown thread → `not_found`.

#### 22.10B.2 `generativeUi/state/update`

Params `threadId`, `turnId`, `itemId`, `blockIndex`, `state?`; result `{}`. Replaces the block's state; omitted or `null` removes it. The turn must contain the item as an agent message (`not_found`), `blockIndex` must be non-negative (`invalid_block`), and the state must be at most 16 KiB of UTF-8 (`state_too_large`).

#### 22.10B.3 `generativeUi/state/updated`

Notification with the same fields as the update, sent after success to every other connection that advertised `generativeUi` and subscribes to the thread; `state` is `null` when removed.

### 22.11 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32070` | `McpServerNotFound` | Unknown server name. |
| `-32072` | `McpServerValidationFailed` | Config invalid for the transport. |
| `-32073` | `McpServerTestFailed` | Probe failed. |
| `-32074` | `McpServerNameConflict` | Case-insensitive logical key conflict. |
| `-32075` | `McpServerReadOnly` | Write targeted a plugin-origin server. |

## 22A. Hooks Management Methods

### 22A.1 Scope

These methods expose lifecycle hook metadata discovered from user config, workspace config, and enabled plugins. They never edit hook commands: command-bearing files remain the source of truth, and AppServer persists only per-user enablement and trust. Gated by `capabilities.hooksManagement` (otherwise `-32601`). The hooks feature owns runtime semantics.

### 22A.2 `HookMetadata` Wire DTO

Non-obvious fields:

- `key`: stable identity. Config hooks use `<absoluteSourcePath>:<snake_case_event>:<groupIndex>:<handlerIndex>`; plugin hooks use `<pluginId>:<sourceRelativePath>:<snake_case_event>:<groupIndex>:<handlerIndex>`.
- `handlerType`: `command`, or a reserved unsupported type reported with diagnostics.
- `matcher`: tool matcher for tool events; empty or null matches all. `condition`: optional command condition such as `Bash(git commit:*)`.
- `command`: declared command, with runtime variables expanded for plugin hooks.
- `executionMode` (`sync`, `async`), `asyncRewake` (may enqueue hook-origin follow-up feedback), `rewakeMessage`, `rewakeSummary`.
- `source`: `user`, `workspace`, `plugin`, or `unknown`; `pluginId` for plugin hooks.
- `displayOrder`: effective execution order.
- `enabled`: effective state after global enablement, per-hook disable, and trust.
- `isManaged`: reserved for runtime-managed hooks.
- `currentHash`: normalized hash over behavior-affecting fields, excluding machine-specific expanded paths. `trustStatus`: `trusted`, `untrusted`, `modified`, or `managed`.

### 22A.3 `hooks/list`

Params none. Result `{ hooks, warnings, errors }`; warnings and errors carry `source`, optional `path`, and `message`. Invalid plugin hook files are reported here and in plugin diagnostics without blocking other plugin contributions.

### 22A.4 `hooks/setState`

Params `key`, optional `enabled` (`false` disables; `true` clears a disable), optional `trustedHash` (the current `currentHash`, to trust or re-trust). Result: the `hooks/list` shape. State is written to user-global configuration under `Hooks.State`, so personal trust and disable choices never land in workspace config. Plugin hook UIs prefer `hooks/trustPlugin` for trust.

### 22A.5 `hooks/trustPlugin`

Params `pluginId`. Result: the `hooks/list` shape. Writes each current hook's `currentHash` into its per-hook `Hooks.State` entry; there is no plugin-level trust record and per-hook `enabled` is unchanged. The plugin must be enabled and contribute at least one hook, otherwise `-32602`.

Both mutation methods rebuild the runtime hook snapshot immediately, invalidate thread agent caches when the effective tool-hook set changes (so future turns rebind), and emit `config/changed` with `source` set to the method name and `regions: ["hooks"]`. Trust is hash-based: a changed hook reports `trustStatus: "modified"` and does not run until the new hash is trusted.

## 23. External Channel Management Methods

### 23.1 Scope

A server-authoritative read/write path for external channel configuration. Gated by `capabilities.externalChannelManagement` (otherwise `-32601`).

### 23.2 `ExternalChannelConfig` Wire DTO

`name`, `enabled`, `transport` (`subprocess`, `websocket`, `managedWebsocket`), and process-launch fields `command`, `builtinModule`, `args`, `workingDirectory`, `env`. Persistence is server-defined.

- `name` is the case-insensitive logical key and must not conflict with a reserved or existing channel name.
- `subprocess` and `managedWebsocket` require `command` or `builtinModule` and allow the launch fields; `managedWebsocket` activates only when AppServer WebSocket mode is enabled.
- `websocket` allows no launch fields because the adapter is started independently.

### 23.3 `externalChannel/list`

Result `{ channels }`: all configured external channels for the workspace.

### 23.4 `externalChannel/get`

Params `name`. Returns one channel.

### 23.5 `externalChannel/upsert`

Params `{ channel }`. Replaces the full logical entry. It is an explicit configuration mutation: replacing an active entry may stop and recreate its host, so clients must not use it to restore display state after reconnecting. Success emits `config/changed` with `source: "externalChannel/upsert"` and `regions: ["externalChannel"]`.

### 23.6 `externalChannel/remove`

Params `name`. Success emits `config/changed` with `source: "externalChannel/remove"` and `regions: ["externalChannel"]`.

### 23.7 `externalChannel/logs`

Params `name`, optional `tail` (default: the log provider's retention window). Result `{ name, lines }`. Hosts without an external channel log provider return `-32601`.

### 23.8 `externalChannel/configOptionAvailability`

Params `name`, `key`. Result `{ name, key, available }`, where `available` lists the option values the connected adapter reports for that configuration field ([External Channel Adapter](external-channel-adapter.md#44-extchannelconfigoptionavailability)), or is `null` when the adapter is not connected, does not answer, or cannot tell. A `null` result never blocks saving the configuration.

### 23.9 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32080` | `ExternalChannelNotFound` | Unknown channel. |
| `-32081` | `ExternalChannelValidationFailed` | Payload invalid for the transport. |
| `-32082` | `ExternalChannelNameConflict` | Case-insensitive conflict with an existing key or native channel. |

## 23A. Agent Profile Management Methods

These methods manage Agent Profile Markdown files, which compile into `ThreadConfiguration` snapshots for `thread/start` and reusable task runtimes (distinct from SubAgent launcher profiles). Gated by `capabilities.agentProfileManagement`.

Profile `id` references carry the canonical profile `name`: trimmed, Unicode NFC, compared ordinally, 1-240 Unicode scalar values, no control characters. Filenames are storage details and never define the name.

`source` values are `builtIn`, `plugin`, `user`, `workspace`, and `managed`; `builtIn`, `plugin`, and `managed` are read-only. Precedence is `builtIn` < `plugin` < `user` < `workspace` < `managed`; same-id profiles never merge, and when a request omits `source` the highest-priority valid profile is used.

### 23A.1 Profile Diagnostics

Diagnostics carry `severity`, a stable `code`, and an English fallback `message`; clients own localization. `PinnedProviderUnavailable` is a warning for a structurally valid profile whose pinned provider/model cannot resolve in the workspace: saving still succeeds, but thread creation fails until the runtime is available.

### 23A.2 `agent/profiles/list`

Params: optional `source`, `includeInvalid`. Result `{ profiles }`, each with `id`, `description`, `source`, `path`, `updatedAt` (file last-write time; omitted without a backing file), `fingerprint`, `valid`, `isBuiltIn`, `readOnly`, `shadowed`, `sourceStack`, `lockedFields`, `restrictedFields`, `trustRestricted`, `staleThreadIds`, and `diagnostics`.

### 23A.3 `agent/profiles/read`

Params `id`, optional `source`. Result `{ profile }` with `fingerprint`, `valid`, `diagnostics`, and `rawContent`.

### 23A.4 `agent/profiles/validate`

Params `rawContent`, `source`. Validates without writing. Result: `valid`, `diagnostics`, `summary`, and `compiledConfig`. `providerPreference` is omitted when the profile inherits model settings; when present it has `providerId`, `model`, `reasoning` (only `enabled` and `effort` — profile APIs never accept or emit reasoning output visibility), and `speed`.

### 23A.5 `agent/profiles/upsert`

Params `id`, `source` (`user` or `workspace`), `rawContent`. Creates or replaces a writable profile; the frontmatter `name` must match `id`. Optional `previousName` renames within the selected writable source without a client-side delete; a conflicting destination returns `AgentProfileConflict` and leaves the old document intact.

### 23A.6 `agent/profiles/remove`

Params `id`, `source`. Result `{ removed }`.

### 23A.7 `agent/profiles/refreshThread`

Explicitly refreshes one profile-backed thread from the currently resolved profile; profile file changes never mutate existing threads otherwise. Params `threadId`, optional `profileId` (defaults to the thread's `agentProfileId`).

A profile without `providerPreference` preserves the thread's provider/model/reasoning/speed snapshot; a present one replaces all of it, deriving reasoning output visibility from the selected model's catalog default. The full replacement is validated before the thread changes. Result: `threadId`, resolved `profile` identity, the new `config`, `wasStale`, and an `audit` record.

### 23A.8 Conversational Builder Draft Methods

Available only for threads whose `ThreadConfiguration.agentBuilderTargetId` is set; other threads return `-32602`. Drafts never write profile files and emit no config or file change notifications.

#### `agent/profiles/builderDraft/read`

Params `threadId`. Result `{ threadId, targetId, targetSource, rawContent }`. When no draft exists the server seeds it from the target profile, or an empty draft when the target does not exist yet.

#### `agent/profiles/builderDraft/update`

Params `threadId`, `rawContent`. Replaces the draft; result as `builderDraft/read`. Clients debounce structured-editor edits and must flush pending updates before sending a builder message, so every builder turn sees the current draft.

### 23A.8A Agent Packages

These methods move a user or workspace profile between installations as one file. The agent packages feature owns the file format, preview states, and what an import installs.

| Method | Purpose |
|--------|---------|
| `agent/profiles/export/plan` | File name, size bound, and the skills and plugins an export can carry, with why the profile relies on each. |
| `agent/profiles/export/read` | Builds the package for the chosen contents at offset `0` and returns it in chunks of at most 1 MiB; a negative offset discards it. |
| `agent/profiles/import/upload` | Receives an Agent package or Profile document in chunks of at most 1 MiB; the last chunk returns the preview. |
| `agent/profiles/import/commit` | Installs the chosen contents and creates the profile under the chosen name and source; returns the `agent/profiles/read` shape. |
| `agent/profiles/import/discard` | Discards an uploaded import. |

Uploads and exports are held for the requesting connection for at most one hour and discarded when it closes. Method-specific failures are `-32602` with `error.data.code` `agentPackageInvalid`, `agentPackageTooLarge`, `importExpired`, `agentNameTaken`, or `packageNotFound`; profile failures use the codes below.

### 23A.9 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32086` | `AgentProfileNotFound` | Unknown id/source. |
| `-32087` | `AgentProfileValidationFailed` | Markdown/frontmatter invalid, or an unsupported overlay with `agentProfileId`. |
| `-32088` | `AgentProfileProtected` | Write or remove targeted a built-in profile. |
| `-32089` | `AgentProfileSourceUnavailable` | Source unsupported or not readable/writable. |
| `-32091` | `AgentProfileConflict` | Id conflicts with the frontmatter name or a protected source. |

## 24. SubAgent Profile Management Methods

### 24.1 Scope

A server-authoritative read/write path for workspace SubAgent profile configuration. Gated by `capabilities.subAgentManagement` (otherwise `-32601`). Successful mutations emit `config/changed` with `source` set to the method name and `regions: ["subagent"]`.

### 24.2 `SubAgentProfileWrite` Wire DTO

`definition` payloads carry the full SubAgent profile definition, mirroring the `SubAgentProfiles.<name>` configuration shape without `name` (which travels in the request envelope): runtime, launch, input/output, resume, token-extraction, output-file, timeout, trust, permission-mapping, and sanitization fields.

### 24.3 `SubAgentProfileEntry` Wire DTO

- `definition`: the effective definition after built-in and workspace-override resolution. `builtInDefaults`: present only for built-in profiles.
- `isBuiltIn`, `isTemplate`, `isDefault`, `enabled`.
- `hasWorkspaceOverride`: the workspace currently persists `SubAgentProfiles.<name>`.
- `diagnostic`: `enabled`, `binaryResolved`, `hiddenFromPrompt` (effective prompt visibility after enablement, templates, runtime registration, binary resolution, and validation), optional `hiddenReason`, `warnings`.

### 24.4 `subagent/profiles/list`

Result `{ defaultName, settings, profiles }`: built-in profiles plus workspace custom profiles.

- `settings.externalCliSessionResumeEnabled`: whether external CLI profiles that support resume may reuse saved external session ids.
- `settings.providerPreferences`: complete native SubAgent preferences keyed by provider id; a missing entry inherits the parent thread's complete preference.

### 24.5 `subagent/settings/update`

Params: `externalCliSessionResumeEnabled`, `providerPreferences`, or both (at least one required).

- `providerPreferences` replaces the whole workspace map (`{}` clears it). Each record is normalized as a unit; an invalid model, reasoning, or speed rejects the request without writing.
- The resume toggle affects only profiles whose effective definition supports resume. Changing these settings never deletes saved external session ids.

### 24.6 `subagent/profiles/setEnabled`

Params `name`, `enabled`. Returns the updated `SubAgentProfileEntry`. `native` is protected and cannot be disabled.

### 24.7 `subagent/profiles/upsert`

Params `name`, `definition`. A built-in name creates or replaces a workspace override; any other name creates or replaces a custom workspace profile. The workspace persists the full expanded definition.

### 24.8 `subagent/profiles/remove`

Params `name`. Result `{ removed }`. A built-in name removes only its workspace override, restoring defaults (failing when there is none); a custom name removes the profile.

### 24.9 Error Codes

| Code | Constant | When |
|------|----------|------|
| `-32083` | `SubAgentProfileNotFound` | Profile or workspace override missing. |
| `-32084` | `SubAgentProfileValidationFailed` | Invalid or incompatible with runtime rules. |
| `-32085` | `SubAgentProfileProtected` | Targets a protected profile such as `native`. |

### 24.10 Session-Backed SubAgent Child Threads

With `capabilities.subAgentSessions`, profile-backed SubAgents are ordinary child threads plus a parent/child graph. Native profiles run real child turns; external CLI profiles persist synthetic child turns with the prompt, final output or error, and token metadata when available.

**Agent paths**: control targets use stable agent paths. The root is `/root`; each child appends its `taskName` (lowercase ASCII letters, digits, underscores; `root`, `.`, `..` reserved), for example `/root/researcher`. Relative targets resolve from the caller's path; absolute targets start with `/root`. `agentNickname` is display metadata; a child's `displayName` starts from the nickname or task name, and renaming never changes the path. `agentRole` selects the child's role (tool policy, Agent control exposure, prompt profile, instructions); external CLI runtimes receive role instructions but tool filtering cannot be enforced inside them.

**Lifecycle**: `thread/list` hides children unless `includeSubAgents`. Parent archive and delete apply recursively; parent unarchive restores only descendants whose edge is still open; direct child archive or delete is invalid.

#### `subagent/children/list`

Params `parentThreadId`, `includeClosed` (default `false`), `includeThreads`. Result `{ data: [{ edge, thread? }] }`.

- `edge` carries parent and child ids, `parentTurnId`, `depth`, `agentPath`, `taskName`, `agentNickname`, `agentRole`, `profileName`, `runtimeType`, the `supportsSendMessage`/`supportsFollowupTask`/`supportsClose` flags, and `status`. `status: "open"` means the relationship is available for path-based control, not that the child is running. Edges without `agentPath` cannot be path targets.
- With `includeThreads`, `thread` is the `thread/read` header, optionally with a `runtime` snapshot; clients use `thread.runtime.running` for activity and fetch bounded history only when expanding a child.
- A child's working size is `thread.contextUsage.tokens`, not cumulative billing.
- Closed edges are returned only with `includeClosed` (for audit or settings surfaces); background-agent widgets keep the default so closed agents disappear.

#### `subagent/sendMessage`

Params `parentThreadId`, `target`, `message`. Records an inter-agent mailbox message for the target without starting a child turn.

`subagent/sendMessage`, `subagent/followupTask`, and `subagent/close` return a `SubAgentControlResult`: resolved `agentPath`, `taskName`, `status` (for example `sent`, `running`, `queued`, `guidancePending`, `closed`), optional `message`, display and profile fields, and the three `supports*` flags. Internal child thread ids and resume flags are not serialized.

- Delivered mailbox input is persisted as a `userMessage` with `deliveryMode = "subagentMailbox"` and `triggerKind = "subagentMailbox"`; clients keep it for history but render it neither as a parent user bubble nor as a child reply.
- Mailbox delivery is serialized per target path, so sampling and follow-up tasks never consume the same entry concurrently.
- When a path-addressable child Turn ends, the direct parent receives a task notification with `triggerKind = "subagent"`, `triggerRefId` = child path, and `triggerStatus` = child Turn status ([Background Tasks](../architecture/background-tasks.md) §5). A running child closed through `subagent/close` is reported as stopped by the user. Child listing never exposes child final text.

#### `subagent/followupTask`

Params `parentThreadId`, `target`, `message`, `deliveryMode` (`queue` default, or `steer`). An idle target starts a new child turn regardless of mode. With an active turn, `queue` appends to the target's FIFO queue and `steer` promotes the task into current-Turn guidance for a running native SubAgent; running external SubAgents reject `steer`. Pending mailbox messages are delivered with the task. The input carries `triggerKind = "subagentFollowupTask"`, `triggerLabel` = target display label, and `triggerRefId` = target path.

#### `subagent/close`

Params `parentThreadId`, `target`. Rejects `/root` and self-targets, cancels an active child turn the server still owns, marks the edge closed, and archives the child subtree. Default child listing no longer returns it.

## 25. Configuration Methods

### 25.1 Scope

These methods read and change the connected server's configuration by key path. Layers, descriptors, validation, and how a change takes effect are defined in [Configuration](../architecture/configuration.md). Per-thread overrides use `thread/config/update`. Domain operations that persist configuration as a side effect (skills, plugins, MCP, hooks, external channels, SubAgent settings, source control) keep their own methods. Gated by `capabilities.workspaceConfigManagement` (otherwise `-32601`). Clients may set `capabilities.configChange = false` to suppress `config/changed`.

### 25.2 `config/schema`

Params `{}`. Returns the server-derived schema as `sections` (`section`, `order`, `path`, `fields`), each field with `key`, `type`, and `reload` (the reload behavior, camelCase) plus `subsystemKey` when `reload` is `subsystemRestart`. The payload is additive; clients ignore unknown properties.

### 25.3 `config/read`

Params: optional `includeLayers` (default `false`). Result:

- `config`: effective configuration keyed by on-disk property names — descriptor defaults overlaid by the user layer, then the workspace layer.
- `origins`: for each key path whose value comes from a file layer, `{ name, version }`; defaults are absent.
- `layers` (only with `includeLayers`): the user layer, then the workspace layer, each `{ name, version, config }`.

A layer `name` is `{ type: "user" | "workspace", file }`. A layer `version` is `sha256:` plus the hex digest of the layer's JSON with sorted keys; a missing file has the version of an empty object.

Non-empty sensitive values are masked as `"***"` everywhere. A value is sensitive when its field is marked sensitive in `config/schema`, or its key ends in `ApiKey`, `Token`, `Secret`, or `Password`, is `Authorization`, or is any value of a `Headers`, `EnvironmentVariables`, or `Env` map, at any depth.

`Providers` appears with the values stored in the files; resolved provider defaults come from `provider/list`.

### 25.4 Configuration Writes

#### `config/value/write`

Write one key path to one layer.

**Params**:

- `keyPath`: dot-joined on-disk property names from the root (for example `Tools.CodeMode.Mode`); a path inside an object-valued field such as `ProviderPreferences.openai` is accepted. Segments containing `.`, `"`, or `\` are double-quoted with `\` escaping (for example `ProviderPreferences."openai.personal"`).
- `value`: `null` removes the key and prunes emptied parents. A `"***"` string, as the value or a nested object member, keeps the stored value at that path; a `"***"` array element is written literally.
- `mergeStrategy`: `replace`, or `upsert` to deep-merge an object into the existing object. Unknown values return `-32602`.
- `filePath`: a layer `file` from `config/read`. Omitted targets the workspace layer, or the user layer for key paths under `Providers`.
- `expectedVersion`: the write fails unless the target layer still has this version.

**Result**: `status` (`ok`, or `okOverridden` when a higher-precedence layer still determines an edited field's value), the target layer's new `version` and `filePath`, and with `okOverridden` an `overriddenMetadata` (`message`, `overridingLayer`, masked `effectiveValue`).

#### `config/batchWrite`

Apply ordered `edits` (`{ keyPath, value, mergeStrategy }`) to one layer atomically, with `filePath` and `expectedVersion` as above. Omitted `filePath` targets the user layer when every edit is under `Providers`; a batch mixing `Providers` with other keys must name the user layer or fails with `configValidationError`. Result as `config/value/write`.

#### Write Semantics

- A write updates in-process configuration before the response, so later reads and Turns observe it; each field takes effect according to its `reload` metadata.
- Only fields whose effective value changes are checked against bounds and options; an unrelated invalid value does not block a write, but the whole configuration must still deserialize.
- Sensitive values are write-only: `"***"` keeps the stored value, `null` removes it, anything else replaces it. No result, error, or notification contains a sensitive value.
- Unrelated content of the target file is preserved.
- A write that changes at least one effective value emits `config/changed` with `source` set to the method name and `regions` listing the changed key paths; a write that changes nothing emits nothing.

#### Providers

`Providers` maps a provider id to an object with `DisplayName`, `Protocol`, `ApiKey`, `AuthMethod`, `BackendId`, `BackendSettings`, `EndPoint`, `NetworkTimeoutSeconds`, `MaxOutputTokens`, `StreamMaxRetries`, `StreamIdleTimeoutMs`, `SupportsImageGeneration`, and `SupportsFreeformTools`. Writing `Providers.<id>` or `Providers.<id>.<Field>` creates or edits a provider; writing `null` to `Providers.<id>` deletes it. Ids compare case-insensitively. To edit without touching the key, clients send the masked `"***"` back or omit it from an `upsert`.

- Provider edits persist to the user layer; naming another layer fails with `configValidationError`, as does any edit under `Providers` while a remote model service manages providers.
- Each changed entry needs an id that is non-empty without `/`, `\`, or `:`; a `Protocol` of `openai-chat-completions`, `openai-responses`, or `anthropic`; an absolute-URI `EndPoint` when non-empty; positive `NetworkTimeoutSeconds`, `MaxOutputTokens`, and `StreamIdleTimeoutMs`; `StreamMaxRetries` from 0 to 100; and an `AuthMethod` of `apiKey` or `chatgptOAuth` (which requires an OpenAI protocol). An entry with a `BackendId` needs an object `BackendSettings` and positive limits; its backend validates protocol and method.
- After editing `ProviderId` or `Providers`, a non-empty effective `ProviderId` must name a defined (or service-managed) provider; deleting or selecting an unavailable provider fails with `configValidationError`.
- Omitted fields are resolved at apply time as `provider/list` reports them; defaults are not written to the file.
- Changes take effect for new Turns, model listing, and provider tests without restart; `config/changed` lists `Providers` and the domain tag `providers`.

#### Write Errors

A rejected write leaves the file unchanged and returns `-32600` with `data.configWriteErrorCode`:

| `configWriteErrorCode` | Cause |
|------------------------|-------|
| `configLayerReadonly` | `filePath` is not the user or workspace layer file. |
| `configVersionConflict` | `expectedVersion` differs from the layer's current version. |
| `configValidationError` | The candidate does not deserialize, a changed field violates type, bounds, or options, a semantic rule fails, or a `Providers` rule above is violated. |
| `configSchemaUnknownKey` | The key path names no field and is not inside an object-valued field. |

### 25.5 `config/changed`

Notification `{ source, regions, changedAt }`, emitted after write completion and in-process state update.

- `source`: the RPC method or runtime operation that caused the change; each method states its `source` and `regions`.
- `regions`: changed key paths for `config/value/write` and `config/batchWrite`, or coarse domain tags for domain operations: `providers`, `skills`, `plugins`, `plugins.config`, `memory`, `lsp`, `mcp`, `hooks`, `externalChannel`, `subagent`, `sourceControl`. `providers` accompanies `Providers` whenever a change alters the provider set.
- Key paths match `config/schema` key paths ordinally. Clients refresh a configuration view when a path under a displayed prefix appears and refresh domain state (`skills/list`, `mcp/list`, …) when its tag appears. Unknown regions are ignored.

## 25A. Source Control Methods

### 25A.1 Scope

These methods bind a workspace to a source control provider, test Perforce connectivity, and expose the Perforce pending-changelist workflow: provider selection, connection configuration, connectivity testing, thread-scoped changelist targets, changelist creation, and changelist preparation. They never submit or shelve and are not a general version-control console.

- Gated by `capabilities.sourceControlManagement`, advertised when a workspace `.craft` path is available (otherwise `-32601`). Perforce changelist UI and RPCs are additionally gated by `sourceControl/get.capabilities.perforceChangelist`, which is `false` while Perforce is offline.
- All Perforce work runs in the AppServer environment with the workspace as working directory, so results reflect the machine, PATH, and credentials that own the workspace. Clients never run `p4`.
- Credentials are never persisted: `sourceControl/update` rejects any password field, and the `password` given to `sourceControl/test` is used once for a login attempt (passed via stdin), never stored, returned, or logged. Long-lived authentication relies on the server-side ticket.

### 25A.2 `sourceControl/get`

Params `{}`. Returns the effective binding snapshot from merged global and workspace `SourceControl` configuration (workspace wins). It never runs `p4`.

- `provider`: `none`, `git` (default), or `perforce`. `effectiveProvider` equals it; there is no auto-detection.
- `connectionMode`: Perforce `p4config` or `manual`; null otherwise.
- `status`: derived ([Section 25A.9](#25a9-status-and-error-taxonomy)) — `notConfigured` with no provider, `notTested` for an untested Perforce binding this session, `offline` when `perforce.online` is `false`. Live connectivity comes only from `sourceControl/test`.
- `workspacePath`: absolute path owned by this AppServer.
- `perforce`: non-sensitive connection fields (`port`, `client`, `user`, `charset`, `p4ConfigName`, `p4ExecutablePath`, `timeoutSeconds`, `online`, `autoOffline`); never a password or ticket; null without Perforce config.
- `capabilities`: `perforceChangelist` (only when the effective provider is Perforce and online), `gitCommit` (only for Git), `perforceBinding`, and `perforceShelve`/`perforceSubmit` (always `false`).

### 25A.3 `sourceControl/update`

Params `provider`, optional `connectionMode`, optional `perforce` (omitted fields unchanged; `null` clears the section). Result: the `sourceControl/get` snapshot.

- Writes only non-sensitive values to the workspace configuration's `SourceControl` section, preserving unrelated content and key casing. Any password-equivalent field fails with `-32602`.
- Binding is allowed regardless of test outcome, so offline or VPN scenarios are not blocked; clients may save an unverified binding with `perforce.online = false` and show it as not verified.
- Success emits `config/changed` with `source: "sourceControl/update"` and region `sourceControl`.

### 25A.4 `sourceControl/test`

Params `provider` (must be `perforce`), optional `connectionMode` and `perforce` overrides for this test only, optional transient `password`. Read-only; persists nothing.

The server resolves the client program (`p4ExecutablePath`, else the environment) and checks in order: program usable, server reachable, ticket valid, client exists, and workspace inside the client root and mapped by the client view. The first failing check determines `status` and `code`. Missing SSL trust reports `SSLTrustRequired`; the server never establishes trust for the user. Raw provider stderr is never the primary message and is redacted of credentials.

**Result**: `status`, stable `code`, English `summary` and `fallbackText`, detail groups `identity`, `workspace`, `authentication`, and `diagnostics`, and `warnings`/`errors` lists of `{ code, fallbackText }`. Warnings are non-blocking; non-empty `errors` means the test did not fully succeed.

### 25A.5 Thread Target Shape

Perforce changelist targets are thread-scoped and persisted with the thread: `SourceControlThreadTarget` is `{ provider: "perforce", changelist }`, where `changelist` is `"default"` or a numbered pending changelist id as a string (never a number). Threads default to the `default` changelist when the workspace provider is Perforce. Forks never inherit a target, so separate work streams do not share a pending changelist.

### 25A.6 `sourceControl/threadTarget/get` and `sourceControl/threadTarget/update`

`sourceControl/threadTarget/get` takes `threadId` and returns `{ target }`. `sourceControl/threadTarget/update` takes `threadId` and `target` (`null` clears it, so later reads return the default), persists it, returns `{ target }`, and emits `thread/updated`. A bare target update is a metadata write; changelist existence is validated by operations that need a live server.

### 25A.7 `sourceControl/changelist/list`

Params: optional `threadId`. Result `{ changelists, target }`: a synthesized `default` entry plus the configured user and client's numbered pending changelists (`id`, `isDefault`, `description`, `user`, `client`, `status`). With `threadId`, the thread's effective workspace and persisted target are used; without it (pre-thread surfaces), the foreground workspace and the default target.

Live changelist RPCs require the effective provider to be Perforce and online; offline, the server runs no `p4` and rejects them as unavailable.

### 25A.8 `sourceControl/changelist/create` and `sourceControl/changelist/prepare`

`sourceControl/changelist/create` creates a numbered pending changelist. Params: optional `threadId`, `description` (empty allowed and normalized), `setAsTarget` (default `true`; with a thread it stores the new target and emits `thread/updated`). Without `threadId` the changelist is created on the foreground workspace and the client carries the id as a pre-selection until the first thread starts. Result `{ changelist, target }`.

`sourceControl/changelist/prepare` moves the thread's opened files into a pending changelist; it never submits or shelves. Params: `threadId`, `paths` (workspace-relative, non-empty), optional `description`, optional `target` (omitted uses the stored thread target). Result: `status`, `code`, `changelist`, `created`, `movedPaths`, `skippedPaths`, `warnings`, `errors`.

- Changelist membership is established only here. File tools never open files for edit or add them to a changelist; automatic `p4 edit`/`p4 add` around file writes is left to user-configured `PreToolUse`/`PostToolUse` hooks.
- Target `"default"` creates a numbered changelist, moves the files into it, updates the thread target, and emits `thread/updated`.
- A numbered target must exist; supplied opened files not already in it are moved (including from other pending changelists, since prepare is an explicit user action), then the description is updated when provided. If the move succeeds but the description update fails, the error still reports the changelist and moved paths.
- Files already in the target are left as-is; files not opened are ignored unless Perforce fails reading opened state.
- Stable codes include `Prepared`, `Created`, `NoFiles`, `Timeout`, `LoginRequired`, `ChangelistNotFound`, `FileAlreadyInOtherChangelist`, `P4ExecutableNotFound`, `P4CommandFailed`.

### 25A.9 Status and Error Taxonomy

| Status | Meaning |
|--------|---------|
| `notConfigured` | No provider bound. |
| `notTested` | Provider bound but not validated this session. |
| `connected` | Server, client, user, and workspace mapping all passed. |
| `loginRequired` | Server and client reachable but no valid ticket. |
| `offline` | `perforce.online` is `false`, or auto-offline engaged after the server was unreachable. |
| `error` | Configuration or connectivity error. |

Error and warning codes: `P4ExecutableNotFound`, `P4ExecutableInvalid`, `P4ConfigMissing`, `MissingPort`, `MissingClient`, `MissingUser`, `ServerUnavailable`, `Timeout`, `SSLTrustRequired`, `LoginRequired`, `AuthenticationFailed`, `ClientNotFound`, `ClientHostMismatch`, `ClientRootMismatch`, `WorkspaceNotMapped`, `WorkspaceOutsideClientRoot`, `Unknown`. Codes are stable; servers send `code` plus English `fallbackText`, clients localize and fall back to `fallbackText` for unknown codes.

## 25B. File System Methods

These methods let a client that does not share the AppServer host's file system (a paired phone, or a Desktop extension against a remote workspace) read, inspect, change, and watch host paths. Gated by `capabilities.fileSystem`.

| Method | Params | Result |
|---|---|---|
| `fs/readFile` | `{ path }` | `{ dataBase64 }` |
| `fs/writeFile` | `{ path, dataBase64 }` | `{}`; creates or replaces the file. |
| `fs/createDirectory` | `{ path, recursive? }` | `{}`; `recursive` (default `true`) creates missing parents; an existing directory succeeds. |
| `fs/getMetadata` | `{ path }` | `{ isDirectory, isFile, isSymlink, createdAtMs, modifiedAtMs }`; times are Unix ms, `0` when unreported. `isSymlink` describes `path` itself; the others describe its target. |
| `fs/readDirectory` | `{ path }` | `{ entries }` of direct children `{ fileName, isDirectory, isFile }`. |
| `fs/remove` | `{ path, recursive?, force? }` | `{}`; both default `true`; `force` makes a missing path succeed. |
| `fs/copy` | `{ sourcePath, destinationPath, recursive? }` | `{}`; a directory tree requires `recursive`; an existing destination file is replaced. |
| `fs/watch` | `{ watchId, path }` | `{ path }`: the canonical watched path. |
| `fs/unwatch` | `{ watchId }` | `{}` |

`fs/changed` is a notification `{ watchId, changedPaths }`.

Rules:

- Paths must be absolute host paths (relative → `-32602`). They are not limited to the workspace: the client already has the host user's authority.
- Other failures use `-32103` with `error.data.code` and the resolved path in `error.data.params.path`: `PathBlocked`, `FileNotFound`, `NotAFile`, `NotADirectory`, `DirectoryNotFound`, `DirectoryNotEmpty`, `FileTooLarge`.
- A path under `Security.BlacklistedPaths` fails with `PathBlocked`, checked as given and after resolving links and junctions; methods then act on the resolved path, except `fs/remove`, which removes a link itself and never its target. `fs/copy` checks both paths; a recursive copy skips blocked entries and every link inside the tree; a recursive remove with a blocked path inside fails and removes nothing.
- `fs/readFile` rejects directories (`NotAFile`) and files over 8 MiB (`FileTooLarge`).
- `fs/writeFile` never creates parents (`DirectoryNotFound`); malformed base64 is `-32602`. Requests must fit the transport message limit ([Section 15.6](#156-framing)), so clients keep written files to about 2 MiB.
- `fs/createDirectory` fails with `NotADirectory` when a file exists at `path`, and `DirectoryNotFound` when non-recursive with a missing parent.
- `fs/remove` fails with `FileNotFound` when missing without `force`, and `DirectoryNotEmpty` for a non-empty directory without `recursive`.
- `fs/copy` rejects a directory source without `recursive` and copying a directory into itself or a descendant (`-32602`); a missing destination parent is `DirectoryNotFound`.
- `fs/watch` watches `path` itself (a directory reports changes to direct children). `watchId` is client-chosen and connection-scoped; reusing an active id is `-32602`. A not-yet-existing path is watched through its parent so its creation is reported (`FileNotFound` if the parent is missing too). Changes are coalesced for about 200 ms per notification. Watches end with `fs/unwatch` (unknown ids succeed) or connection close.

## 25C. Command Execution Methods

`command/exec` runs one program on the AppServer host outside any thread and returns its buffered output, for clients that do not share the host (such as Desktop extensions). Gated by `capabilities.commandExec`.

| Method | Params | Result |
|---|---|---|
| `command/exec` | `{ command, cwd?, env?, timeoutMs?, outputBytesCap? }` | `{ exitCode, stdout, stderr }` |

Rules:

- `command` is the argument vector (empty → `-32602`), passed without a shell. `command[0]` is a path (relative paths resolve against `cwd`) or a program name found on the host `PATH` (with `PATHEXT` on Windows). On Windows, `.cmd`/`.bat` files run through the command interpreter with arguments quoted to arrive literally; arguments containing line breaks are rejected for them.
- `cwd` is an absolute host path (relative → `-32602`), defaulting to the workspace root. `env` entries override the server environment; `null` removes a variable.
- The command runs with the host user's authority like the file system methods. It is not an agent tool call: no approval, hooks, or thread record.
- Start failures use `-32104` with `error.data.code` `CommandNotFound`, `CommandNotExecutable`, `DirectoryNotFound`, or `PathBlocked` (`cwd` under `Security.BlacklistedPaths`). A program that starts and fails returns its exit code normally.
- `timeoutMs` defaults to 10000; on expiry the process is terminated and the result has `exitCode` 124 with output so far.
- `stdout` and `stderr` are UTF-8, each capped at `outputBytesCap` (default 1 MiB).
- The result is sent when the process exits, reading output for at most 2 more seconds so descendants holding the streams open do not delay it. Closing the connection terminates a running command.

## 26. Memory Management Methods

A server-authoritative path for destructive workspace memory maintenance. Gated by `capabilities.memoryManagement` (otherwise `-32601`).

### 26.1 `memory/reset`

Params none. Result `{}`. Clears the contents of every memory store of the workspace (the workspace store and every scope store) and the Dreams artifacts under `.craft/dreams`, keeping the directories themselves.

- Sessions, thread history, skills, plugins, automations, Agent Profiles, and configuration are untouched; `Memory.Enabled` and `Memory.AutoExtract` are preserved, so later threads may save memory again.
- Memory-derived welcome suggestion caches are cleared.
- Success emits `config/changed` with `source: "memory/reset"` and `regions: ["memory"]`.

## 27. Dreams Management Methods

Dreams methods expose background consolidation of workspace memory stores and review of the results; the Dreams feature owns the product behavior. Each run consolidates one store into a candidate that replaces the store's files when applied. Model runs are Session-backed internal maintenance threads with one turn (`originChannel = "dreams"`, `dotcraft.internal = "dreams"`), hidden from `thread/list` unless `includeInternal`. Gated by `capabilities.dreams` (otherwise `-32601`).

### 27.1 Run object

Non-obvious fields: `scope` (omitted for the workspace store); `status` (`running`, `succeeded`, `skipped`, `failed`, `canceled`); `trigger` (`manual`, `scheduled`); `candidateThreadCount` (threads in the run manifest); `evidenceThreadIds` and evidence search/read counts; `changes` (`added`, `updated`, `deleted` memory file names and `indexChanged`); `reviewStatus` (`pending`, `applied`, `discarded`, `archived`); `autoApplied`; the run's own `summary`; `errorType` and `message` (`storeChanged` for an apply conflict); the internal `threadId`/`turnId`, filled as soon as known; `usage`; and `inputManifestPath`.

### 27.2 `dreams/status`

Params none. Result: Dreams configuration (`enabled`, `interval`, `autoApply`, `minSessionsSinceLastRun`), `nextRunAt`, `running`, and `lastRun` (`null` if Dreams never ran).

### 27.3 `dreams/run`

Params none. Result: the `dreams/status` shape. Requests runs for every consolidatable store (the workspace store and each Agent Profile store with memory files or eligible threads).

- When enabled and idle, the server persists `running` for the first run, executes runs one at a time in the background, and returns quickly. With a run already active it returns the active status without duplicates. With Dreams or memory disabled it returns the disabled status.
- Manual runs bypass interval and session thresholds but not the store's consolidation lock.
- With `Dreams.AutoApply`, a succeeded run applies itself under the conflict rule of [Section 27.7](#277-review-actions) and records `autoApplied`; already pending runs are unchanged.
- There is no progress or completion notification; clients poll `dreams/status`.

### 27.4 `dreams/create`

Params (all optional): `scope` (must name a consolidatable store; omitted means the workspace store), `threadIds` (narrows the manifest), `instructions`, `model` (override). Result `{ run }`.

### 27.5 `dreams/get`

Params `runId`. Result `{ run, files }`: each changed file with `name`, `change`, and `baseline`/`candidate` text (an added file has no baseline, a deleted file no candidate).

### 27.6 `dreams/list`

Params: optional `includeArchived`. Result `{ runs }`.

### 27.7 Review Actions

`dreams/cancel`, `dreams/apply`, `dreams/discard`, and `dreams/archive` take `{ runId }` and return `{ run }`.

- `dreams/apply` replaces the store's memory files and index with the candidate only while the store still matches the run's baseline. Otherwise it fails with `-32105` (`storeChanged`), the run stays `pending`, and the store is untouched. Success emits a `memory`-region `config/changed`.
- `dreams/discard` marks a pending run discarded without touching the store.
- `dreams/archive` hides a run from default listing without deleting its files.
- `dreams/cancel` is best-effort for running jobs.

## 27A. Usage Telemetry Methods

### 27A.1 Scope

These methods expose workspace usage analytics (overviews, history charts, tool activity, top threads). Gated by `capabilities.usageTelemetry`, which is `false` when tracing is disabled (the usage fact table and trace store are not written); methods then return `-32601`.

Usage facts are recorded once per completed Turn: timestamp, origin channel, root thread, model, reasoning effort, LLM call count, and token breakdown. Tool calls, skill references, and errors come from persisted trace events. Every aggregate buckets a fact by its own timestamp.

#### Date ranges

`usage/summary`, `usage/history`, and `usage/threads` share `from` and `to` (inclusive `YYYY-MM-DD` in the client's local frame; omitted means unbounded) and `tzOffsetMinutes` (minutes added to UTC for local time, default 0, clamped to ±840). A malformed date returns `-32602`; `from` after `to` matches nothing.

#### Surface attribution

The `surface` dimension is the origin channel of the **root** thread: SubAgent facts roll up under the thread that spawned them. Facts whose root thread no longer exists use their own origin channel.

### 27A.2 `usage/summary`

Params: the date-range fields (`{}` is lifetime). Result: `sessionCount` (distinct root threads with a completed Turn in range), `totalRequests` (LLM calls over completed Turns), `totalResponses` (response trace events), `totalToolCalls`, `totalErrors` (error and provider-error events), `totalContextCompactions`, token totals (`totalInputTokens`, `totalOutputTokens`, `totalCachedInputTokens`, `totalCacheWriteInputTokens`, `totalFreshInputTokens` = `max(0, input − cached − cacheWrite)`, `totalNonCachedInputTokens` = `max(0, input − cached)`, `totalReasoningOutputTokens`, `totalTokens` = input + output), tool duration total/average/max, and `cacheHitRate` (`cached / input`, `0` without input). Every numeric field is `0` when nothing matches. Period comparison is a client concern.

### 27A.3 `usage/history`

One metric per local day, optionally broken down by one dimension, in a single shape for all combinations.

**Params**: `metric` (required), `groupBy` (default `none`), the date-range fields, and `topLimit` (default 10, clamped 1-20; ignored for `none` and `tokenType`).

| Metric | Source | Value | Dimensions |
|--------|--------|-------|------------|
| `tokens` | completed Turns | input + output tokens | `none`, `tokenType`, `surface`, `model`, `reasoning`, `speed` |
| `turns` | completed Turns | 1 per Turn | `none`, `surface`, `model`, `reasoning`, `speed` |
| `requests` | completed Turns | LLM calls | `none`, `surface`, `model`, `reasoning`, `speed` |
| `toolCalls` | tool-call-completed trace events | 1 per call | `none`, `toolSource`, `tool` |
| `skillUses` | skill-referenced trace events | 1 per reference | `none`, `skill` |
| `errors` | error and provider-error trace events | 1 per event | `none`, `surface` |

Other combinations or unknown values return `-32602`.

**Result**: `unit` (`tokens` for the tokens metric, else `count`), effective `groupBy`, `days` (days with data, ascending; clients fill gaps), each with local `date`, `total` across all groups (the share denominator), and `values` (`{ key, value }` descending; empty for `none`), plus `series` (every group's range total, descending; empty for `none`).

Group keys are raw; clients map labels:

- `tokenType`: `uncached`, `cached`, `cacheWrite`, `output` (never folded).
- `surface`: the root thread's origin channel.
- `model`, `reasoning` (`low`, `medium`, `high`, `extrahigh`), `speed` (`standard`, `fast`): as recorded on the Turn; `unknown` when absent (or reasoning off).
- `toolSource`: `builtin`, `mcp:<server>`, `plugin:<pluginId>`, `client`, `binding`, or `unknown`.
- `tool`: tool name as invoked. `skill`: skill name without `$`.

Groups are ranked by range total; the leading `topLimit` keep their keys on every day and the rest fold into `other`, so a group's key never changes between days.

### 27A.4 `usage/threads`

Params: the date-range fields and `limit` (default 5, clamped 1-50). A thread is active in range when its most recent completed Turn falls inside it.

Result `{ threads }`: root threads (SubAgents rolled up) ordered by lifetime `totalTokens`, then `lastActiveAt`, each with `threadId`, `title` (display name, else first user message; omitted when neither exists), `originChannel`, `lastActiveAt`, lifetime `turns` and token totals, `cacheHitRate`, and `archived`.

### 27A.5 `usage/thread`

Params `threadId` (root thread, required; blank → `-32602`). Result: lifetime `turns`, token totals, and `cacheHitRate` across the thread and its SubAgents, plus `groups`: one entry per distinct model × reasoning effort × speed (dimensions omitted when the Turns carry none), ordered by `totalTokens`; empty without completed Turns.

### 27A.6 `profile/insights`

Workspace activity insights. Params: `topSkills` (default 5, clamped 1-20).

**Result**: `topModel` and `topReasoning` (`{ key, count, total }`, where `count / total` is the share; omitted when none recorded), `skillsExplored` (distinct skills referenced), `totalSkillsUsed`, `totalThreads` (non-internal threads, active and archived), `longestTaskMs` (longest single Turn), and `skills` (most-referenced, by count then name, with `pluginId` for plugin skills).

Models are keyed by model id only (provider not distinguished), counted from response trace events. A skill use is counted only when the agent loads it through `SkillView`; skills injected otherwise (for example `always: true`) are not counted.

## 28. Protocol Ownership

This protocol is the authoritative wire contract for DotCraft clients and adapters: its methods, notifications, item types, capability flags, transport behaviors, and extension surfaces are defined here. `DotCraft.Protocol` contains the executable DTOs and typed RPC catalog for it; generated artifacts, SDK models, and client projections are not independent authorities. Dynamic third-party extension methods stay on the raw JSON-RPC path.
