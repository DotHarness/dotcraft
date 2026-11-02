# Remote model service

| Field | Value |
|---|---|
| Version | 0.9.1 |
| Status | Living |
| Date | 2026-10-19 |

DotCraft runtimes select either direct model access or one remote model service. A service exposes
multiple configured providers. The worker runs the provider adapters, model loop, tools,
native history, and compaction policy. The service owns upstream credentials and network access.

## Transport boundary

Remote access replaces provider HTTP transport after native request construction. It carries
native request bodies, streaming responses, HTTP status, and protocol headers without translating
messages or tool calls into another inference format. Responses, Chat Completions, Anthropic,
native compaction, images, and catalogs use the same transport boundary. Provider-independent
Runtime startup does not require a reachable model service.

The caller authenticates separately from the upstream provider. Each caller has a stable identity
and an allowed provider set. The service selects the upstream address from its configuration and
accepts only supported model operations. Caller authorization, cookies, and hop-by-hop headers
never become upstream authentication. The service cannot execute workspace tools.

Provider descriptions contain protocol, authentication method, non-secret routing and model
metadata, and sanitized authentication status. They contain no API keys, OAuth tokens, or server
filesystem paths. Workers keep provider request shaping and native history.
Built-in backends publish each model's supported protocols, limits, reasoning options and prices.
Workers resolve the model's protocol before inference, and the service validates its operation.
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
The official host uses its own state directory; an embedded host supplies authorization,
provider resolution, credential storage, and usage recording.
Hosts explicitly compose built-in backends such as [OpenRouter](openrouter.md). Local plugin backends
remain outside the service boundary.

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
