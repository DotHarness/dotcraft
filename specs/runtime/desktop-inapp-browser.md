# DotCraft Desktop In-App Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | Living |
| **Date** | 2026-06-05 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md), [Chrome Browser Runtime](chrome-browser-runtime.md) |

Purpose: define the behavior contract for DotCraft Desktop's embedded in-app browser automation runtime. The runtime is exposed to AppServer as `desktop-iab` and presents a browser-use compatible `iab` backend inside the thread-bound Node REPL.

---

## 1. Scope

This spec covers:

- Desktop embedded browser automation exposed through `NodeReplJs`.
- Browser client loading, native pipe backend discovery, framed JSON-RPC transport, command lifecycle, cancellation, diagnostics, and cleanup semantics.
- Tab identity, ownership, navigation, screenshots, DOM access, Playwright-compatible helpers, coordinate input, DOM-CUA, and capability behavior.
- Desktop viewer behavior that makes agent browser actions observable without stealing unrelated user focus.
- Compatibility expectations between the Desktop embedded browser backend and the Chrome backend.

This spec does not define:

- General user browsing UX unrelated to agent automation.
- Chrome extension or native host setup; see [Chrome Browser Runtime](chrome-browser-runtime.md).
- Automation against hidden profile data such as cookies, passwords, local storage, browser history, or cache databases.
- A new model-visible browser API beyond the documented browser-use compatibility subset.

---

## 2. Goals

1. **Browser-use compatible API**: DotCraft should load a DotCraft-owned browser client that preserves the documented browser-use compatible JavaScript shape and avoids maintaining a separate model-visible shim.
2. **Thread-bound isolation**: Browser sessions are isolated by thread/session metadata, while JavaScript bindings can survive across Node REPL evaluations.
3. **Recoverable command failures**: Navigation, locator, CDP, timeout, and unsupported-command errors fail the current browser promise, not the entire REPL runtime.
4. **Predictable tab ownership**: Created, claimed, kept, released, and closed tabs have explicit lifecycle rules.
5. **Independent command readiness**: Screenshots, DOM snapshots, locator waits, and coordinate actions use command-specific readiness instead of one global page-text gate.
6. **Observable automation**: Agent actions remain visible through viewer tabs, automation state, and a virtual cursor where practical.
7. **Safe diagnostics**: Errors are actionable without leaking page bodies, credentials, hidden browser storage, full pipe paths, or other sensitive data.

---

## 3. Architecture

The runtime has five layers:

1. **AppServer binding**
   - Binds browser automation to a client connection that declared both `capabilities.nodeRepl` and `capabilities.browserUse`.
   - Advertises the Desktop embedded backend as `desktop-iab`.
   - Forwards `threadId`, `turnId`, `evaluationId`, and `browserSession` to Desktop through `ext/nodeRepl/evaluate`.

2. **Desktop Node REPL manager**
   - Owns one persistent JavaScript context per bound thread while the Desktop connection remains active.
   - Injects `nodeRepl`, `dotcraft`, `display`, and `agent`.
   - Exposes `dotcraft.browserClientPath` and active `dotcraft.browserSession` so the browser client can initialize in the current turn.
   - Preserves REPL state across recoverable browser command errors.

3. **DotCraft browser client**
   - Provides `setupBrowserRuntime({ globals })`.
   - Discovers browser backends through `nodeRepl.nativePipe`.
   - Installs `agent.browsers`, browser handles, tab handles, capabilities, and browser-use helper APIs.
   - Treats the Desktop in-app browser backend as `iab` inside the browser-use API.
   - Is maintained as DotCraft source code under `desktop/resources/browser/scripts/`, not as an upstream minified browser client artifact.

4. **Desktop IAB backend server**
   - Runs in the Electron main process.
   - Listens on a local native pipe discovered by the browser client.
   - Speaks length-prefixed JSON-RPC.
   - Maps browser-use backend commands to viewer tabs, Electron `webContents`, CDP, Desktop browser policy, virtual cursor state, and diagnostics.

