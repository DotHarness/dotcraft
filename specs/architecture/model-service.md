# Remote model service

| Field | Value |
|---|---|
| Version | 0.15.0 |
| Status | Living |
| Date | 2026-11-26 |

DotCraft runtimes select either direct model access or one remote model service. A service exposes
multiple configured providers. The worker runs the provider adapters, model loop, tools,
native history, and compaction policy. The service owns upstream credentials and network access.

## Transport boundary

Remote access replaces provider HTTP transport after native request construction. It carries
native request bodies, streaming responses, HTTP status, and protocol headers without translating
messages or tool calls into another inference format. Responses, Chat Completions, Anthropic,
native compaction, images, and catalogs use the same transport boundary. Runtime startup does not
require a reachable model service. The provider catalog carries the [ChatGPT OAuth model
metadata](openai-subscription-auth.md) for Responses Lite selection and the worker's context budget.
It includes metadata for explicitly selectable models that are hidden from the picker. Picker
visibility applies to model listing, not runtime lookup by model ID.

The caller authenticates separately from the upstream provider. Each caller has a stable identity
and an allowed provider set. A provider that is not configured or not allowed returns
`403 provider_forbidden`. A configured, allowed provider that cannot be resolved for inference
returns `503 backend_unavailable`. The service selects the upstream address from its configuration
and accepts only supported model operations. Caller authorization, cookies, and hop-by-hop headers
never become upstream authentication. The service cannot execute workspace tools.

Provider descriptions contain protocol, authentication method, non-secret routing and model
metadata, and sanitized authentication status when known. They contain no API keys, OAuth tokens,
or server filesystem paths. An unknown upstream authentication status does not block provider
selection. A reported authentication failure does. The service authenticates each upstream request.
Workers keep provider request shaping and native history.

HTTP backends publish per-model protocol, effective output limit, reasoning options, prices when
known, and native request adaptation. Workers apply the effective output limit when building native
requests. A model explicitly rejected by the backend's resolver is omitted without removing other
models from the catalog. A backend must explicitly provide `IProviderNativeHttpRouter` to be served.
The worker selects a native protocol adapter from the model catalog and contacts only the service.
It does not load the backend or receive the upstream endpoint, authentication headers, or backend
settings. OpenRouter publishes only schema-defined supplier preferences for request shaping.

The service resolves the model and operation again from its authorized provider instance. An HTTP
backend maps the model to a standard Anthropic Messages, OpenAI Responses, or Chat Completions
request with a service-resolved HTTP(S) target and authentication. It accepts only the matching
POST inference operation, with a bounded UTF-8 JSON body and no query or compression. The request
context and body must identify the same model. The service does not rewrite that model or translate
the body. Native provider operations retain their own authorization and wire behavior. Backend
catalog refresh does not require preparing inference credentials.

Thread, root thread, Turn, and request-kind correlation remain explicit. The request context also carries
the service tier selected in the final native request options, so billing does not infer it from a
thread preference or inspect compressed request bodies. Cache routing identities are scoped by the
stable caller identity.

Cancellation closes the upstream request. OAuth recovery belongs to the service; model and
tool-loop retry remain in the runtime. An upstream response is not replayed after streaming starts.
Native failures retain their status, headers, and body. Service failures have stable codes and are
translated by the remote transport into DotCraft failures. There is no direct-credential fallback.

## Hosting and authentication

`DotCraft.Agents.Remote` supplies the client through Harness. `DotCraft.ModelService` supplies
registration and HTTP endpoints for an embedding ASP.NET host or the official `dotcraft
model-service` host. Neither service registration nor endpoint mapping creates a Session runtime.
The official host uses its own state directory. An embedded host supplies caller authorization,
provider resolution, credential storage, and usage recording. Hosts can register HTTP backends
directly, including [OpenRouter](openrouter.md), without enabling plugins. An embedded host carries
the stopping signal of the resolved backend generation for the complete HTTP request.

An embedding host resolves each inference request against its configured providers and caller
grants, independently of its catalog of available providers.

