# DotCraft App Binding specification

| Field | Value |
|---|---|
| Version | 2.0.0 |
| Status | Normative |
| Date | 2026-07-15 |
| Related specs | [Tools architecture](../architecture/tools-architecture.md), [AppServer protocol](appserver-protocol.md), [Tool result presentation](tool-result-presentation.md), [Session Core](../architecture/session-core.md) |

App Binding is DotCraft's application connection and thread-authorization control plane. It does not define, attach, execute, or present tools. Ordinary application capabilities come from one binding-scoped MCP session; interactive presentation uses MCP Apps. Social-channel bindings authorize a conversation target whose operations are exposed by a managed native tool source.

The canonical cross-SDK method, state, and stable-error fixture is [`fixtures/app-binding-v2.json`](./fixtures/app-binding-v2.json). .NET, TypeScript, and Python SDK tests MUST consume this same fixture.

## 1. Boundary

App Binding owns:

- stable app identity, installation and connection state;
- request-specific handoff and one-click thread enablement;
- app-principal authentication and rotation;
- one binding grant and authority revision per app/thread relationship;
- binding MCP activation, rebind, status, capability approval, revoke and audit;
- verified social conversation authority and routing.

App Binding does not own:

- Dynamic Tool declarations or callbacks;
- executable tool catalogs, per-tool scopes, or tool-selection consent;
- tool dispatch, result schemas, Session item lifecycle, or approval policy;
- private iframe resources, UI calls, or context blocks;
- Teams roles or runtime behavior.

An app descriptor contains product identity, installation and connection UX, branding, and safe links. Execution declarations such as tool namespaces, scopes, and catalogs are invalid.

## 2. Capability negotiation

AppServer advertises `appBindingVersion: 2`. A client that requires App Binding MUST declare version 2. Unsupported App Binding methods return `AppBindingUpgradeRequired` with `requiredVersion: 2`.

## 3. App-principal connection

The app principal is scoped to workspace, user, and app id. The standard flow is:

1. A trusted client calls `app/connection/start`.
2. The app reads the short-lived request with `app/connection/request/get`.
3. The app completes it with `app/connection/connect`; DotCraft returns a raw principal credential exactly once.
4. An initialized AppServer connection calls `app/connection/authenticate` with that credential.
5. Authenticated connections may call only the app-role methods for their own principal.

Principal credentials expire after 30 days. `app/connection/refresh` atomically rotates the credential and immediately invalidates the old credential. DotCraft persists only principal identity, expiry, random salt, and a fixed-time comparable verifier. Raw principal credentials are never returned after creation or rotation.

`app/connection/revoke` invalidates the principal, prevents new activation/rebind, and revokes all bindings owned by it. Disconnecting a control connection does not stop an otherwise healthy binding MCP session. Multiple authenticated connections for the same principal may coexist.

## 4. Ordinary binding workflow

### 4.1 One-click enable

`thread/appBindings/enable` is one whole-app authorization action for one thread. It creates a request in `connecting`. If the principal is connected, DotCraft notifies it through `app/binding/requested`; otherwise the result includes a request-specific handoff.

The principal reads the request with `app/binding/request/get` and calls `app/binding/activate` with a validated Streamable HTTP endpoint and a newly generated binding bearer. The binding enters `syncing` while DotCraft initializes MCP and validates the initial capability snapshot. The original enable action approves that first valid snapshot; there is no routine second confirmation.

The binding becomes `active` only after the approved snapshot and live runtime are atomically available.

### 4.2 Rebind

After restart, durable non-revoked bindings load as `offline`. The authenticated principal obtains its eligible bindings through `app/bindings/list` and calls `app/binding/rebind` with the current authority revision, a validated endpoint, and a new bearer.

Rebind retains the approved capability baseline. It requires no user confirmation unless the new snapshot expands authority. A stale principal, binding id, authority revision, or bearer cannot resurrect a revoked binding.

### 4.3 Status

Binding state is one of:

- `connecting`: waiting for app activation;
- `syncing`: MCP is connecting or its snapshot is being validated;
- `active`: the approved snapshot has a live runtime;
- `offline`: durable authority exists but its live runtime is unavailable;
- `needsConfirmation`: a candidate expansion awaits thread-side confirmation;
- `revoked`: authority and credential verifier are removed;
- `failed`: stable failure requiring app or user action;
- `cancelled`: an unfinished enable request was cancelled.

State labels are not authority. Every dispatch checks binding id, authority revision, approved capability revision, live lease, and common invocation policy.

## 5. Binding MCP

Every ordinary binding owns an independent MCP session, bearer, generation, approved snapshot, and lifecycle. Binding MCP is additive to thread/workspace MCP configuration and uses binding origin provenance. Thread MCP null/empty/replacement semantics cannot remove it.

Only Streamable HTTP is accepted:

- loopback HTTP and remote HTTPS are allowed;
- remote plaintext HTTP, stdio, commands, arguments, environment variables, and working directories are forbidden;
- redirect or endpoint changes that cross a trust boundary require a new validated activation.

The raw binding bearer is memory-only. Restart therefore makes the binding offline until rebind rotates it. A control-connection failure does not terminate a healthy MCP session; MCP loss affects only its owning binding.

Revoke immediately removes dispatch authority, cancels calls owned by the binding, closes its MCP session and MCP App views, clears in-memory secrets, increments authority revision, and invalidates the next effective tool snapshot. It does not revoke sibling bindings unless the whole app principal is revoked.

## 6. Capability snapshots

### 6.1 Contents and limits