5. **Viewer browser surface**
   - Hosts in-app browser tabs as regular viewer tabs.
   - Shows automation state, session name, last action hints, and virtual cursor movement when available.
   - Keeps user focus stable after the initial agent-created tab open.

---

## 4. AppServer and Session Metadata

Desktop must continue to identify the embedded backend to AppServer as `desktop-iab`:

```json
{
  "capabilities": {
    "nodeRepl": { "backend": "desktop-node" },
    "browserUse": {
      "backend": "desktop-iab",
      "backends": ["desktop-iab"],
      "protocolVersion": 2,
      "browserSessionProtocolVersion": 1,
      "supportsCancel": true,
      "supportsCommandCancel": true
    }
  }
}
```

Rules:

- `browserUse.backend` is the AppServer-visible backend id. The browser client may use a different internal backend id.
- Inside Node REPL, the Desktop backend id is `iab` for browser-use compatibility.
- `browserSession.sessionId` is the isolation key and normally equals the thread id.
- `turnId` is forwarded when known.
- `evaluationId` changes for every Node REPL evaluation and is used for command cancellation and late-result suppression.
- The IAB backend `getInfo` result must include `metadata.dotcraftSessionId` equal to the active `browserSession.sessionId` so the browser client can select the current session's backend.
- Missing `sessionId` or `evaluationId` fails browser backend commands with `SessionMetadataMissing`.
- Unknown `browserUse` capability fields remain optional and forward-compatible.

---

## 5. Node REPL Environment

Desktop must provide the browser client with the following globals:

| Global | Requirement |
|--------|-------------|
| `nodeRepl.nativePipe.createConnection(path)` | Opens a connection only to DotCraft-owned browser-use native pipes. |
| `nodeRepl.env` | Provides browser-client environment toggles. |
| `nodeRepl.tmpDir` | Points to the platform temp directory used for pipe discovery where applicable. |
| `nodeRepl.emitImage(image)` | Displays screenshots and other image outputs. |
| `nodeRepl.setResponseMeta(key, value)` | Records browser-use response metadata. |
| `nodeRepl.createElicitation(request)` | Routes browser-use confirmation prompts through Desktop approval UX when required. |
| `nodeRepl.fetch` | May be provided for browser-client compatibility, but ambient network checks should be disabled unless explicitly required. |
| `dotcraft.browserClientPath` | Absolute path to the bundled browser client entrypoint. |
| `dotcraft.browserSession` | Active browser session metadata for the current evaluation. |

Default browser-client environment:

- `BROWSER_USE_AVAILABLE_BACKENDS=iab`
- `BROWSER_USE_DISABLE_AMBIENT_NETWORK=1`
- `BROWSER_USE_SECURITY_MODE=disabled-for-local-testing`

The browser client path should point to the DotCraft-owned browser client entrypoint under `desktop/resources/browser/scripts/`. Desktop must not rely on a separate model-visible API shim as the default runtime surface.

---

## 6. Native Pipe Transport

The Desktop IAB backend listens on one local pipe per Desktop process:

| Platform | Address Shape |
|----------|---------------|
| Windows | `\\.\pipe\dotcraft-browser-use-dotcraft-<pid>-<nonce>` |
| macOS/Linux | `<tempdir>/dotcraft-browser-use/dotcraft-<pid>-<nonce>.sock` |

Rules:

- The address must be discoverable by the DotCraft browser client. On macOS/Linux the client scans the `<tempdir>/dotcraft-browser-use/` directory and connects to matching socket files.
- Connections use 4-byte little-endian length-prefixed JSON frames.
- Payloads are JSON-RPC 2.0 request, response, and notification objects.
- Newline-delimited JSON and fixed TCP ports are not part of the IAB backend protocol.
- `nodeRepl.nativePipe.createConnection` must reject paths outside the expected DotCraft-owned browser-use pipe namespace.
- Pipe paths are forbidden in diagnostics and UI.

