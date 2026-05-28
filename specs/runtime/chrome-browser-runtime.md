# DotCraft Chrome Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | Living |
| **Date** | 2026-05-11 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Plugin Architecture](../extensions/plugin-architecture.md), [Desktop Client](../clients/desktop-client.md) |

Purpose: define the behavior contract for DotCraft's Chrome-backed browser automation runtime. Browser work is thread-bound, session-scoped, cancellable, recoverable, and safe to diagnose without exposing Chrome profile data.

---

## 1. Scope

This spec covers:

- Thread-bound Chrome browser automation exposed through `NodeReplJs`.
- Browser session identity, command lifecycle, tab ownership, timeout, cancellation, diagnostics, and cleanup semantics.
- Chrome extension and native host backend connectivity.
- Compatibility expectations between the Desktop embedded browser backend and the Chrome backend.
- Agent-facing recovery expectations for Chrome setup and runtime failures.

This spec does not define:

- General web browsing UX unrelated to agent automation.
- Chrome Web Store publication, enterprise deployment policy, or extension branding.
- Browser automation against cookies, passwords, local storage, Chrome history, profile databases, or other profile storage files.
- A full browser trace viewer or new browser API surface beyond the documented compatibility subset.

---

## 2. Goals

1. **Durable browser sessions**: Browser automation is a thread-bound session with explicit turn and evaluation metadata, stable JavaScript bindings, and deterministic cleanup semantics.
2. **Stable agent state**: Recoverable browser command errors do not destroy unrelated Node REPL state such as `globalThis.browser` or `globalThis.tab`.
3. **Predictable tab ownership**: User, claimed, created, kept, released, and closed tabs have explicit lifecycle rules.
4. **Bounded data transfer**: Page data and `tab.evaluate` results are capped before large payloads can destabilize the REPL or transport.
5. **Actionable recovery**: Setup, backend, command, timeout, cancellation, debugger, and result-size failures have stable categories and user-safe recovery guidance.
6. **Privacy preservation**: Diagnostics never include page bodies, cookies, localStorage, full URLs, profile paths, pipe paths, extension ids, or native host manifest paths.

---

## 3. Architecture

The runtime has four layers:

1. **AppServer binding**
   - Binds a thread to the AppServer client connection that declared `capabilities.nodeRepl` and `capabilities.browserUse`.
   - Routes `ext/nodeRepl/evaluate` and `ext/nodeRepl/cancel` to that client.
   - Treats browser runtime failures as plugin function results, not provider failures.

2. **Desktop Node REPL manager**
   - Owns one persistent JavaScript context per thread while the Desktop connection remains bound.
   - Injects `agent`, `display`, and `dotcraft`.
   - Tracks active `evaluationId`, outer timeout, cancellation, and late-result suppression.
   - Keeps REPL state across command-level Chrome errors.

3. **Browser session runtime**
   - Exposes `agent.browsers.list()` to enumerate available browser backends and `agent.browsers.get(id)` to acquire a backend-specific browser handle.
   - Carries `sessionId`, `turnId`, `evaluationId`, `backendId`, and tab ownership metadata on browser commands.
   - Provides command-level timeout, cancellation, result-size limiting, and diagnostics.

4. **Chrome backend**
   - Controls the user's existing Chrome profile through the DotCraft Chrome extension and native host.
   - Uses native pipe backend discovery and a framed session command protocol.
   - Implements the shared browser/tab API subset advertised by the Chrome skill.

---

## 4. AppServer and Session Metadata

`ext/nodeRepl/evaluate` accepts optional browser metadata in addition to the legacy evaluate payload:

```json
{
  "threadId": "thread_abc",
  "turnId": "turn_123",
  "evaluationId": "node-repl-...",
  "browserSession": {
    "protocolVersion": 1,
    "sessionId": "thread_abc",
    "threadId": "thread_abc",
    "turnId": "turn_123",
    "evaluationId": "node-repl-..."
  },
  "code": "await browser.user.openTabs()",
  "timeoutMs": 30000
}
```

Rules:

- `browserSession.sessionId` is the isolation key and normally equals the thread id.
- `turnId` is preserved when available.
- `evaluationId` changes for each Node REPL call.
- Desktop exposes the active value as `globalThis.dotcraft.browserSession`.
- Browser backends read the active value at command time so persistent JavaScript state can survive across evaluations while each command still carries the latest `evaluationId`.
- Chrome commands without `sessionId`, `turnId`, or `evaluationId` fail with `SessionMetadataMissing`.

The AppServer `capabilities.browserUse` object may advertise optional browser metadata such as browser session protocol version, command cancellation support, result-size limits, timeout limits, typed finalize support, and Chrome diagnostics support. Unknown capability fields are optional and forward-compatible.

---

## 5. Evaluation and Error Isolation

There are two timeout and cancellation levels:

| Level | Owner | Effect |
|-------|-------|--------|
| Evaluation timeout/cancel | Desktop Node REPL manager | Cancels the active evaluation and may reset the REPL context. |
| Browser command timeout/cancel | Browser client/backend | Fails only the current JavaScript promise and preserves thread REPL state. |

