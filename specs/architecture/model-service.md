# Remote model service

DotCraft runtimes select either direct model access or one remote model service. A service exposes
multiple configured providers. The worker runs the existing provider adapters, model loop, tools,
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
filesystem paths. Workers retain current provider request shaping and native history capabilities.
Thread, root thread, Turn, and request-kind correlation remain explicit. Cache routing identities
are scoped by the stable caller identity.

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

OpenAI authorization, exchange, refresh, and revocation have one implementation in DotCraft.
File storage and host-owned encrypted storage implement the same token-store contract. A credential
has one refresh owner per service instance. Login, logout, and refresh coordinate state updates;
new refresh tokens are persisted before being used by subsequent requests. Permanent authentication
failures require a new login. Browser interaction remains the responsibility of the calling host.

Service usage is extracted from upstream responses by provider-owned readers. Hosts receive
normalized token counts with caller and request correlation. Usage-recording failures do not fail
model responses. Request bodies and credentials are not included in these records.

## Deployment

The official Compose model service is independently deployable and may serve multiple Stacks.
Each Stack has a separate inference credential. Only the model service and its login helper mount
the upstream credential directory. Worker containers retain their own workspace and user state.
Remote providers are managed by the service; worker clients can select models and read status,
but cannot mutate providers or start or revoke upstream authentication.

## Validation

Direct and remote paths use the same scripted upstream and must preserve model-visible requests,
native history, compression, compaction, hosted tools, multimodal content, streaming, cancellation,
error classification, and usage. Integration tests exercise separate worker/service state,
concurrent refresh, restart, revoked caller access, and host-specific provider authorization.