A host may opt into the shared [trusted .NET plugin runtime](dotnet-extensions.md) without starting
an Agent Session. It uses installed bundles, the same manifest and contribution contracts, and
explicit discovery and trust inputs. The official service scopes plugin enablement, trust, settings,
data, and credentials to its own state rather than inheriting the operator's workspace. Embedded
hosts bind those inputs and the provider credential store to their own authority and
provider-instance scope. Candidate plugins and dependencies must pass the host's admission policy
before activation. Trust grants apply to accepted contents, not just a plugin name.

The standalone CLI discovers installed bundles beneath `<state>/plugins`. Only IDs explicitly supplied with
`--plugin <id>` can activate, including dependencies. `plugin trust <id>` grants trust to the accepted
fingerprint. `plugin revoke <id>` removes all persisted grants for that plugin from the state-scoped
trust authority in one operation, even if its installed bundle has changed or is missing. Trust
administration does not require inference-ready provider configuration. The CLI validates provider
readiness for `serve` and `check`. Provider-specific authentication uses that same state scope, and
changes to file-backed provider credentials take effect without restarting the service.

The service registers admitted plugins' contributions through the shared runtime and composes only
model backend and provider Usage consumers. It does not compose tool execution, tool projection, or
plugin authoring. This limits what the host uses, not what admitted plugin code can execute.
Provider-instance login, logout, status, and
account Usage are management operations, separate from per-request inference accounting. A backend
supplies its own account Usage reader. A Usage contribution can supply it for an authorized native
API-key provider. Worker inference credentials never grant management access. Revocation prevents
new calls and stops work owned by the active plugin generation. Hosts can restart to apply package
or settings changes. Custom chat clients without native HTTP routes are outside the remote inference
boundary.

OpenAI authorization, exchange, refresh, and revocation have one implementation in DotCraft.
File storage and host-owned encrypted storage implement the same token-store contract. A credential
has one refresh owner per service instance. Login, logout, and refresh coordinate state updates;
new refresh tokens are persisted before being used by subsequent requests. Permanent authentication
failures require a new login. Browser interaction remains the responsibility of the calling host.

Service usage is extracted from upstream responses by provider-owned readers. Hosts receive
normalized token counts and nullable decimal USD `CostUsd` with caller and request correlation.
The service uses an upstream-reported amount, including zero, when present; otherwise it uses the
built-in model catalog and the shared per-request estimator. Cache read and write rates fall back to
the input rate, and unknown prices remain unspecified. OpenAI Responses applies the service-tier
rules in [Model options](../features/model-options.md), using the response tier before the requested
tier and preserving the Codex `default` echo exception. This adjustment follows the actual
`responses` or `responses/compact` operation, not the provider's default protocol. Other operations
receive no inferred Fast multiplier. This price is per HTTP model call, not a Session Turn total.

Usage-recording or cost-estimation failures do not fail model responses. Request bodies and
credentials are not included in these records. Usage reading never alters the forwarded response.

## Deployment

The official Compose model service is independently deployable and may serve multiple Stacks.
Each Stack has a separate inference credential. Only the model service and its login helper mount
the upstream credential directory. Worker containers retain their own workspace and user state.
Remote providers are managed by the service; worker clients can select models and read status,
but cannot mutate providers or start or revoke upstream authentication.

## DeepSeek search

Provider catalogs optionally advertise `webSearchBackend: "deepseek"` for official DeepSeek API-key providers; its absence disables remote DeepSeek search. This metadata contains no credentials and is carried through the worker's effective provider metadata and AppServer provider list.

`POST tools/search` uses the provider HTTP transport, caller authorization, correlation context, timeout, cancellation, and observers. The service accepts it only for a configured official DeepSeek provider, independently of whether its model protocol is OpenAI or Anthropic. It maps the logical operation to `https://api.deepseek.com/v1/tools/search` and injects the provider's API key using Bearer authentication. Client authorization and Anthropic model authentication headers are never forwarded as search credentials. Search does not require a model, does not execute workspace tools, and never falls back to worker credentials. Other providers cannot use this operation.

## Anthropic upstream contract

Anthropic requests use API version `2026-10-21` and `Authorization: Bearer` authentication, including model catalog requests. The service discards worker credentials and injects its configured upstream credential; remote SDK placeholder credentials never reach the upstream.