Command-level errors must not clear REPL state. Examples:

- `BridgeDisconnected`
- `CommandTimeout`
- `CommandCancelled`
- navigation or locator timeout
- `ResultTooLarge`
- `DebuggerUnavailable`
- `UnsupportedApi`
- ordinary JavaScript rejection from a browser command

Outer control errors may clear or rebuild REPL state:

- `NodeReplJs timed out after ...`
- `NodeReplJs cancelled`
- explicit user/client reset
- VM context creation failure
- AppServer thread binding replacement or disconnection

Outer timeout/cancel must first invoke the registered Chrome cancellation hook for the active `evaluationId`, then proceed with normal REPL cleanup.

---

## 6. Chrome Host Transport

The Chrome backend does not use a fixed TCP port. After Chrome starts the native messaging host, the host creates one local pipe per process:

| Platform | Address Shape |
|----------|---------------|
| Windows | `\\.\pipe\dotcraft-chrome-<pid>-<nonce>` |
| macOS/Linux | `<tempdir>/dotcraft-chrome-<pid>-<nonce>.sock` |

Desktop discovers candidates by scanning the platform pipe prefix, connecting to candidates, sending `getInfo`, and selecting a protocol-compatible backend. Discovery must not fall back to the historical fixed TCP port.

The browser-client and native host use 4-byte little-endian length-prefixed JSON frames. Newline-delimited JSON is not part of the Chrome host protocol.

### Command Envelope

```json
{
  "id": 1,
  "kind": "command",
  "commandId": "chrome-command-...",
  "method": "tab.evaluate",
  "params": {},
  "browserSession": {
    "protocolVersion": 1,
    "sessionId": "thread_...",
    "turnId": "turn_...",
    "evaluationId": "node-repl-..."
  },
  "timeoutMs": 15000
}
```

### Cancel Envelope

```json
{
  "id": 2,
  "kind": "cancel",
  "commandId": "chrome-command-...",
  "browserSession": {
    "sessionId": "thread_...",
    "turnId": "turn_...",
    "evaluationId": "node-repl-..."
  },
  "reason": "outer-timeout"
}
```

### Response Envelope

```json
{
  "id": 1,
  "ok": true,
  "result": {}
}
```

On failure:

```json
{
  "id": 1,
  "ok": false,
  "error": "CommandTimeout: Chrome extension command timed out."
}
```

Event envelopes use:

```json
{
  "kind": "event",
  "event": "backend.closed",
  "browserSession": {},
  "data": {}
}
```

---

## 7. Command Lifecycle and Cancellation

Each command follows this lifecycle:

1. Browser client generates `commandId`, records pending state, and sends a framed command envelope.
2. Native host forwards the command to the Chrome extension and starts a matching timeout.
3. Extension records `commandId -> pending command` and runs the requested operation.
4. Completion resolves the pending command only if it has not been cancelled.
5. Timeout or cancellation removes pending state, sends cancel downstream, and rejects the caller with a classified error.
6. Late results for cancelled commands are discarded.

Command timeouts are clamped to `1..120000` ms.

Cancellation is cooperative:

- Browser client sends cancel envelopes for pending commands matching the cancelled `evaluationId`.
- Native host forwards cancel to the extension and clears its pending maps.
- Extension wait/poll commands observe the cancel signal and stop early.
- Already issued single CDP calls are not aggressively killed; their late results are ignored.

Cancelable wait/poll commands include navigation wait, URL wait, load state wait, locator waits/actions, file chooser wait, and temporary tab content wait.

---

## 8. Tab Ownership and Finalize

Each backend tracks ownership per `sessionId`:

| State | Meaning |
|-------|---------|
| `user` | Browser tab exists outside the agent session. |
| `claimed` | The session adopted a user tab returned by the latest `openTabs()` call. |
| `created` | The session opened the tab through agent browser APIs. |
| `kept` | The session explicitly kept the tab at finalization. |
| `released` | The session released a claimed/adopted tab at finalization. |
| `closed` | The session closed an agent-created tab at finalization. |

Rules:

- `browser.user.openTabs()` is read-only and may expose title, URL, recency, and grouping metadata only.
- `browser.user.claimTab(tab)` accepts a tab object returned by the current session's latest `openTabs()` call, or an id from that exact result.
- Guessed ids and stale-session ids are rejected.
- Agent-created tabs are closed by default at finalize.
- Claimed user tabs are released by default at finalize.

Finalization requires typed keep entries:

```js
await browser.tabs.finalize({
  keep: [{ tab, status: "deliverable" }]
});
```

`status` must be `"handoff"` or `"deliverable"`.

Invalid legacy shapes:

- `browser.tabs.finalize({ keep: [tab] })`
- `browser.tabs.finalize({ keep: [id] })`
- `browser.tabs.finalize({ keep: true })`

Finalize returns a summary:

```json
{
  "ok": true,
  "kept": ["tab-id"],
  "closed": ["tab-id"],
  "released": ["tab-id"]
}
```

---

## 9. Data Limits

Browser command results must be bounded:

- `tab.evaluate(fnOrSource, arg, { timeoutMs, maxBytes })` has a default maximum serialized result size of 1 MB.
- Callers may request a smaller `maxBytes` but cannot raise the default cap through public JS API.
- Oversized results fail with `ResultTooLarge`.
- `ResultTooLarge` includes the configured limit and a coarse actual or estimated serialized size when known, but never includes the oversized content.
- Content reads use `maxLength` where available.
- Large pages and logs should be read with page-side filtering, smaller chunks, or bounded content reads rather than full-document evaluate results.

---

## 10. Setup Status and Diagnostics

`chrome.checkSetup()` returns normalized setup checks:

```json
{
  "extension": { "ok": true, "code": "extensionReady", "message": "DotCraft Chrome extension is ready." },
  "nativeHost": { "ok": true, "code": "nativeHostReady", "message": "Chrome Native Host is installed." },
  "chromeRunning": { "ok": true, "code": "chromeRunning", "message": "Chrome is running." },
  "installedBrowsers": { "ok": true, "code": "chromeInstalled", "message": "Google Chrome is installed." },
  "backend": { "ok": true, "code": "backendConnected", "message": "Chrome backend is connected." },
  "bridge": { "ok": true, "code": "backendConnected", "message": "Chrome backend is connected." }
}
```

`bridge` remains a compatibility alias for `backend`; new code should use `backend`.

Each setup check uses:

- `ok`: boolean result.
- `code`: stable diagnostic code.
- `message`: short user-safe message.
- `action`: optional recovery action id.
- `safeDetails`: optional safe summary fields.

Allowed diagnostics:

- session id, turn id, evaluation id, command id;
- backend id, method, status, elapsed time, timeout, cancellation flag;
- error category;
- pipe candidate count, compatible backend count, reconnect count;
- backend protocol version and coarse setup counts.

Forbidden diagnostics:

- page text, DOM, request/response bodies, cookies, localStorage;
- full URLs;
- full native pipe paths;
- Chrome profile paths;
- extension ids;
- native host manifest paths;
- oversized result content.

Desktop settings use "Chrome backend" / "Chrome 后端" terminology. "Chrome Bridge" is legacy wording and must not appear in new UI copy.

Overall setup status priority:

1. Chrome missing
2. Extension not ready
3. Native host missing
4. Chrome not running
5. Backend disconnected
6. Connected

Backend disconnected recovery text:

> Make sure Chrome is open, click the DotCraft Chrome extension icon, then refresh status.

The Chrome extension popup remains lightweight: connected state shows that the backend is ready, disconnected state directs the user to start/reconnect the backend, and `pipePath` is never rendered.

---

## 11. Error Categories and Recovery

Stable categories:

- `ChromeNotInstalled`
- `ChromeNotRunning`
- `ExtensionMissing`
- `ExtensionDisabled`
- `NativeHostInvalid`
- `BridgeDisconnected`
- `SessionMetadataMissing`
- `CommandTimeout`
- `CommandCancelled`
- `ResultTooLarge`
- `DebuggerUnavailable`
- `UnsupportedApi`

Agent recovery:

- `BridgeDisconnected`: explain as Chrome backend disconnected; run `dotcraft.chrome.checkSetup()` and ask the user to click the DotCraft Chrome extension icon if setup is otherwise healthy.
- `CommandTimeout`: fail only the current JavaScript promise; retry only after narrowing the command or increasing command timeout for a specific wait.
- `CommandCancelled`: do not blindly retry; confirm whether the user cancelled, the turn timed out, or the workflow should resume.
- `SessionMetadataMissing`: rerun Chrome runtime setup in the current Node REPL context so `dotcraft.browserSession` is available.
- `DebuggerUnavailable`: ask the user to close DevTools or another extension UI controlling the tab, then retry the specific command.
- `ResultTooLarge`: narrow the query, use `maxLength`, or read smaller chunks; do not retry the same large result with a longer timeout.
- `UnsupportedApi`: use the documented Chrome compatibility subset or ask before switching browser-control paths.

---

## 12. Acceptance

- Browser automation is thread-bound and survives normal command failures.
- `globalThis.browser` and `globalThis.tab` remain reusable across Node REPL calls until the thread binding or runtime is intentionally reset.
- Every Chrome command carries `sessionId`, `turnId`, `evaluationId`, and `commandId`.
- Missing session metadata fails with `SessionMetadataMissing`.
- Chrome backend discovery uses native pipe candidates and framed host protocol, with no fixed TCP fallback.
- Command timeout/cancel rejects only the current JavaScript promise.
- Outer Node REPL timeout/cancel sends Chrome cancel envelopes before resetting the REPL runtime.
- Wait/poll commands observe cooperative cancellation.
- Late command results do not resolve cancelled pending requests.
- `tab.evaluate` and content reads enforce bounded results.
- `browser.tabs.finalize({ keep: [{ tab, status }] })` is the authoritative cleanup boundary.
- Desktop and extension setup diagnostics show safe, actionable Chrome backend status.
- AppServer, Desktop, Chrome extension, native host, and browser-client tests cover command timeout, command failure, cancellation, result-size limits, tab finalization, setup diagnostics, and reconnect behavior.