Example request:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "getInfo",
  "params": {
    "session_id": "thread_abc",
    "turn_id": "turn_123"
  }
}
```

Example response:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "id": "iab",
    "name": "DotCraft In-App Browser",
    "metadata": {
      "dotcraftSessionId": "thread_abc"
    }
  }
}
```

---

## 7. Backend Primitives

The IAB backend implements the primitive methods expected by the browser-use client. Public JavaScript APIs are owned by the browser client; backend primitives are not model-visible tools.

Required primitives:

| Method | Requirement |
|--------|-------------|
| `ping` | Health check for discovery and reconnect. |
| `getInfo` | Returns backend id `iab`, protocol metadata, safe capabilities, and `metadata.dotcraftSessionId`. |
| `getTabs` | Lists tabs owned by the current session. |
| `getUserTabs` | Lists claimable user tabs using safe title, URL, and recency metadata only. |
| `getUserHistory` | Always fails with `UnsupportedApi: browser.user.history is not supported by Desktop IAB`; Desktop never reads hidden browser history. |
| `claimUserTab` | Adopts a tab returned by the latest `getUserTabs` result for the same session. |
| `createTab` | Creates a new viewer browser tab and returns a backend tab id. |
| `finalizeTabs` | Applies typed keep, release, and close rules. |
| `nameSession` | Updates viewer automation session labels. |
| `attach` / `detach` | Manages top-level CDP attachment for a tab. |
| `attachTarget` / `detachTarget` | Manages target-scoped CDP sessions for frames and related targets. |
| `executeCdp` | Runs a CDP command with command timeout, cancellation, and result-size limits. |
| `moveMouse` | Updates the visible virtual cursor and action hints. |
| `executeUnhandledCommand` | Handles backend-specific capability commands not implemented directly by the browser client. |

Tab ids exposed through backend primitives must be stable numeric ids scoped to the browser session. Desktop may keep existing viewer tab ids internally, but those ids must not leak as browser-use backend tab ids.

Target-scoped CDP sessions are available only when Electron debugger can attach the target and returns a concrete `sessionId`. Unsupported frame or OOPIF targets must fail with `UnsupportedApi` rather than silently succeeding or routing commands to the top-level page.

---

## 8. Tab Ownership and Finalize

Each session tracks tab ownership:

| State | Meaning |
|-------|---------|
| `user` | Viewer browser tab exists outside the agent session. |
| `claimed` | The session adopted a user tab returned by the latest `getUserTabs` call. |
| `created` | The session created the tab through browser-use APIs. |
| `kept` | The session explicitly kept the tab at finalization. |
| `released` | The session released a claimed/adopted tab at finalization. |
| `closed` | The session closed an agent-created tab at finalization. |

Rules:

- Agent-created tabs are regular viewer tabs.
- Creating the first browser tab for a session may focus the viewer tab; later automation updates must not steal focus.
- Agent-created tabs close by default at finalization.
- Claimed user tabs release by default at finalization.
- Stale guessed tab ids are rejected with `TabStale` or `InvalidArgument`.
- `browser.tabs.finalize({ keep })` is the authoritative cleanup boundary.
- Keep entries must be typed as `handoff` or `deliverable` when typed finalize is advertised.
- Model-facing API descriptions and validation errors should show the typed form `finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })`.
- Temporary tabs used only for inspection should be closed explicitly with `tab.close()` or cleaned up through finalization; `browser.tabs.content({ urls })` is preferred for read-only temporary page fetches.

---

## 9. Navigation and Readiness

Navigation is command-scoped and must not depend on a successful DOM snapshot.

Rules:

- Local development hosts such as `localhost:3000`, `127.0.0.1:5173`, and `[::1]:8080` default to `http://` when no scheme is supplied.
- Navigation remains subject to Desktop browser policy, including external-domain approval, allowed domains, and blocked domains.
- Main-frame `did-fail-load` must become a structured navigation failure containing safe error code, safe description, requested URL summary, and final URL summary when available.
- Chromium error pages such as `chrome-error://chromewebdata/` must not be reported as successful navigation to the requested site.
- Screenshots may run on empty, loading, or error pages and must not require useful body text.
- DOM, locator, and accessibility commands use their own readiness and wait behavior.
- DOM snapshot readiness may proceed for an `interactive` or `complete` document with an existing `document.body`, even when text and interactable-element heuristics are temporarily empty.
- `waitForLoadState("domcontentloaded")` must complete for an already loaded tab when `document.readyState` is `interactive` or `complete` and `document.body` exists; readiness sampling must not depend on `requestAnimationFrame`, which can be throttled in hidden or unfocused tabs.
- A page-text-length heuristic must not be a global precondition for unrelated commands.

---

## 10. Page Data and API Compatibility

The model-visible Browser API is defined by the bundled browser client and the bundled Browser skill documentation.

Requirements:

- Capabilities returned by `browser.capabilities.get(...)` and `tab.capabilities.get(...)` are asynchronous when the browser client defines them as asynchronous; skill examples must use the matching `await` shape.
- Unsupported APIs fail with `UnsupportedApi` and a stable English fallback message instead of being absent, hanging, or silently ignored.
- The supported reference-client subset includes `tabs.new/selected/list/get/content/finalize`, `browser.user.openTabs/claimTab`, browser `visibility` and `viewport` capabilities, `tab.goto/back/forward/reload/title/url`, screenshots, virtual clipboard `readText/writeText/read/write`, `playwright.evaluate(fnOrExpression, arg?, options?)`, `domSnapshot`, `waitForURL`, real `waitForLoadState`, `waitForTimeout`, `expectNavigation`, common locator reads and actions, `locator.all()` cached reads, `locator.filter()`, `locator.and()`, `locator.or()`, scoped `locator(selector, options)` filters, `getByRole/Text/Label/Placeholder/TestId`, same-origin `frameLocator`, coordinate CUA actions, DOM-CUA visible-node actions, `pageAssets.list/bundle`, and page-defined WebMCP tools through `tab.capabilities.get("webmcp")`.
- `executeUnhandledCommand` accepts browser-use compatible command aliases for BrowserUser, navigation, screenshots, Playwright evaluate/DOM/locator operations, CUA, DOM-CUA, pageAssets, WebMCP, viewport, visibility, tabs content, clipboard, and dev logs. Aliases must normalize snake_case and camelCase fields to the same Desktop IAB runtime methods.
- Playwright-compatible helpers exposed by the browser client must be backed by CDP primitives where practical, including locator actions, `getBy*` helpers, title, URL, and read-only evaluate helpers.
- `playwright.evaluate(fnOrExpression, arg?, options?)` is model-facing read-only page evaluation. It may read page state and compute bounded results, but must reject common navigation, DOM mutation, storage mutation, network-send, scroll, click, focus, and form side effects. Interaction side effects belong to locators, CUA, DOM-CUA, navigation, or wait helpers.
- When a Playwright-compatible helper cannot be implemented safely in IAB, the Browser skill must not claim it as supported.
- Ordinary page downloads, `waitForEvent("download")`, file chooser APIs, file upload, CUA media download, `browser.user.history()`, and complex content exports such as `tab_content_export` are not Desktop IAB capabilities and must fail with `UnsupportedApi` or the browser-use compatible unsupported behavior.
- `pageAssets.bundle()` is the only file-transfer download exception in M3. It uses the browser client's file-transfer prompt, Desktop IAB approval handling, and safe temp output; it does not expose ordinary browser downloads.
- WebMCP support is limited to tools explicitly exposed by the current page through `navigator.modelContext`. Desktop IAB must not synthesize tools from hidden browser state, extension storage, cookies, or local profile data.
- `domSnapshot()` returns a string payload owned by the bundled browser client. When the Desktop IAB implementation uses JSON content for that string, callers that need structured fields must parse it explicitly. JSON snapshots must order top-level fields as `title`, `url`, `bodyText`, `accessibilitySnapshot`, then `elements` so model-facing orientation data appears before full element arrays.
- `waitForLoadState` must observe the Desktop backend load state and must not be implemented as a client-side no-op.
- Page text, DOM snapshots, console logs, and evaluate results must be size-limited before crossing the REPL boundary.
- `ResultTooLarge` includes the configured limit and coarse size metadata when known, but never includes the oversized content.

