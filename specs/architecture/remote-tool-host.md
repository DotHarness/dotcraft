# Remote Tool Host

| Field | Value |
|---|---|
| Version | 0.1.0 |
| Status | Draft |
| Date | 2026-09-01 |
| Parent | [Tool Architecture](tools-architecture.md) |
| Related Specs | [Runtime Module Boundaries](runtime-module-boundaries.md), [AppServer Protocol](../protocols/appserver-protocol.md) |

## 1. Purpose

This specification defines Remote Tool Host, a provider-free DotCraft process that executes an
explicit subset of native tools for an Agent Host over an authenticated intranet connection. The
Agent Host remains the owner of the model loop, Session Core, Tool Call and Tool Result items,
common hooks, and user interaction. Remote Tool Host owns the remote machine's workspace runtime,
local policy, native tool execution, and execution audit.

Remote Tool Host is the execution and resource owner. Remote Tool Host Client is the component in
an Agent Host that connects to it. The feature is not called Remote Tool Client mode or RPC mode.

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

Remote Tool Host does not provide general .NET remoting, IL weaving, arbitrary method interception,
gRPC, NAT traversal, a cloud relay, LAN discovery, OAuth, enterprise SSO, multi-user ACLs, or a
LocalSystem/root service. It does not proxy MCP, Runtime Dynamic, Legacy App Binding, Session,
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

The Agent compares the remote descriptor with its existing local registration. Host-only tools are
ignored. A missing or mismatched remote descriptor makes that local tool unavailable while the
thread is remotely routed; it does not cause local execution. Other matching RPC-eligible tools
remain usable.

The Host MUST revalidate definition identity and contract hash for every call. Catalog revisions
are cache invalidation hints rather than authority.

## 5. Host and workspace model

Each installed Host has one durable, opaque `hostId` and zero or more Host-local workspace records:

```text
(hostId, workspaceId) -> canonical absolute directory
```

`workspaceId` is unique within one Host. Only a local Host administrator may create, remove, or
retarget a workspace. The Agent cannot submit a new root or reinterpret a workspace identifier.
Each thread has at most one runtime-only `RemoteToolRoute` containing `hostId`, `workspaceId`, and a
live lease reference. The route is omitted from persisted Session configuration and cold resume
starts disconnected.

A workspace lease is exclusive between Agent Host processes. Threads within one Agent Host process
MAY share the same lease, including Native SubAgents that inherit the parent's route at child
creation. Parent and child routes are independent after creation. A second Agent Host receives
`WorkspaceBusy` immediately; there is no queue and no force option.

One Agent Host reuses one stateful MCP session per Remote Tool Host. Local route references retain a
workspace lease. Releasing the final reference releases the lease. The client sends a heartbeat at
least every 15 seconds and the Host expires a lease after 60 seconds without a heartbeat. Expiry or
release cancels and drains lease-owned foreground calls and terminates lease-owned background
terminals and LSP processes before another Agent Host can acquire the workspace. Lease-owned result
artifacts are deleted when the final reference is released or the lease expires. Stale lease
artifact directories are removed when the Host starts because leases are not durable across a Host
restart.

Switching routes acquires the new lease before publishing the new route and releasing the old one.
Failure to acquire leaves the old route unchanged. Connecting to the current route is idempotent.

## 6. Model control surface

The Agent Host exposes three ordinary, profile-managed Core tools with these canonical names:

```text
RemoteToolHost.List()
RemoteToolHost.Connect(hostId, workspaceId)
RemoteToolHost.Disconnect()
```

Provider-flat projections MAY translate the dot but the canonical identity remains namespaced.
These tools never accept or return an endpoint, token, certificate, credential reference, or raw
Host configuration.

`List` returns the safe registered Host catalog, online state, available workspaces, and the current
thread route. `Connect` uses only a registered Host credential, verifies the pinned certificate and
profile, acquires the workspace, negotiates the remote tool catalog, and immediately publishes the
route for later calls in the same Turn. Its result includes a non-secret execution summary:
hostname, OS, user, canonical workspace path, Host instance id, and matched/unavailable tool names.
`Disconnect` removes only the current thread route and returns it to local execution.

The Connect declaration description MAY include a bounded, non-secret Host registry snapshot for
planning. `List` is authoritative. Out-of-band registry changes become model-visible no later than
the next Turn and MUST NOT change the Connect input schema.

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

The Host validates authentication, MCP session, lease, workspace, RPC eligibility, definition,
contract hash, and arguments. It then applies Host-local policy against the real remote environment
and invokes the native runtime. Before encoding a text result, the Host materializes oversized text
under the remote workspace at
`.craft/remote-tool-host/artifacts/<leaseId>/tool-results/<thread>/<tool>_<invocation>.txt`. The requested
limit is clamped to the profile hard ceiling of 100,000 characters; zero cannot disable this
transport ceiling. The Host returns only a bounded preview and a workspace-relative path. The
artifact root MUST resolve inside the registered workspace after symlink and reparse-point
resolution. A materialization failure fails the call and MUST NOT fall back to transmitting the
complete result.

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
validation cannot constrain arbitrary shell access. `WriteStdin` is authorized only for a terminal
created by an approved `Exec` in the same workspace lease. File and LSP policy canonicalizes paths
and resolves symlinks, junctions, and reparse points before applying workspace rules.

## 8. MCP profile and failure semantics

