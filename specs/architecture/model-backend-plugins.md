# Model backend plugins

| Field | Value |
|---|---|
| Status | Living |
| Date | 2026-10-19 |

## Identity and ownership

An explicitly selected model backend owns provider construction, supported protocol identifiers,
authentication methods, capabilities, request execution and retry policy. A configured provider is
an instance of a backend. Protocol names describe the implementation's input dialect and never select
an explicitly configured backend.

Agents owns provider-neutral backend and request contracts. Core contributes backends through
`IModelBackendSource`. Runtime adapts collectible implementations and joins their lifetime to the
existing generation gate. The application still explicitly selects its built-in providers.

Backend IDs are unique workspace-wide. Activation stages backend registration and rejects duplicate
IDs, including conflicts with built-ins. Backend sources are workspace-scoped and cannot replace other
contributions. Registrations are activation-only. Missing, disabled or untrusted backends leave
configuration readable but fail model operations without fallback.

## Configuration and resolution

`Providers.<id>.BackendId` selects a backend. `BackendSettings` contains instance-specific JSON
configuration. Without a backend ID, existing protocol, authentication and endpoint defaults remain
unchanged. Explicit backends validate their own settings and declared protocol/authentication choices.
They do not inherit HTTP endpoints, API-key requirements or protocol-based hosted capabilities.

Desktop lists backend instances with every other provider and creates them from the same editor.
Backend-specific options use typed controls in a plugin settings surface. `BackendSettings` holds
their serialized values. Authentication uses the shared provider sign-in flow and backend credential
storage.

One runtime resolver serves chat, native subagents, summaries, maintenance, cache warming, model
catalogs, authentication, usage and native history. Backend generation and instance configuration
participate in cache identity. Authentication changes invalidate affected identities. A stale catalog
refresh cannot publish after its generation is revoked.

Host retry is opt-in for explicit backends. Backend-owned retry disables the host reconnect wrapper.
The host remains responsible for tool policy, approval and execution. Backends emit standard model
tool calls and consume the resulting tool messages.

## Generation lifetime

Factories, clients, authentication, model catalogs and history sessions cross the generation boundary
only through host-owned adapters. Async calls and streaming enumeration hold the gate throughout.
Capability lookup is allowlisted; arbitrary plugin service objects and raw private SDK values cannot
escape through `GetService` or response metadata.

Revocation removes routes before signalling generation stopping. Login, refresh and inference receive
the stopping cancellation token. Admitted work drains before provider-owned resources are disposed.
Cached clients and agents are invalidated when backend contributions change. Provider-dependent
background work starts only after plugin activation.

## Authentication

Authentication is scoped to a configured provider instance. Supported methods describe API-key,
OAuth, delegated CLI or ambient/keyless access. Availability checks distinguish missing credentials
from unavailable dependencies. A plugin may delegate all credential management to an external CLI.

Host credential storage partitions by backend ID, provider ID and canonical defining configuration
source. Stored JSON never enters workspace configuration, rollouts or ordinary logging. Writes use
atomic replacement, current-user file permissions and cross-process locking. Refresh, login commit
and logout serialize per partition. Cancellation is checked before committing a write.
Plugins receive only a bound partition view.

Backend authenticators join built-in sign-in, including ChatGPT, in one Core coordinator that Desktop
and CLI share. AppServer projects it through [provider authentication](../protocols/provider-auth.md).

Provider-independent startup permits plugin discovery, trust, configuration and login before any
usable model exists. Standalone ModelService hosts reject local-backend configurations.

## Prompt provenance and native history

Each sampling request can carry an immutable `ProviderPromptSnapshot` with ordered fragments
classified as harness, caller instructions, project instructions, skill, memory, extension or opaque.
Fragments include source identity, text and placement. Provenance is captured while composing the
request and checked against the actual sampling text. It never changes the model-visible prompt.

Legacy assemblers or transforms that rewrite text without source mapping produce opaque fragments.
Backends must not treat opaque output as known-source text. Tool declarations remain in the normal
model request and provenance does not provide execution callbacks. Prompt projection and filtering
are backend responsibilities.

Existing request kinds, conversation identities, history sessions, attempt leases and awaited
history replacement coordinate stateful implementations. Runtime signal notifications are
observational and cannot replace awaited history mutation. Fork, rollback and compaction must
invalidate or update a parked external conversation before its next sampling request.

Native history is selected by `IProviderHistorySessionFactory`, independent of protocol name.
Explicit backend ledger records retain backend and configured provider identity through replay,
fork and recovery. Switching that identity rebuilds from neutral history; opaque entries from another
backend are never relabelled. Legacy unbound records remain valid only for built-in Responses.