Default serialized browser result cap: 1 MB unless `capabilities.browserUse.maxBrowserResultBytes` advertises a different lower cap.

---

## 11. Coordinate Input and DOM-CUA

Coordinate and DOM-CUA behavior must be independent of the legacy DOM snapshot path.

Rules:

- Coordinate actions require object-shaped finite coordinates such as `{ x: 940, y: 444 }`.
- Legacy positional calls such as `tab.cua.click(940, 444)` fail with `InvalidArgument` and a message showing the object-shaped form.
- Pointer actions should move the visible virtual cursor along a short path before click, double-click, drag, and scroll input is sent.
- Coordinate scroll should prefer CDP `Input.synthesizeScrollGesture` with mouse source, no fling, and deterministic speed after moving the visible virtual cursor. Electron wheel input may be used only as a fallback when CDP scroll is unavailable.
- CUA scroll treats `x`/`y` as viewport origin coordinates and `scrollX`/`scrollY` or `deltaX`/`deltaY` as scroll distance. Zero-distance CUA scroll must fail clearly instead of reporting success.
- DOM-CUA scroll without a `node_id` treats `x`/`y` as scroll distance and uses the viewport center as the gesture origin. DOM-CUA scroll with a `node_id` uses the node center as origin and accepts `x`/`y`, `scrollX`/`scrollY`, or `deltaX`/`deltaY` as distance aliases.
- Overlay injection failure must not block the underlying native or CDP input event.
- DOM-CUA visible node discovery should use CDP DOM, accessibility, and layout data rather than parsing a browser-client DOM snapshot string.
- DOM-CUA node ids are session-scoped and invalidated on navigation, reload, frame detach, and tab close.
- DOM-CUA actions resolve the current element box at action time and fail with `TabStale`, `NodeStale`, or `LocatorStrictModeViolation` when the target is no longer valid or ambiguous.

---

## 12. Timeouts, Cancellation, and Recovery

There are two timeout and cancellation levels:

| Level | Owner | Effect |
|-------|-------|--------|
| Evaluation timeout/cancel | Desktop Node REPL manager | Cancels the active evaluation and may reset the REPL context only when necessary. |
| Browser command timeout/cancel | Browser client/backend | Fails only the current browser promise and preserves thread REPL state. |

Rules:

- Browser commands carry a command id and the active `sessionId`, `turnId`, and `evaluationId`.
- Command timeouts are clamped to `1..120000` ms.
- Each tab has an ordered command queue for operations that cannot safely overlap on the same `webContents` or CDP session.
- `ext/nodeRepl/cancel` and outer evaluation timeout first cancel pending backend commands for the matching `evaluationId`.
- Late results for cancelled commands are ignored.
- CDP `message` and `detach` events from Electron debugger are forwarded as `onCDPEvent` notifications with `{ tabId, sessionId? }` source metadata; navigation and wait APIs must consume these real events instead of unconditional synthetic success.
- Recoverable browser command errors must not clear `globalThis.browser`, `globalThis.tab`, or unrelated user-defined globals.
- The REPL context may be rebuilt for explicit reset, VM creation failure, app shutdown, thread binding replacement, or unrecoverable JavaScript runtime corruption.

---

## 13. Security, Privacy, and Policy

Desktop browser policy remains authoritative.

