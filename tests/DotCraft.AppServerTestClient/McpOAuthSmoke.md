# Pre-registered MCP OAuth smoke test

Run this local server to test DotCraft's pre-registered OAuth login without an external service or a model account. It binds only to loopback and uses disposable test credentials.

## Start the server

From the repository root:

```powershell
dotnet run --project tests/DotCraft.AppServerTestClient -- mcp-oauth-smoke-server --port 18554 --callback-port 18555
```

The server registers two clients with the same callback, `http://127.0.0.1:18555/callback/`:

| Client ID | Test secret |
| --- | --- |
| `smoke-client-a` | `smoke-secret-a` |
| `smoke-client-b` | `smoke-secret-b` |

It advertises no dynamic registration endpoint. `/control/stats` reports authorization, refresh, and unexpected registration requests without recording credentials.

## Test with Desktop

1. Open a disposable workspace in the current development build of Desktop.
2. In Settings → MCP, add a Streamable HTTP server named `oauth-smoke`, with URL `http://127.0.0.1:18554/mcp`.
3. Enable the pre-registered OAuth client, enter `smoke-client-a` and `smoke-secret-a`, and set callback URL to `http://127.0.0.1:18555/callback/`. Save.
4. Select Authenticate. The local authorization endpoint validates the registered callback and PKCE, then redirects the browser back to DotCraft. The server should become ready and expose `echo`.
5. Use `echo` with message `OAUTH_SMOKE_OK`. Automated integration coverage invokes the tool directly, without a model. In Desktop, a configured agent can invoke the same tool from a chat.
6. Restart Desktop, then expire the server's access tokens:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:18554/control/expire
Invoke-RestMethod http://127.0.0.1:18554/control/stats
```

The next connection/tool call should refresh without reopening authorization. `registrations` stays zero and `authorizations` stays unchanged.

For the environment-reference variant, set a variable in the environment that starts Desktop, then select Environment variable as the secret source:

```powershell
$env:DOTCRAFT_MCP_SMOKE_SECRET = 'smoke-secret-a'
```

For secret rotation, update the server and the configured secret, then reconnect:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:18554/control/rotate/smoke-client-a -Body @{ secret = 'rotated-smoke-secret' }
Invoke-RestMethod -Method Post http://127.0.0.1:18554/control/expire
```

Changing to `smoke-client-b` / `smoke-secret-b` requires a fresh login. An occupied callback port or incorrect secret must fail rather than silently using a different callback or another client's tokens. Remove the test MCP entry after testing. Stop the server with Ctrl+C.

## Automated verification

```powershell
dotnet test tests/DotCraft.AppServer.Tests --filter FullyQualifiedName~McpPreRegisteredOAuth
dotnet test tests/DotCraft.Core.Tests --filter FullyQualifiedName~McpPreRegisteredOAuth
```

The integration tests start the same HTTP server with isolated temporary workspace/user-data directories. They cover AppServer configuration and login completion, actual callbacks, tool calls, cold refresh, secret rotation, client switching, fixed-port failure, explicit configuration removal, and dynamic registration without pre-registered configuration. Core tests cover token-cache isolation, configured-secret omission, environment precedence, and invalid callback configuration.