During `syncing`, DotCraft reads the MCP tool catalog and every MCP Apps resource associated with those tools. At most 32 associated resources are inspected, with concurrency 4, a 10-second per-resource timeout, 2 MiB per resource, and 8 MiB total.

The durable normalized summary contains only information needed for display, semantic comparison, offline stubs and audit:

- canonical tool identity and input schema;
- visibility and risk/approval annotations;
- UI resource association;
- normalized CSP, domain, and permissions;
- source/content/security hashes used for diagnostics.

It never contains a credential, live executor, resource body, result `_meta`, or private tool output.

An unavailable or invalid UI resource excludes that presentation but does not remove an otherwise text-correct tool. A presentation that later becomes usable is a capability expansion.

### 6.2 Expansion

The following require `thread/appBindings/confirmCapabilities`:

- a new tool;
- input-schema widening, including a removed required constraint, added optional input, broader type/enum/range, or broader `additionalProperties`;
- broader model/app visibility;
- less restrictive risk or approval annotations;
- a new UI association or broader app-call authority;
- broader CSP domains or iframe permissions.

Removal and provable narrowing take effect without confirmation. Stable reordering, title/description changes, endpoint rotation, and bearer rotation do not require confirmation. Complex or ambiguous schema/security differences are expansion.

While `needsConfirmation`, candidate capabilities are not callable. Acceptance atomically promotes the candidate revision. Rejection discards the candidate and moves the binding offline; the previous `ApprovedTools` snapshot remains only as a non-executable registration, display, audit, and future-diff baseline. A compatible authenticated rebind may return the binding to `active` without another confirmation, while any later expansion creates a new candidate requiring confirmation.

### 6.3 Offline stubs

An offline binding preserves model-visible, schema-stable registrations from its last approved snapshot for prompt-cache stability. Their live lease fails before remote dispatch with `AppBindingOffline`. A revoked binding exposes no stub.

Turn snapshots remain immutable, but revocation and authority-revision checks apply at dispatch time and override an older snapshot.

## 7. Social-channel bindings

Social binding authorizes one verified channel/account/conversation target for one thread. It does not create a binding MCP session.

The dedicated methods are:

- `thread/socialBindings/request/create`;
- `app/socialBinding/request/get`;
- `app/socialBinding/accept`;
- `app/socialBinding/rebind`;
- `app/socialBinding/resolve`.

The channel adapter is authenticated by its AppServer channel identity. It resolves a short-lived bind request, proves control of the concrete target, and supplies the canonical channel name, account id, conversation kind/id, delivery target, display label, and bound-by principal. DotCraft atomically enforces target uniqueness.

A managed social `IToolSource` contributes Plugin Native registrations for active/offline social bindings. Canonical identity retains the channel namespace and source tool name. Its authority reference includes the binding revision and verified target.

Target-like fields such as `target`, `deliveryTarget`, `chatId`, `groupId`, and `conversationId` are reserved. A channel descriptor that exposes one at the top level is rejected. Invocation also rejects case or alias variants before dispatch. The verified delivery target is injected only into trusted channel call context.

Explicit social tools and final assistant delivery resolve the same binding revision and target. Channel unavailability retains the authority and stable offline tools but returns a stable unavailable error. Revocation blocks both tool and final-reply routing.

Threads created by inbound channels retain their independent Origin Channel authority and tool source. They neither require App Binding nor merge with a Desktop social binding by display name.

## 8. Public methods and notifications

Trusted client methods:

- `app/list`, `app/view`;
- `app/connection/start`, `app/connection/status`, `app/connection/revoke`;
- `thread/appBindings/enable`, `thread/appBindings/list`, `thread/appBindings/confirmCapabilities`, `thread/appBindings/revoke`;
- `thread/socialBindings/request/create`.

App-principal methods:

- `app/connection/request/get`, `app/connection/connect`, `app/connection/authenticate`, `app/connection/refresh`;
- `app/binding/request/get`, `app/binding/activate`, `app/binding/rebind`, `app/bindings/list`.

Channel-principal methods are the social methods in Section 7 plus `app/threadInput/enqueue` where the External Channel Adapter protocol permits it.

Notifications are `app/connection/changed`, `app/binding/requested`, and `thread/appBindings/changed`. They contain stable states/reasons and no secret.

## 9. Persistence, audit, and security

The version 2 store is workspace scoped and uses atomic replacement. Corruption preserves a diagnostic copy and cannot silently overwrite the file with empty state.

Audit records identify actor/principal, app, thread, binding, old/new state, authority and capability revisions, decision, stable reason, and timestamp. They contain no bearer, raw credential, resource body, or tool arguments.

Security invariants:

1. An app descriptor, endpoint, tool definition, resource, or invocation argument cannot grant authority.
2. Revocation and expiry are checked at dispatch, not only snapshot construction.
3. One binding cannot reuse another binding's bearer, MCP session, view, target, or authority revision.
4. App-principal connections cannot call general AppServer methods.
5. Common tool approval remains required after whole-app enablement.
6. UI support is optional; useful non-interactive output remains required.

## 10. Acceptance

- One enable action activates and approves the initial binding MCP snapshot.
- Restart creates offline stubs and authenticated rebind rotates the bearer.
- Capability expansion is semantic, confirmed by the thread owner, and unenforceable before acceptance; rejection leaves the binding offline until a compatible authenticated rebind.
- App principal, binding bearer, and binding grant have independent revoke scopes.
- Managed social tools use native registrations and server-owned targets.
- Origin-channel execution remains independent.
- Version 1 execution, private UI, scopes, catalogs, attachments, and context blocks are absent.
- Core, Desktop, .NET, TypeScript, and Python agree on the version 2 wire contract.