Rules:

- External navigation follows approval, allowed-domain, and blocked-domain settings.
- Browser automation must not expose cookies, passwords, localStorage, IndexedDB, browser history, cache databases, or hidden profile paths through diagnostics or helper APIs.
- URL diagnostics should use safe summaries rather than full URLs when the URL may contain credentials, tokens, search params, or fragments.
- Page bodies, DOM text, request bodies, response bodies, and console payloads are never included in runtime diagnostics unless they are the explicit command result requested by the agent and pass result-size limits.
- Native pipe paths, process ids with nonces, extension ids, and local profile paths are forbidden in UI and ordinary logs.
- Ambient browser-client network checks are disabled by default; backend policy enforcement must not depend on client-side ambient network calls.

---

## 14. Error Categories and Diagnostics

Stable IAB error categories:

- `IabBackendUnavailable`
- `SessionMetadataMissing`
- `PolicyBlocked`
- `ApprovalDenied`
- `NavigationFailed`
- `CommandTimeout`
- `CommandCancelled`
- `ResultTooLarge`
- `DebuggerUnavailable`
- `UnsupportedApi`
- `InvalidArgument`
- `LocatorStrictModeViolation`
- `TabStale`
- `NodeStale`
- `PageClosed`

Client-visible errors must provide:

- stable `code`;
- short English fallback text;
- safe structured params when useful;
- no sensitive diagnostic fields listed in [Section 13](#13-security-privacy-and-policy).

Agent recovery guidance:

- `IabBackendUnavailable`: retry browser runtime setup in the current REPL context; if it repeats, ask the user to restart Desktop.
- `NavigationFailed`: inspect the safe error code and final URL summary before retrying; do not treat Chromium error pages as success.
- `CommandTimeout`: narrow the command or increase the specific command timeout; do not reset the REPL as the first recovery step.
- `ResultTooLarge`: filter page-side, request smaller content, or use chunking.
- `UnsupportedApi`: use the documented compatibility subset.
- `InvalidArgument`: fix the call shape before retrying.

---

## 15. Browser Skill Contract

The bundled Browser skill is part of the runtime contract because it teaches the model how to call the browser API.

Rules:

- The bundled plugin resource is the source of truth for Browser skill text; workspace-installed copies are derived artifacts.
- Skill examples must match the actual asynchronous API shape.
- The skill must document Node REPL output rules, including using `console.log` for text and `nodeRepl.emitImage` for images.
- The skill must document locator discipline, strict locator failures, CUA object-shaped coordinates, DOM-CUA behavior, screenshot output, and the supported Playwright-compatible subset.
- The skill must not describe APIs that are missing from the bundled browser client or unsupported by the IAB backend.

---

## 16. Acceptance

- Desktop declares `desktop-iab` to AppServer and exposes an internal browser-use backend id `iab` in Node REPL.
- The DotCraft browser client can initialize through `setupBrowserRuntime({ globals })`, discover the IAB backend through native pipe discovery, and install `agent.browsers`.
- `metadata.dotcraftSessionId` binds discovered IAB backends to the active DotCraft thread/session.
- Browser commands carry session and evaluation metadata and can be cancelled independently of the outer REPL request.
- A command timeout or navigation failure rejects only the current browser promise and preserves reusable REPL globals.
- Navigation failures, including Chromium error pages, return structured safe errors.
- Screenshot can run on empty or error pages without waiting for DOM snapshot readiness.
- DOM-CUA visible node discovery does not depend on the DOM snapshot string path.
- CUA rejects positional coordinate calls with a clear `InvalidArgument` error.
- Created and claimed tabs follow explicit finalize rules.
- Result-size limits are enforced before data crosses the REPL boundary.
- Viewer tabs show automation state and virtual cursor movement where possible without stealing user focus after the initial open.
- AppServer, Desktop main-process, browser backend transport, Node REPL, and Browser skill tests cover the runtime contract.
