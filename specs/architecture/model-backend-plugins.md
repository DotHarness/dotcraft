# Model backend plugins

| Field | Value |
|---|---|
| Status | Living |
| Date | 2026-10-19 |

## Identity and ownership

An explicitly selected model backend owns provider construction, supported protocol identifiers,
authentication methods, capabilities and request execution. A configured provider is
an instance of a backend. Protocol names describe the implementation's input dialect and never select
an explicitly configured backend.

Agents owns provider-neutral backend and request contracts. Core contributes backends through
`IModelBackendSource`. Runtime adapts collectible implementations and joins their lifetime to the
existing generation gate. The application still explicitly selects its built-in providers.

Backend IDs are unique workspace-wide. Activation stages backend registration and rejects duplicate
IDs. Backend sources are workspace-scoped and cannot replace other
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
catalogs, authentication and usage. Backend generation and instance configuration
participate in cache identity. Authentication changes invalidate affected identities.

The host applies its normal retry policy to backend requests. The host remains responsible for tool policy, approval and execution. Backends emit standard model
tool calls and consume the resulting tool messages.

## Generation lifetime

Factories, clients, authentication and model catalogs cross the generation boundary only through
host-owned adapters. Async calls and streaming enumeration hold the gate throughout. Capability
lookup is limited to model catalogs, authentication and usage; arbitrary plugin service objects and
raw private SDK values cannot escape through `GetService` or response metadata.

Revocation removes routes before signalling generation stopping. Login, refresh and inference receive
the stopping cancellation token. Admitted work drains before provider-owned resources are disposed.
Cached clients and agents are invalidated when backend contributions change. Provider-dependent
background work starts only after plugin activation.

## Authentication

Authentication is scoped to a configured provider instance. Supported methods describe API-key,
OAuth, delegated CLI or ambient/keyless access. Availability checks distinguish missing credentials
from unavailable dependencies. A plugin may delegate all credential management to an external CLI.

Host credential storage partitions by backend ID and provider ID. Stored JSON never enters workspace
configuration, rollouts or ordinary logging. Credential files are created with current-user
permissions before any secret is written; writes use atomic replacement and cross-process locking. Refresh, login commit
and logout serialize per partition. Cancellation is checked before committing a write.
Plugins receive only a bound partition view.

Backend authenticators join built-in sign-in, including ChatGPT, in one Core coordinator that Desktop
and CLI share. AppServer projects it through [provider authentication](../protocols/provider-auth.md).

Provider-independent startup permits plugin discovery, trust, configuration and login before any
usable model exists. Standalone ModelService hosts reject local-backend configurations.

## Requests and conversation state

Backends are stateless toward the host. Each sampling request carries the complete model-visible
conversation, including system instructions and tool declarations, through the standard chat client.
The active request context identifies the conversation, turn and request kind; a backend that keeps
an external session keys it by that identity and rebuilds it when the conversation it receives no
longer matches. Compaction, fork and rollback reach a backend only as a changed conversation on its
next request.

The host persists provider-neutral history for backend providers. Provider-native history, compaction
bridges and native compactors remain built-in provider features.