The data plane is stateful MCP Streamable HTTP over HTTPS. It uses standard initialization,
`tools/list`, `tools/call`, cancellation, progress, content/result, and elicitation contracts. The
DotCraft profile adds these JSON-RPC methods over the established MCP session:

```text
dotcraft/remoteToolHost/workspaces/list
dotcraft/remoteToolHost/workspaces/acquire
dotcraft/remoteToolHost/workspaces/release
dotcraft/remoteToolHost/workspaces/heartbeat
```

The list result includes `hostId`, `hostInstanceId`, `catalogRevision`, and safe workspace
descriptors. Acquire accepts `workspaceId` and returns `leaseId`, expiry, and environment summary.
Release accepts one `leaseId`. Heartbeat is a notification carrying the session's active lease ids.
Unknown fields follow MCP extension behavior; missing required profile fields fail closed.

Calls are never automatically retried after transmission. If the client cannot establish whether a
call executed, it returns `RemoteOutcomeUnknown` with the invocation id. Client cancellation is
forwarded through MCP and remains best effort at the transport boundary, but the Host MUST drain
owned foreground resources before reporting terminal cancellation.

Stable Remote Tool Host error codes are:

```text
HostNotRegistered       HostOffline              AuthenticationFailed
CertificateMismatch     ProtocolMismatch         WorkspaceNotFound
WorkspaceBusy           LeaseLost                ToolContractMismatch
RemoteToolUnavailable   RemotePolicyDenied       ApprovalDeclined
RemoteOutcomeUnknown    RemoteResultMaterializationFailed
```

Errors include a stable code, safe English fallback, retryability, and structured safe details.

## 9. Pairing, authentication, and local state

Setup creates a 256-bit cryptographically random bearer token, self-signed TLS identity, and
`hostId`. A pairing file contains profile version, Host id and display name, HTTPS endpoint,
certificate SHA-256 fingerprint, and the one-time-exported raw token. The Host stores only the token
hash. Losing the pairing secret requires rotation; one token is active per Host and rotation
immediately revokes the previous token.

Registration imports a pairing file outside the model loop. The Agent stores the raw token in the
operating-system credential store and writes only a credential reference in ordinary configuration.
Bearer credentials appear only in the HTTPS `Authorization` header and MUST NOT enter URLs, command
arguments, model context, Session persistence, trace output, or logs. The client pins the exact
certificate fingerprint. Any browser `Origin` header is rejected by default.

The v1 deployment profile assumes direct intranet reachability. Bearer possession has the
permissions of the logged-in Host user and MUST be treated accordingly.

## 10. Host lifecycle and CLI

The official application exposes:

```text
dotcraft tool-host setup <https-endpoint> [-o|--output <pairing-file>]
dotcraft tool-host workspace add <workspace-id> <absolute-path>
dotcraft tool-host workspace list [--json]
dotcraft tool-host workspace remove <workspace-id>
dotcraft tool-host policy list [--json]
dotcraft tool-host policy set <tool-name> <allow|deny|needs-approval>
dotcraft tool-host autostart install|remove
dotcraft tool-host token rotate [-o|--output <pairing-file>]
dotcraft tool-host status [--json]
dotcraft tool-host serve
dotcraft tool-host register <pairing-file>
dotcraft tool-host unregister <host-id>
dotcraft tool-host list [--json]
dotcraft tool-host test <host-id>
```

The CLI is non-interactive. Setup requires an explicit HTTPS endpoint because it determines both
the listener and the certificate identity. Workspace registration requires an explicit stable
workspace id. Setup and token rotation write a pairing file named from `hostId` in the current
directory unless `--output` is supplied.

Registration MUST NOT accept the token as a command-line argument. `serve` composes a provider-free
workspace execution runtime and the Remote Tool Host transport. It does not compose a model
provider, Session Core, AppServer, memory, or Agent orchestration. User-level login autostart is the
v1 lifecycle; Windows user autostart is the initial conformance target.

## 11. Observability and conformance

The original Tool Call and Tool Result remain authoritative. Safe invocation provenance records
`executionTarget=remote`, `hostId`, `workspaceId`, `hostInstanceId`, `remoteInvocationId`, and remote
latency. Credentials and authorization headers are always redacted. The Host audit records MCP
session, workspace, tool, result code, duration, and cancellation without recording credentials.

Conformance tests cover:

- generated and reflection RPC eligibility, Core export, and ineligible-source rejection;
- contract hashing, missing/mismatched catalogs, and per-call revalidation;
- local, same-Turn connect, remote, disconnect, and no-fallback execution;
- Native SubAgent inheritance and independent routes over a shared process lease;
- same-client sharing, cross-client `WorkspaceBusy`, heartbeat expiry, and process failure;
- canonical path and symlink/reparse-point policy;
- allow, deny, elicitation acceptance, decline, and cancellation;
- background terminal and LSP cleanup at lease release;
- MCP text, image, audio, structured content, progress, cancellation, and errors;
- remote text materialization, remote `ReadFile` access, and lease-scoped artifact cleanup;
- token rotation, authentication failure, certificate pinning, Origin rejection, and redaction;
- a pure Host dependency graph with no model, Session, memory, or AppServer services; and
- a two-process HTTPS end-to-end execution flow.

When no Remote Tool Host is registered, the existing model tool schema and local execution behavior
remain unchanged.
