# Remote Tool Host

| Field | Value |
|---|---|
| Version | 0.4.0 |
| Status | Draft |
| Date | 2026-09-06 |
| Parent | [Tool Architecture](tools-architecture.md) |
| Related Specs | [Hub Architecture](hub-architecture.md), [Satellite](../clients/satellite.md), [Runtime Module Boundaries](runtime-module-boundaries.md), [Prompt Cache](prompt-cache.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

## 1. Purpose

This specification defines Remote Tool Host, a provider-free DotCraft process that executes an
explicit subset of native tools for an Agent Host on another machine of the same intranet. The
Agent Host remains the owner of the model loop, Session Core, Tool Call and Tool Result items,
common hooks, and user interaction. Remote Tool Host owns the remote machine's workspace runtime,
local policy, native tool execution, and execution audit.

Remote Tool Host never listens for inbound connections. It dials out to the Hub on the Agent
machine, and that Hub relays each execution session to the Agent Host as an opaque byte stream.
The Hub is therefore the rendezvous point; it is not a runtime, not a proxy that understands tool
traffic, and not a lease owner.

Remote Tool Host is the execution and resource owner. Remote Tool Host Client is the component in
an Agent Host that connects to it. Hub records a paired Remote Tool Host as a **satellite peer**.
The Windows tray client that installs, pairs, and supervises a Remote Tool Host for a
non-technical machine owner is specified in [Satellite](../clients/satellite.md); the product name
of that client is Satellite, while this specification keeps the technical terms.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are normative.

## 2. Boundaries and non-goals

Remote Tool Host MUST preserve the Tool Architecture definition/binding split:

- a remotely executed tool retains its existing `ToolDefinitionId`, canonical `ToolName`, schemas,
  source provenance, exposure, and Session projection;
- remote execution replaces only the live runtime route behind the stable registration;
- a remote route is not a `ToolSourceKind`, and the Agent MUST NOT import a second remote namespace;
- ordinary CLR calls remain local; only calls dispatched through the DotCraft tool pipeline are
  eligible for remote routing;
- a remote failure MUST NOT silently fall back to the local binding.

Remote Tool Host does not listen for inbound connections, so the remote machine needs no inbound
firewall rule, port forward, reverse proxy, or TLS identity. It dials out to exactly one Hub per
pairing. The Hub is a byte relay for those connections: it MUST NOT parse, rewrite, inspect, log,
or persist MCP traffic, and it MUST NOT hold a workspace lease.

Remote Tool Host does not provide general .NET remoting, IL weaving, arbitrary method interception,
gRPC, NAT traversal, a cloud relay, LAN discovery, OAuth, enterprise SSO, multi-user roles, or a
LocalSystem/root service. Each pairing is one principal; there are no roles or permissions beyond
Host-local tool policy. It does not proxy MCP, Runtime Dynamic, Legacy App Binding, Session,
Agent-control, planning, goal, or user-interaction tools.

## 3. RPC eligibility

`ToolRpcAttribute` is a parameterless method marker used alongside `ToolAttribute` or
`GeneratedToolAttribute`. It means that a Core Native tool MAY be exported by a Remote Tool Host
and that an Agent Host MAY route the tool's runtime binding remotely. It grants no authority,
changes no approval policy, and has no effect on direct method calls.

The generated tool declaration and generated catalog descriptor MUST carry an `RpcEligible`
boolean derived from the attribute. Reflection-based discovery MUST produce the same value. A
method carrying `ToolRpcAttribute` without either supported tool attribute is invalid and SHOULD
produce a generator diagnostic.

The initial Core RPC-eligible set is:

- `ReadFile`, `WriteFile`, `EditFile`, `GrepFiles`, and `FindFiles`;
- `LSP`;
- `Exec` and `WriteStdin`.

`WebSearch` and `WebFetch` remain Agent-local because connecting a workspace MUST NOT implicitly
change network egress, DNS, proxy, or search-provider configuration. Remote network access is a
separate capability and is not part of this profile.

## 4. Contract identity and catalog negotiation

Remote Tool Host profile version `1` defines a contract hash over canonical UTF-8 data containing:

1. `ToolDefinitionId`;
2. canonical tool name;
3. model-facing description;
4. canonical input and optional output JSON Schemas;
5. stable RPC annotations; and
6. the Remote Tool Host profile version.

The hash excludes runtime-binding identity, presentation metadata, workspace paths, local policy,
instance provenance, and connection state. Object properties are ordinally sorted recursively and
numbers use their canonical JSON representation before SHA-256 is calculated.

The Host exports only RPC-eligible Core Native registrations. Every exported MCP tool carries the
following metadata:

```json
{
  "_meta": {
    "dotcraft/remoteTool": {
      "profileVersion": "1",
      "definitionId": "CoreNative:core-native:ReadFile",
      "contractHash": "sha256-base64url",
      "catalogRevision": "opaque-revision"
    }
  }
}
```

`tools/list` is lease-scoped. The Host MUST resolve the exported catalog from the caller's active
lease and MUST NOT fall back to an arbitrary registered workspace; a `tools/list` without lease
metadata fails with `ProtocolMismatch`. The workspace list result additionally carries the Host
build version, a catalog digest over the sorted `definitionId:contractHash` pairs, and a per-tool
contract summary, so the Agent can explain a mismatch before any call is made.

The Agent compares the remote descriptor with its existing local registration. Host-only tools are
ignored. A missing or mismatched remote descriptor makes that local tool unavailable while the
thread is remotely routed; it does not cause local execution. Other matching RPC-eligible tools
remain usable. The connect result names each unavailable tool with a stable reason code
(`RemoteToolUnavailable` when the Host does not export it, `ToolContractMismatch` when the hash
differs) and a short human-readable detail that includes both build versions when they differ.

The Host MUST revalidate definition identity and contract hash for every call. Catalog revisions
are cache invalidation hints rather than authority.

## 5. Host, workspace, peer, and lease model

Each installed Host has one durable, opaque `hostId` and zero or more Host-local workspace records:

```text
(hostId, workspaceId) -> canonical absolute directory
```

`workspaceId` is unique within one Host. Only a local Host administrator may create, remove, or
retarget a workspace. The Agent cannot submit a new root or reinterpret a workspace identifier.

A Host also has zero or more **pairings**. A pairing binds the Host to one Hub and is recorded on
the Host as a peer record containing the Hub endpoint, an opaque `peerId` assigned by that Hub, a
credential reference, and the `workspaceId` the pairing was created for. That workspace reference
is a label for local surfaces; workspace records and Host policy remain the only access boundary.
The Hub records the same pairing as a satellite peer under the same `peerId` together with the
Host display name, machine information, build version, last reported workspaces, and last-seen
time. Agent Hosts on the Hub's machine address the Remote Tool Host by that `peerId`; it is the
`hostId` value they see in every catalog, route, and client surface. The Host-local `hostId` never
leaves the Host machine.

Each thread has at most one runtime-only `RemoteToolRoute` containing `hostId`, `workspaceId`, and a
live lease reference. The route is omitted from persisted Session configuration and cold resume
starts disconnected.

A workspace lease is exclusive between Agent Host processes. Threads within one Agent Host process
MAY share the same lease, including Native SubAgents that inherit the parent's route at child
creation. Parent and child routes are independent after creation. A second Agent Host receives
`WorkspaceBusy` immediately; there is no queue and no force option. Workspace descriptors report a
lease as `self` when the requesting Agent Host owns it and `other` otherwise, together with the
lease expiry; the owner identity itself is never disclosed.

One Agent Host uses one stateful MCP session per Remote Tool Host. That session is carried by
exactly one Hub-brokered data connection (§8). Losing the data connection ends the MCP session but
does not release the lease; the lease expires through its normal heartbeat TTL and is then
reclaimed by the Host. The client sends a heartbeat at least every 15 seconds and the Host expires a
lease after 60 seconds without a heartbeat. Expiry or release cancels and drains lease-owned
foreground calls and terminates lease-owned background terminals and LSP processes before another
Agent Host can acquire the workspace. Lease-owned result artifacts are deleted when the final
reference is released or the lease expires. Stale artifact directories are removed when the Host
starts because leases are not durable across a Host restart.

Switching routes acquires the new lease before publishing the new route and releasing the old one.
Failure to acquire leaves the old route unchanged. Connecting to the current route is idempotent.

## 6. Model control surface

When the Remote Tool Host client capability is installed, the Agent Host always exposes three
ordinary, profile-managed, directly loaded Core tools with these canonical names:

```text
RemoteToolHost.List()
RemoteToolHost.Connect(hostId, workspaceId)
RemoteToolHost.Disconnect()
```

Provider-flat projections MAY translate the dot but the canonical identity remains namespaced.
These tools never accept or return an endpoint, token, certificate, credential reference, or raw
Host configuration.

Their static descriptions are:

```text
Namespace: Manage this thread's remote workspace connection.
List: List registered Remote Tool Hosts, their workspaces, and this thread's current connection.
Connect: Connect this thread to a remote workspace.
Disconnect: Disconnect this thread from its remote workspace.
Connect.hostId: Remote Tool Host id.
Connect.workspaceId: Remote workspace id.
```

The namespace, tool descriptions, schemas, ordering, and exposure are static. Peer registrations,
online catalogs, thread routes, and lease state MUST NOT change any declaration or deferred-search
metadata. An empty peer catalog therefore still exposes all three tools: `List` returns an empty
catalog, `Connect` returns `HostNotRegistered`, and `Disconnect` reports that no route was removed.
Profile policy MAY continue to filter the tools as it filters other Core tools.

`List` returns the safe peer catalog from the Hub, online state, available workspaces with their
lease state, and the current thread route. It does not open a data connection. `Connect` acquires
the workspace over a Hub-brokered data connection, negotiates the remote tool catalog, and
immediately publishes the route for later calls in the same Turn. Its result includes a non-secret
execution summary: hostname, OS, user, canonical workspace path, Host instance id, matched tool
names, and unavailable tool names with their reasons. `Disconnect` removes only the current thread
route and returns it to local execution. `List` is the authoritative discovery surface and reads
the current Hub catalog when it is called, so out-of-band pairing changes do not require a
tool-snapshot rebuild.

The model control surface and the client control surface defined by the AppServer Protocol
(`remoteToolHost/*`) are two entries to the same client. A client-driven route change is not a Turn
and is not tool use; it is subject to the same lease, catalog, and safety rules as the model tools.

At the start of each Turn, the Agent Host appends a Remote Tool Host section to the latest user
message runtime context when the thread has a connected or lost route. The section contains only
`Status`, `HostId`, `WorkspaceId`, `HostName`, `OperatingSystem`, `UserName`, and
`RemoteWorkingDirectory`. Values are bounded and encoded as single-line scalars. The section MUST
omit lease and Host instance identifiers, endpoints, tokens, certificates, and credential
references. A disconnected thread has no Remote Tool Host runtime-context section. A successful
`Connect` or `Disconnect` result is authoritative for the remainder of its current Turn; the next
Turn's runtime context reflects the new state.

The control namespace remains directly loaded while it contains this small discovery and
connection surface. A future deferred projection MUST treat the whole namespace as one stable
capability, MUST NOT vary with registrations or connection state, and MUST fall back to direct
exposure when provider-native tool search is unavailable.

## 7. Invocation flow and policy

The Agent Host performs the common source-neutral Tool Dispatcher pipeline through authority,
argument validation, thread policy, pre-use hooks, and Agent-side approval before entering the
route-aware runtime binding. On a remote route it then sends an MCP `tools/call` carrying:

```json
{
  "_meta": {
    "dotcraft/remoteToolCall": {
      "leaseId": "opaque",
      "workspaceId": "workspace-id",
      "invocationId": "globally-unique",
      "definitionId": "source-qualified-id",
      "contractHash": "sha256-base64url",
      "maxResultChars": 50000,
      "spillPreviewLines": 40
    }
  }
}
```

The Host validates the MCP session, lease, workspace, RPC eligibility, definition, contract hash,
and arguments. It then applies Host-local policy against the real remote environment and invokes
the native runtime. The workspace runtime is composed with the Host's effective configuration
(§10) and the configured path blacklist; a Host MUST NOT execute with a weaker file boundary than a
local Agent applies to the same directory.

Before encoding a text result, the Host materializes oversized text under the Host's private state
at `~/.craft/remote-tool-host/artifacts/<leaseId>/tool-results/<thread>/<tool>_<invocation>.txt`.
The artifact root MUST be a trusted read path of every leased workspace runtime so remote
`ReadFile` can reach it, lies outside every leased workspace so tool writes into it follow the
ordinary out-of-workspace rules, and MUST resolve inside the Host state directory after symlink
and reparse-point resolution. A Host whose artifact root is
blacklisted MUST fail at startup rather than at call time. The requested limit is clamped to the
profile hard ceiling of 100,000 characters; zero cannot disable this transport ceiling. The Host
returns only a bounded preview and the artifact path. A materialization failure fails the call and
MUST NOT fall back to transmitting the complete result.

The result `_meta.dotcraft.remoteArtifact` object contains `path` and `characterCount` when a text
result was materialized. The Agent Host preserves this safe provenance and does not spill that
preview into its local workspace. The Host does not create Session items or run the Agent Host's
common hooks. The Agent Host performs final result validation and normalization, terminalizes the
original Session projection, and runs terminal hooks exactly once.

Host policy returns `allow`, `deny`, or `needsApproval`. A denial cannot be overridden remotely.
For `needsApproval`, the Host sends standard MCP form elicitation inside the active call. The Agent
Host binds the elicitation to the invocation's Turn and creates the ordinary approval interaction.
Acceptance authorizes only the current invocation; persistent policy can be changed only through
Host-local administration. A disconnected or non-interactive Agent declines safely.

Remote `Exec` requires approval for every new command by default because working-directory
validation cannot constrain arbitrary shell access. The Host MUST record the background terminal
session ids created by each approved `Exec` against the acquiring lease and MUST reject `WriteStdin`
for any other session id with `RemotePolicyDenied`. This binding is unconditional and is not
weakened by a Host-local `allow` policy. File and LSP policy canonicalizes paths and resolves
symlinks, junctions, and reparse points before applying workspace rules.

## 8. Transport profile and failure semantics

The data plane is a stateful MCP session over a newline-delimited JSON byte stream. Profile v1
carries that stream on a WebSocket pair brokered by the Hub on the Agent machine: the Agent Host
opens a loopback WebSocket to its Hub, the Remote Tool Host opens an outbound WebSocket to the same
Hub, and the Hub relays frames between them without interpretation. The Host is the MCP server and
the Agent Host is the MCP client even though the Host initiated the connection. Profile v1 uses
bearer authentication over WebSocket. The scheme follows the invitation URL: an `http` invitation
pairs and connects over `ws://`, an `https` invitation over `wss://`, and the Host persists the
scheme with the Hub host and port in the peer record so every later control and data connection
uses the same one. The Hub's own satellite listener speaks plain HTTP on a trusted intranet; an
`https` invitation only arises when a TLS reverse proxy fronts that listener, and the Host never
downgrades such an invitation to plain WebSocket.

The MCP session uses standard initialization, `tools/list`, `tools/call`, cancellation, progress,
content/result, and elicitation contracts. The DotCraft profile adds these JSON-RPC methods over the
established MCP session:

```text
dotcraft/remoteToolHost/workspaces/list
dotcraft/remoteToolHost/workspaces/acquire
dotcraft/remoteToolHost/workspaces/release
dotcraft/remoteToolHost/workspaces/heartbeat
```

The list result includes `hostId`, `hostInstanceId`, `catalogRevision`, `buildVersion`,
`catalogDigest`, per-tool contract summaries, and safe workspace descriptors with lease state.
Acquire accepts `workspaceId` and returns `leaseId`, expiry, and environment summary. Release
accepts one `leaseId`. Heartbeat is a notification carrying the session's active lease ids. Unknown
fields follow MCP extension behavior; missing required profile fields fail closed.

### 8.1 Control channel

For every pairing the Host keeps one outbound control WebSocket to the Hub. On connect it presents
either a one-time invite id (first connection, §9) or its peer credential, then sends `hello` with
its display name, machine name, operating system, user, build version, and current workspace
descriptors. Afterwards it sends `heartbeat` every 15 seconds carrying the current workspace
descriptors including lease state; the Hub marks a peer offline after 45 seconds without a
heartbeat. The Hub sends `openSession` with a session id when an Agent Host wants a data
connection, and `revoked` when the pairing is removed. The Host answers `openSession` by opening a
data connection or by sending `sessionFailed` with a stable code.

A lost control connection is retried with bounded exponential backoff starting at one second and
capped at 120 seconds, with jitter, and the backoff resets after a successful `hello`. Reconnecting
never requires user action on either machine.

### 8.2 Data sessions

An Agent Host asks its Hub for a session to a peer. The Hub sends `openSession` over the control
channel and waits up to 15 seconds for the Host to open a data connection carrying that session id
and the peer credential. The Hub then relays the two WebSockets frame for frame, preserving message
type and fragment boundaries, until either side closes. One data connection is one MCP session.
Multiple Agent Host processes on the same Hub machine use separate data connections and therefore
separate sessions; lease exclusivity between them is unchanged.

Calls are never automatically retried after transmission. If the client cannot establish whether a
call executed, it returns `RemoteOutcomeUnknown` with the invocation id. Client cancellation is
forwarded through MCP and remains best effort at the transport boundary, but the Host MUST drain
owned foreground resources before reporting terminal cancellation.

### 8.3 Error codes

Stable Remote Tool Host error codes are:

```text
HostNotRegistered       HostOffline              AuthenticationFailed
ProtocolMismatch        WorkspaceNotFound        WorkspaceBusy
LeaseLost               ToolContractMismatch     RemoteToolUnavailable
RemotePolicyDenied      ApprovalDeclined         RemoteOutcomeUnknown
RemoteResultMaterializationFailed
SatelliteOffline        SatelliteSessionFailed   InviteInvalid
HubUnavailable
```

Errors include a stable code, safe English fallback, retryability, and structured safe details.
`HubUnavailable` means the Agent Host could not reach its own Hub; `SatelliteOffline` means the Hub
has no live control connection for the peer; `SatelliteSessionFailed` means the peer declined or
failed to open the requested data connection.

## 9. Pairing, authentication, and local state

Pairing is initiated by the Agent side. `invite` asks the Hub to mint an invite: an opaque,
single-use invite id with a default validity of 24 hours, an optional display label, an optional
short purpose, and the invite URL served by the Hub's satellite listener. An invitation never names
a folder: which folder is shared is the invited machine owner's decision, made on that machine. The
invite id is a bearer secret while it is valid; the Hub stores only its hash, so invites survive a
Hub restart without a plaintext copy on disk, and the Hub MUST NOT write invite ids to request logs.
The label and purpose are stored beside that hash and are what the invited machine is shown before
it decides.

The invite URL is content-negotiated by the Hub, so the same link serves a person opening it in a
browser, a client asking for the invitation's details, and the CLI. A client MUST be able to read
label, purpose, and expiry with a single `GET` of the invite URL that neither consumes the
invitation nor writes any state on either machine; only the control-channel handshake below
consumes it. The variants are specified by [Hub Architecture](hub-architecture.md) §6.1.

`join` accepts the invite URL, connects the control channel with the invite id, and receives the
durable pairing: a `peerId` and a 256-bit random peer credential. The Host stores the raw credential
in the operating-system credential store and writes only the Hub endpoint, `peerId`, and a
credential reference to its configuration. The Hub stores only the credential hash. Every later
control or data connection presents the peer credential as a bearer credential. There is exactly
one shared secret per pairing, which keeps the security level of the previous inbound profile.

Bearer credentials appear only in the WebSocket handshake `Authorization` header and MUST NOT enter
URLs, command arguments, model context, Session persistence, trace output, or logs. Agent Hosts on
the Hub machine authenticate to the Hub with the existing loopback Hub token and hold no Remote
Tool Host credential.

`revoke` on either side ends a pairing. On the Agent side it deletes the Hub's peer record and
closes live connections; the Host receives `revoked`, deletes its peer record and credential, and
stops reconnecting. On the Host side it deletes the local peer record and credential and closes the
control connection; the Hub then observes the peer as offline until the Agent side also revokes it.

Host state lives under `~/.craft/remote-tool-host/`: `host.json` (identity, display name,
workspaces, tool policies, peer records, catalog revision), `serve.lock`, `artifacts/`,
`workspaces/<workspaceId>/`, and `audit/<date>.jsonl`. Hub state for satellite peers lives under
`~/.craft/hub/satellites.json` and is specified by [Hub Architecture](hub-architecture.md).

The v1 deployment profile assumes direct intranet reachability of the Hub's satellite listener from
the Host machine. Peer credential possession has the permissions of the signed-in Host user and MUST
be treated accordingly.

## 10. Host lifecycle and CLI

The official application exposes:

```text
dotcraft tool-host setup [--name <display-name>]
dotcraft tool-host workspace add <workspace-id> <absolute-path>
dotcraft tool-host workspace list [--json]
dotcraft tool-host workspace remove <workspace-id>
dotcraft tool-host policy list [--json]
dotcraft tool-host policy set <tool-name> <allow|deny|needs-approval>
dotcraft tool-host autostart install|remove
dotcraft tool-host status [--json]
dotcraft tool-host serve
dotcraft tool-host join <invite-url> [--workspace <absolute-path>]
dotcraft tool-host revoke <id>
dotcraft tool-host invite [--name <label>] [--host <address>] [--expires <hours>] [--json]
dotcraft tool-host list [--json]
dotcraft tool-host test <id>
```

The CLI is non-interactive. `setup` creates only the Host identity and display name and takes no
endpoint. `join` accepts the invite URL, or the equivalent `dotcraft://satellite/join` link, and
performs the pairing; it registers the folder given by `--workspace`, which is required whenever the CLI pairs on its
own, because the invitation never names a folder, and is ignored when a running Satellite takes the
invitation instead.
When a Satellite client is running on the machine, `join` forwards the invite to it instead of
pairing directly, so the machine owner sees the consent window. `revoke` accepts a `peerId` and
resolves from local state whether it acts as the Host or as the Agent side. `invite` and `list` run
on the Agent machine against its Hub.

`serve` takes no flags; stored pairings decide behavior. It holds `serve.lock` so a second `serve`
on the same machine exits without starting, loads `~/.craft/config.json` for Host-level settings,
merges each leased workspace's `.craft/config.json` over it for that workspace's runtime, and
composes a provider-free workspace execution runtime with one control channel per pairing. It does
not compose a model provider, Session Core, AppServer, memory, or Agent orchestration. A Host with
no pairing refuses to start and points at `join`.

User-level login autostart is the v1 lifecycle; Windows user autostart is the initial conformance
target. A machine runs at most one Remote Tool Host process: when the Satellite client owns
autostart, `autostart install` MUST refuse, and installing Satellite autostart MUST remove the CLI
autostart entry. The same public runtime hosting entry point serves both the CLI verbs and the
Satellite client so their lifecycle semantics cannot diverge.

## 11. Observability and conformance

The original Tool Call and Tool Result remain authoritative. Safe invocation provenance records
`executionTarget=remote`, `hostId`, `workspaceId`, `hostInstanceId`, `remoteInvocationId`, and remote
latency. Credentials and authorization headers are always redacted. The Host audit records MCP
session, workspace, tool, result code, duration, and cancellation without recording credentials.

Conformance tests cover:

- generated and reflection RPC eligibility, Core export, and ineligible-source rejection;
- contract hashing, missing/mismatched catalogs, per-call revalidation, and unavailable reasons
  that include both build versions;
- local, same-Turn connect, remote, disconnect, and no-fallback execution;
- Native SubAgent inheritance and independent routes over a shared process lease;
- same-client sharing, cross-client `WorkspaceBusy` with `self`/`other` owner markers, heartbeat
  expiry, and process failure;
- lease-scoped `tools/list` and fail-closed behavior without lease metadata;
- canonical path, symlink/reparse-point, and blacklist policy;
- allow, deny, elicitation acceptance, decline, and cancellation delivered across the Hub bridge;
- `WriteStdin` bound to terminals created by an approved `Exec` in the same lease, regardless of
  Host policy;
- background terminal and LSP cleanup at lease release;
- MCP text, image, audio, structured content, progress, cancellation, and errors;
- remote text materialization under the Host state root, remote `ReadFile` access to it without
  approval, and lease-scoped artifact cleanup;
- invite issue, single-use consumption, expiry, join, and revoke from both sides;
- control-channel reconnect with backoff, and offline/online transitions observed by the Hub;
- byte-identical relay through the Hub bridge for fragmented messages;
- configuration loading, the serve lock, and the CLI/Satellite autostart exclusion;
- a pure Host dependency graph with no model, Session, memory, or AppServer services; and
- an in-process Hub + Host + Agent bridge execution flow and a two-process outbound end-to-end flow.

When no Remote Tool Host is paired, the existing model tool schema and local execution behavior
remain unchanged.
