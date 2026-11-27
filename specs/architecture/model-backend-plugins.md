# Model backend plugins

| Field | Value |
|---|---|
| Version | 0.15.0 |
| Status | Living |
| Date | 2026-11-26 |

## Identity and ownership

An explicitly selected model backend owns provider construction, supported protocol identifiers,
authentication methods, capabilities and request execution. A configured provider is
an instance of a backend. Protocol names describe the implementation's input dialect and never select
an explicitly configured backend.

Agents owns provider-neutral backend and request contracts. Core contributes backends through
`IModelBackendSource`. Runtime adapts collectible implementations and joins their lifetime to the
existing generation gate. The application explicitly selects built-in providers and backends.
Built-in and plugin backends have separate lifetimes. Model-specific protocol and capabilities
are resolved before orchestration. See [OpenRouter integration](openrouter.md).

Backend IDs are unique workspace-wide, and built-in IDs are reserved. Activation stages backend
registration and rejects duplicate IDs. Backend sources are workspace-scoped and cannot replace other
contributions. Registrations are activation-only. Missing, disabled or untrusted backends leave
configuration readable but fail model operations without fallback.

## Configuration and resolution

`Providers.<id>.BackendId` selects a backend. `BackendSettings` contains instance-specific JSON
configuration. Without a backend ID, existing protocol, authentication and endpoint defaults remain
unchanged. Explicit backends validate their own settings and declared protocol/authentication choices.
They do not inherit HTTP endpoints, API-key requirements or protocol-based hosted capabilities.
Their model catalog reports each model's display name and reasoning capability; built-in
protocol catalogs never describe backend models. When a model is selected, a backend provider may
expose `IProviderModelRuntimeResolver` through `GetService` so runtime normalization projects
per-model metadata, capabilities, declared protocol selection and output token limits bounded by any
configured maximum while the host preserves provider identity, configured settings, non-built-in
status and generation tracking.

Desktop lists backend instances with every other provider and creates them from the same editor.
A backend descriptor may supply an optional short `Description` for provider selection, and the host
attributes plugin-contributed backends to their owning plugin (`pluginId`) so clients can present
the plugin's artwork and fallback subtitle.
Backend-specific options use typed controls in a plugin settings surface. `BackendSettings` holds
their serialized values. Authentication uses the shared provider sign-in flow and backend credential
storage.

One runtime resolver serves chat, subagents, summaries, maintenance, cache warming, model
catalogs, authentication and usage. Backend generation and instance configuration
participate in cache identity. Authentication changes invalidate affected identities. A replaced
plugin provider stops admitting new calls and releases its resources after its admitted calls
drain, without unloading the generation or interrupting other providers. Cleanup failures are
logged without blocking replacement.

The host applies its normal retry policy to backend requests. The host remains responsible for tool policy, approval and execution. Backends emit standard model
tool calls and consume the resulting tool messages.

## Generation lifetime

Factories, clients, authentication, model runtime resolvers and model catalogs cross the generation boundary only through
host-owned adapters. Async calls and streaming enumeration hold the gate throughout. Provider capability
lookup admits published contracts for model catalogs, model runtime resolution, authentication, account Usage, and native HTTP routing. Chat client
services and response metadata return only host-owned values.

Revocation removes routes before signalling generation stopping. Login, refresh and inference receive
the stopping cancellation token. Admitted work drains before provider-owned resources are disposed.
Cached clients and agents are invalidated when backend contributions change. Provider-dependent
background work starts only after plugin activation.

## Authentication

Authentication is scoped to a configured provider instance. Supported methods describe API-key,
OAuth, delegated CLI or ambient/keyless access. Availability checks distinguish missing credentials
from unavailable dependencies. A plugin may delegate all credential management to an external CLI.

Host credential storage partitions by host scope, backend ID, and provider ID. Stored JSON never enters workspace
configuration, rollouts or ordinary logging. Credential files are created with current-user
permissions before any secret is written; writes use atomic replacement and cross-process locking. Refresh, login commit
and logout serialize per partition. Cancellation is checked before committing a write.
Plugins receive only a bound partition view.

Backend authenticators join built-in sign-in, including ChatGPT, in one Core coordinator that Desktop
and CLI share. AppServer projects it through [provider authentication](../protocols/provider-auth.md).
An authenticator may throw `ProviderAuthenticationException` to report a controlled failure code and
message. The runtime copies only the text across the generation boundary without retaining plugin
exception instances, while unexpected exceptions remain redacted to `ProviderLoginFailed`.

Provider-independent startup permits plugin discovery, trust, configuration and login before any
usable model exists. Backend use in a [remote model service](model-service.md) requires explicit native HTTP routing.

## Requests and conversation state

A backend chat client may expose a host-owned `ModelSteeringPolicy` through its typed service
lookup. `AtNextBoundary` delivers a requested immediate steer as ordinary steering. `Immediate`
or no policy preserves the host's default interruption behavior. The host reads the policy from
the selected backend chat client when it builds the Agent, before applying middleware. The resolved
mode stays fixed for that Agent. The backend does not acquire queue ownership or intercept explicit
cancellation.

Backends are stateless toward the host. Each sampling request carries the complete model-visible
conversation, including system instructions and tool declarations, through the standard chat client.
The active request context identifies the conversation, turn and request kind; a backend that keeps
an external session keys it by that identity and rebuilds it when the conversation it receives no
longer matches. Compaction, fork and rollback reach a backend only as a changed conversation on its
next request.

A backend that reports aggregate billing rather than the latest model request attaches
`ProviderContextUsage` to its usage content. The attachment gives occupied tokens and, when known,
the latest request's input tokens. A null attachment marks occupancy as unknown and prevents
the host from inferring it from billing. A measured attachment can accompany zero billing usage.

The host persists provider-neutral history for backend providers. Provider-native history, compaction
bridges and native compactors remain built-in provider features.
