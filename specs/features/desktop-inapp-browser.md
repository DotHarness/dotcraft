# DotCraft Desktop In-App Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 0.7.10 |
| **Status** | Living |
| **Date** | 2026-10-01 |
| **Parent Specs** | [Chrome Browser Runtime](chrome-browser-runtime.md) |

Purpose: define the behavior contract for DotCraft Desktop's embedded in-app browser automation runtime. The runtime is exposed to AppServer as `desktop-iab` and presents a browser-use compatible `iab` backend inside the thread-bound Node REPL.

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

## 2. Goals

1. **Browser-use compatible API**: DotCraft should load a DotCraft-owned browser client that preserves the documented browser-use compatible JavaScript shape and avoids maintaining a separate model-visible shim.
2. **Thread-bound isolation**: Browser sessions are isolated by thread/session metadata, while JavaScript bindings can survive across Node REPL evaluations.
3. **Recoverable command failures**: Navigation, locator, CDP, timeout, and unsupported-command errors fail the current browser promise, not the entire REPL runtime.
4. **Predictable tab ownership**: Created, claimed, kept, released, and closed tabs have explicit lifecycle rules.
5. **Independent command readiness**: Screenshots, DOM snapshots, locator waits, and coordinate actions use command-specific readiness instead of one global page-text gate.
6. **Observable automation**: Agent actions remain visible through viewer tabs, automation state, and a virtual cursor where practical.
7. **Safe diagnostics**: Errors are actionable without leaking page bodies, credentials, hidden browser storage, full pipe paths, or other sensitive data.

## 3. Architecture

The [Node REPL contract](node-repl.md) owns evaluation, lexical state, imports, task-process isolation and outer cancellation. Browser command failures reject only the command promise and preserve that environment.

The runtime has five layers:

1. **AppServer binding**
   - Binds browser automation to a client connection that declared both `capabilities.nodeRepl` and `capabilities.browserUse`.
   - Advertises the Desktop embedded backend as `desktop-iab`.
   - Forwards `threadId`, `turnId`, `evaluationId`, and `browserSession` to Desktop through `ext/nodeRepl/evaluate`.

2. **Desktop Node REPL manager**
   - Routes each bound thread to a lazy persistent utility process while the Desktop connection remains active.
   - Bridges host capabilities to the worker, which exposes `nodeRepl` and `dotcraft`.
   - Exposes `dotcraft.browserClientPath` and active `dotcraft.browserSession` so the browser client can initialize in the current turn.
   - Must not pre-install browser `agent`, `agent.browser`, or `agent.browsers` globals; the bundled browser client owns those model-facing APIs.
   - Preserves REPL state across recoverable browser command errors.

3. **DotCraft browser client**
   - Provides `setupBrowserRuntime()`.
   - Discovers browser backends through `nodeRepl.nativePipe`.
   - Returns an agent exposing `agent.browsers`, browser handles, tab handles, capabilities, and browser-use helper APIs.
   - Treats the Desktop in-app browser backend as `iab` inside the browser-use API.
   - Is maintained as DotCraft source code under `desktop/resources/browser/scripts/`.

4. **Desktop IAB backend server**
   - Runs in the Electron main process.
   - Listens on a local native pipe discovered by the browser client.
   - Speaks length-prefixed JSON-RPC.
   - Maps browser-use backend commands to viewer tabs, Electron `webContents`, CDP, Desktop browser policy, virtual cursor state, and diagnostics.

5. **Viewer browser surface**
   - Hosts in-app browser tabs as persistent DOM webview guests in a window-level renderer host. Main registers the guest WebContents and preserves the existing partition, controls and CDP ownership.
   - Tab creation waits for guest readiness, including hidden automation tabs. Switching tasks or hiding the panel changes presentation without removing guest nodes; explicit tab/window closure disposes them.
   - Renderer portals share the page composition hierarchy; ordinary menus never hide the page. Guest viewport dimensions remain available while automation runs in the background.
   - Shows automation state, session name, last action hints, and the virtual cursor described in [Section 12](#12-coordinate-input-virtual-cursor-and-dom-cua).
   - Keeps user focus stable after the initial agent-created tab open.

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

## 5. Node REPL Environment

Desktop must provide the browser client with the following globals:

| Global | Requirement |
|--------|-------------|
| `nodeRepl.nativePipe.createConnection(path)` | Opens a connection only to DotCraft-owned browser-use native pipes. |
| `nodeRepl.env` | Provides browser-client environment toggles. |
| `nodeRepl.tmpDir` | Points to the platform temp directory used for pipe discovery where applicable. |
| `nodeRepl.write(value)` | Writes explicit text output to the evaluation logs. |
| `nodeRepl.emitImage(image)` | Displays screenshots and other image outputs. |
| `nodeRepl.createElicitation(request)` | Routes browser-use confirmation prompts through Desktop approval UX when required. |
| `nodeRepl.fetch` | May be provided for browser-client compatibility, but ambient network checks should be disabled unless explicitly required. |
| `dotcraft.browserClientPath` | Absolute path to the bundled browser client entrypoint. |
| `dotcraft.browserSession` | Active browser session metadata for the current evaluation. |

`setupBrowserRuntime()` returns an agent for an explicit lexical binding. Neither bootstrap nor the host installs user `agent` or `browser` globals.

Node REPL evaluations for the same thread must be serialized by Desktop. Accidental overlapping browser cells should queue behind the active cell instead of failing with an "already running" error. Cancelling or resetting an active evaluation should also cancel queued evaluations that were waiting on the same stale browser state.

Default browser-client environment:

- `BROWSER_USE_AVAILABLE_BACKENDS=iab`
- `BROWSER_USE_DISABLE_AMBIENT_NETWORK=1`
- `BROWSER_USE_SECURITY_MODE=disabled-for-local-testing`

The browser client path should point to the DotCraft-owned browser client entrypoint under `desktop/resources/browser/scripts/`. Desktop must not rely on a separate model-visible API shim as the default runtime surface.

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
| `moveMouse` | Moves the virtual cursor only; it never dispatches page input. |
| `executeUnhandledCommand` | Handles backend-specific capability commands not implemented directly by the browser client. |

Tab ids exposed through backend primitives must be stable numeric ids scoped to the browser session. Desktop may keep existing viewer tab ids internally, but those ids must not leak as browser-use backend tab ids.

Target-scoped CDP sessions are available only when Electron debugger can attach the target and returns a concrete `sessionId`. Unsupported frame or OOPIF targets must fail with `UnsupportedApi` rather than silently succeeding or routing commands to the top-level page.

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
- Claimed user tabs release by default at finalization. Release removes agent registrations, debugger connections, listeners and page caches without destroying or reloading the Viewer page. All released pages remain discoverable for explicit re-acquisition.
- Stale guessed tab ids are rejected with `TabStale` or `InvalidArgument`.
- Only a turn that actually used browser commands triggers automatic cleanup on completion, failure, or cancellation. `tab.markDeliverable()` releases the live page from agent management at cleanup; it remains a user page. `tab.markHandoff()` keeps a temporary page under agent management until the next browser-using turn; the latest mark wins and is consumed at cleanup. Explicit `browser.tabs.finalize({ keep })` remains compatible early cleanup and writes equivalent turn marks. Evaluation completion does not clean up tabs.
- Keep entries must be typed as `handoff` or `deliverable` when typed finalize is advertised.
- Model-facing API descriptions and validation errors should show the typed form `finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })`.
- Temporary tabs used only for inspection should be closed explicitly with `tab.close()` or cleaned up through finalization; `browser.tabs.content({ urls })` is preferred for read-only temporary page fetches.
- `browser.tabs.content({ urls })` temporary pages are hidden implementation details. They must not emit renderer tab-open events, steal focus, affect first-tab focus bookkeeping, or remain in the visible tab strip.
- Visible automation tabs have paired renderer lifecycle events: a normal automation tab emits `viewer:browser:open` when exposed to the renderer and `viewer:browser:close` when closed by `tab.close()`, `browser.tabs.finalize()`, reset, or cleanup.

## 9. Navigation and Readiness

Navigation is command-scoped and must not depend on a successful DOM snapshot.

Rules:

- Local development hosts such as `localhost:3000`, `127.0.0.1:5173`, and `[::1]:8080` default to `http://` when no scheme is supplied.
- Navigation remains subject to Desktop browser policy, including external-domain approval, allowed domains, and blocked domains.
- Main-frame `did-fail-load` must become a structured navigation failure containing safe error code, safe description, requested URL summary, and final URL summary when available.
- Failed navigation must not make tab snapshots report the failed target URL as a successfully loaded page. Snapshots should prefer the actual `webContents` URL or an explicit error-page URL, with safe navigation-failure diagnostics when available.
- Chromium error pages such as `chrome-error://chromewebdata/` must not be reported as successful navigation to the requested site.
- Screenshot behavior is defined in [Section 11](#11-viewport-and-screenshots).
- DOM, locator, and accessibility commands use their own readiness and wait behavior.
- DOM snapshot readiness may proceed for an `interactive` or `complete` document with an existing `document.body`, even when text and interactable-element heuristics are temporarily empty.
- `waitForLoadState("domcontentloaded")` must complete for an already loaded tab when `document.readyState` is `interactive` or `complete` and `document.body` exists; readiness sampling must not depend on `requestAnimationFrame`, which can be throttled in hidden or unfocused tabs.
- A page-text-length heuristic must not be a global precondition for unrelated commands.

## 10. Page Data and API Compatibility

The model-visible Browser API is defined by the bundled browser client and the bundled Browser skill documentation.

Requirements:

- `agent.browsers`, browser handles, tab handles, locators, CUA, DOM-CUA, capabilities, and helper APIs are model-facing only after the bundled browser client returns them through `setupBrowserRuntime()`.
- Capability lookup examples must use the explicit handle shape: `const visibility = await browser.capabilities.get("visibility"); await visibility.set(true);`. Do not document chained `browser.capabilities.get(...).set(...)` or `tab.capabilities.get(...).list()` usage even though Desktop may tolerate it for compatibility.
- Unsupported APIs fail with `UnsupportedApi` and a stable English fallback message instead of being absent, hanging, or silently ignored.
- `browser.tabs.list()` and `browser.user.openTabs()` must return serializable `TabInfo` objects, not live tab handles. Callers must use `browser.tabs.get(info.id)` or `browser.user.claimTab(info)` before invoking tab methods.
- `browser.tabs.selected()` returns the active automation tab handle when one exists, and `undefined` when no tab is selected. It must not implicitly create a new tab.
- The supported reference-client subset includes `tabs.new/selected/list/get/content/finalize`, `browser.user.openTabs/claimTab`, browser `visibility` and `viewport` capabilities, `tab.goto/back/forward/reload/title/url`, screenshots, virtual clipboard `readText/writeText/read/write`, `playwright.evaluate(fnOrExpression, arg?, options?)`, `domSnapshot`, `waitForURL`, real `waitForLoadState`, `waitForTimeout`, `expectNavigation`, common locator reads and actions, `locator.all()` cached reads, `locator.filter()`, `locator.and()`, `locator.or()`, scoped `locator(selector, options)` filters, `getByRole/Text/Label/Placeholder/TestId`, same-origin `frameLocator`, coordinate CUA actions, DOM-CUA visible-node actions, `pageAssets.list/bundle`, and page-defined WebMCP tools through `tab.capabilities.get("webmcp")` only when the current page advertises them.
- `tabs.new(url?)` is a Desktop IAB compatibility extension. When a URL is supplied, it must trigger at most one backend navigation and must not be followed by a second client-side `goto(url)`.
- `tab.screenshot()` must use the dedicated backend screenshot command so screenshot failures have browser operation context. Generic `executeCdp(Page.captureScreenshot)` remains available only as a low-level backend primitive.
- `executeUnhandledCommand` handles only the commands the bundled browser client sends: tab marks, browser visibility and viewport, tabs content, dev logs, screenshots, clipboard, DOM snapshot, load-state waits, locator operations, DOM-CUA node info, and tab close, back, forward, and reload. `tab_content_export` and every other command type fail with `UnsupportedApi`.
- Playwright-compatible helpers exposed by the browser client must be backed by CDP primitives where practical, including locator actions, `getBy*` helpers, title, URL, and bounded evaluate helpers.
- `playwright.evaluate(fnOrExpression, arg?, options?)` is model-facing bounded page evaluation. It may read page state and compute bounded results, but must reject common navigation, DOM mutation, storage mutation, network-send, scroll, click, focus, and form side effects. Interaction side effects belong to locators, CUA, DOM-CUA, navigation, or wait helpers.
- When a Playwright-compatible helper cannot be implemented safely in IAB, the Browser skill must not claim it as supported.
- Agent download APIs including `waitForEvent("download")`, file chooser APIs, file upload, CUA media download, `browser.user.history()`, and complex content exports such as `tab_content_export` are not Desktop IAB automation capabilities and must fail with `UnsupportedApi` or the browser-use compatible unsupported behavior. This API restriction does not disable ordinary page-initiated user downloads.
- `pageAssets.bundle()` remains the supported automation file-transfer download path. It uses the browser client's file-transfer prompt, Desktop IAB approval handling, and safe temp output; it does not enable ordinary Agent download APIs.
- WebMCP support is a current-page capability limited to tools explicitly exposed through `navigator.modelContext`. `tab.capabilities.list()` must omit `webmcp` unless the current page exposes usable `getTools` and `executeTool` functions. Desktop IAB must not synthesize tools from hidden browser state, extension storage, cookies, or local profile data.
- `domSnapshot()` returns a string payload through the bundled browser client from the host's shared observation implementation. Snapshot and DOM-CUA identifiers refer to the same observed elements and are opaque to callers. Callers that need structured fields must parse JSON explicitly. JSON snapshots must order top-level fields as `title`, `url`, `bodyText`, `accessibilitySnapshot`, then `elements` so model-facing orientation data appears before full element arrays.
- `waitForLoadState` must observe the Desktop backend load state and must not be implemented as a client-side no-op.
- Page text, DOM snapshots, console logs, and evaluate results must be size-limited before crossing the REPL boundary.
- `ResultTooLarge` includes the configured limit and coarse size metadata when known, but never includes the oversized content.

Default serialized browser result cap: 1 MB unless `capabilities.browserUse.maxBrowserResultBytes` advertises a different lower cap.

## 11. Viewport and Screenshots

Viewport rules:

- A visible tab lays out at the panel's content size. A hidden tab keeps its last visible size; a tab that has never been visible uses 1280x720.
- `viewport.set({ width, height })` applies an explicit viewport to the session's selected tab, or its first controlled tab, through `Emulation.setDeviceMetricsOverride` with `deviceScaleFactor: 1` and `mobile: false`. Width is clamped to 240-4096 and height to 160-4096. `viewport.reset()` clears it with `Emulation.clearDeviceMetricsOverride`, returning the tab to its natural size. Both resolve after the emulation is applied.
- When the session controls no tab, `set` and `reset` are held and applied once to the next tab the session creates or controls. Other tabs keep natural sizing; turn end discards a held request.
- Releasing a tab clears its explicit viewport; deactivation keeps it.
- Emulation updates for a tab are serialized, and an update superseded by a newer one is skipped. Every CDP command on the tab waits for pending updates. Emulation is reapplied when the debugger attaches.
- The guest element of a tab with an explicit viewport has the explicit size. When visible, the page and its virtual cursor are scaled together by `min(1, availableWidth / width, availableHeight / height)`, where the available area is the panel content minus 40 px horizontally and 20 px vertically; the scaled page is centered horizontally with at least a 20 px gutter and anchored to the top.
- Agent-facing documentation describes the existing default viewport without fixed dimensions.

Screenshot rules:

- Screenshots may run on empty, loading, or error pages and must not require useful body text.
- Viewport screenshots never change the page's layout size. They capture `Page.getLayoutMetrics().cssVisualViewport` with `clip.scale` set to `1 / devicePixelRatio`, so image dimensions equal the CSS viewport used by coordinate input.
- Screenshots are JPEG at quality 80. Viewport screenshots first request a fresh screencast frame bounded by the CSS viewport for up to two seconds, then fall back to `Page.captureScreenshot` for up to five seconds within the overall operation deadline. Frames predating the request are acknowledged but never returned.
- A visible tab in a shown, unminimized window is captured in place. Any other tab is presented on a capture surface at its current layout size so it paints; the surface stays mounted above the application with opacity `0.001`, no pointer interaction, and a fit-to-window scale, and never changes panel visibility or focus.
- Full-page and cropped screenshots, including low-level `Page.captureScreenshot` calls with `captureBeyondViewport` and a clip, use a capture surface sized exactly to the clip (`ceil(width) × ceil(height)`). Layout settling polls for at most one second and the surface is removed on every terminal path.
- Visible pages and active automation disable background throttling; idle hidden pages restore it. A capture surface temporarily disables owner-renderer throttling and restores its previous value after the last surface is released.
- Screenshot stages report bounded metadata (stage, elapsed time, dimensions, throttling, and outcome), never image data or page content. Cancellation, timeout, debugger detachment, and guest closure release listeners, internal screencasts, and temporary surfaces; late command results cannot update a later operation.
- Screenshot commands serialize per guest. An existing raw CDP screencast prevents an internal screencast from starting; internal screencast events are not exposed to raw CDP subscribers.

## 12. Coordinate Input, Virtual Cursor, and DOM-CUA

Coordinate and DOM-CUA behavior must be independent of the DOM snapshot path.

Virtual cursor rules:

- Backend commands that act on a tab mark it automation-active. Turn cleanup, release, retention, and cancellation deactivate it.
- The viewer renders the cursor in a non-interactive overlay above the guest page. It is never part of the page DOM, page screenshots, or DOM reads.
- The cursor is shown only while its tab is automation-active and visible. Deactivation hides it; release clears its position. Activation without a move rests the cursor at 58% of the viewport width and 55% of its height.
- `moveMouse` assigns each move an increasing sequence number. When the owner window is focused and the tab is visible, the move animates and the backend waits for the renderer's arrival acknowledgement for that sequence, bounded by 1500 ms; otherwise the cursor snaps and the call returns immediately. `waitForArrival: false` never waits. Late acknowledgements for older sequences are ignored, and a timeout never fails the move.
- Moves animate with springs: long moves follow a curved path and short moves glide directly. Activation and the first move after the cursor was hidden show it at the target immediately; hiding fades it out.

Input rules:

- Coordinate actions require object-shaped finite coordinates such as `{ x: 940, y: 444 }` in CSS pixels of the page's current viewport, the same space as viewport screenshots.
- Positional calls such as `tab.cua.click(940, 444)` fail with `InvalidArgument` and a message showing the object-shaped form.
- Page input is dispatched only through CDP `Input.dispatchMouseEvent`, `Input.dispatchKeyEvent`, and `Input.insertText`; other `Input.*` methods fail with `UnsupportedApi`. Before each input command the backend waits for pending viewport emulation on the tab and enables `Emulation.setFocusEmulationEnabled`.
- Pointer sequences:
  - move: cursor move, then `mouseMoved` with no buttons;
  - click: cursor move, `mouseMoved`, then for each count `n` from 1 to `clickCount` a `mousePressed` and `mouseReleased` pair with `clickCount: n` and the button mask, so a double click sends two pairs. If the page starts loading within 250 ms, the click waits for `DOMContentLoaded` or `load`, bounded by 3 seconds, and a timeout does not fail the click. A blocked navigation fails it;
  - drag: cursor move and `mouseMoved` to the start, `mousePressed`, then for each later point a non-waiting cursor move and `mouseMoved` with the button held, then `mouseReleased`; a failure releases the button best-effort;
  - scroll: cursor move, `mouseMoved`, then `mouseWheel` at the origin with the distance as `deltaX`/`deltaY`.
- Typing text dispatches a synthetic paste of the text to the focused element. When the page does not cancel the paste, the text is inserted into the focused input, textarea, or editable element.
- Key presses parse `+`-joined chords with a US keyboard layout. Keys go down in order and up in reverse, carrying `key`, `code`, `windowsVirtualKeyCode`, `location`, and modifier flags; a key held with modifiers other than Shift is sent as `rawKeyDown` without `text`. Copy, cut, and paste chords act on the virtual clipboard; other native clipboard shortcuts fail.
- Copy and cut from credential-like fields copy nothing and delete nothing; these are input, textarea, or select elements that are hidden or whose `type`, `autocomplete`, `id`, `name`, `placeholder`, `aria-label`, or `title` names a username, email, one-time code, password, OTP, 2FA/MFA, or phone field. Copied markup drops the `value` of such inputs.
- Locator clicks require a visible, enabled target, scroll it into view, wait for a stable box, then use the click sequence. Locator `fill`, `type`, and `press` focus their target through the DOM without moving the cursor; `fill` and `type` then use the paste path and `press` the key sequence.
- CUA scroll treats `x`/`y` as viewport origin coordinates and `scrollX`/`scrollY` or `deltaX`/`deltaY` as scroll distance. Zero-distance CUA scroll must fail clearly instead of reporting success.
- DOM-CUA scroll without a `node_id` treats `x`/`y` as scroll distance and uses the viewport center as the gesture origin. DOM-CUA scroll with a `node_id` uses the node center as origin and accepts `x`/`y`, `scrollX`/`scrollY`, or `deltaX`/`deltaY` as distance aliases.
- Cursor failures never fail an action, and cursor waits never exceed the arrival bound.
- DOM-CUA visible node discovery should use CDP DOM, accessibility, and layout data rather than parsing a browser-client DOM snapshot string.
- DOM-CUA node ids are session-scoped and invalidated on navigation, reload, frame detach, and tab close.
- DOM-CUA actions resolve the current element box at action time and fail with `TabStale`, `NodeStale`, or `LocatorStrictModeViolation` when the target is no longer valid or ambiguous.

## 13. Timeouts, Cancellation, and Recovery

There are two timeout and cancellation levels:

| Level | Owner | Effect |
|-------|-------|--------|
| Evaluation timeout/cancel | Desktop Node REPL manager | Cancels backend commands, then terminates the task process under the Node REPL contract. |
| Browser command timeout/cancel | Browser client/backend | Fails only the current browser promise and preserves thread REPL state. |

Rules:

- Browser commands carry a command id and the active `sessionId`, `turnId`, and `evaluationId`.
- Command timeouts are clamped to `1..120000` ms.
- `CommandTimeout` errors should include safe structured data such as operation, command type, CDP method, tab id, and current URL summary when available.
- Each tab has an ordered command queue for operations that cannot safely overlap on the same `webContents` or CDP session.
- `ext/nodeRepl/cancel` and outer evaluation timeout first cancel pending backend commands for the matching `evaluationId`.
- Late results for cancelled commands are ignored.
- CDP `message` and `detach` events from Electron debugger are forwarded as `onCDPEvent` notifications with `{ tabId, sessionId? }` source metadata; navigation and wait APIs must consume these real events instead of unconditional synthetic success.
- Recoverable browser command errors must not clear `browser`, `tab`, or unrelated user-defined globals.
- Outer process ownership and recovery follow [Node REPL](node-repl.md#process-ownership-and-cancellation).

## 14. Security, Privacy, and Policy

Desktop browser policy remains authoritative.

Rules:

- External navigation follows approval, allowed-domain, and blocked-domain settings.
- Browser automation must not expose cookies, passwords, localStorage, IndexedDB, browser history, cache databases, or hidden profile paths through diagnostics or helper APIs.
- URL diagnostics should use safe summaries rather than full URLs when the URL may contain credentials, tokens, search params, or fragments.
- Page bodies, DOM text, request bodies, response bodies, and console payloads are never included in runtime diagnostics unless they are the explicit command result requested by the agent and pass result-size limits.
- Native pipe paths, process ids with nonces, extension ids, and local profile paths are forbidden in UI and ordinary logs.
- Ambient browser-client network checks are disabled by default; backend policy enforcement must not depend on client-side ambient network calls.

## 15. Error Categories and Diagnostics

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
- no sensitive diagnostic fields listed in [Section 14](#14-security-privacy-and-policy).

Native-pipe browser clients must preserve backend error `code` and safe `data` on the JavaScript `Error` object so callers can inspect fields such as navigation `validatedURL`, `finalURL`, and safe error descriptions.

Agent recovery guidance:

- `IabBackendUnavailable`: retry browser runtime setup in the current REPL context; if it repeats, ask the user to restart Desktop.
- `NavigationFailed`: inspect the safe error code and final URL summary before retrying; do not treat Chromium error pages as success.
- `CommandTimeout`: narrow the command or increase the specific command timeout; do not reset the REPL as the first recovery step.
- `ResultTooLarge`: filter page-side, request smaller content, or use chunking.
- `UnsupportedApi`: use the documented compatibility subset.
- `InvalidArgument`: fix the call shape before retrying.

## 16. Browser Skill Contract

The bundled Browser skill and the runtime documentation returned by `browser.documentation()` are part of the runtime contract because they teach the model how to call the browser API.

Rules:

- The bundled plugin resource and `desktop/resources/browser/scripts/browser-documentation.mjs` are the sources of truth; workspace-installed copies are derived artifacts.
- The skill covers surface choice, user-facing progress wording, bootstrap through `dotcraft.browserClientPath`, the `NodeReplJs` tool and the `iab` browser id, Node REPL output through `nodeRepl.write` and `nodeRepl.emitImage`, persistent bindings and stale-tab recovery. It states that `agent` is returned by the bundled browser client and requires reading the complete runtime documentation before the first browser action.
- The complete runtime documentation contains the supported API (tabs, the Playwright-compatible subset, DOM-CUA, and coordinate actions), operating guidance (observation, locator discipline, strict locator failures, search-result narrowing, bounded evaluate, tab reuse, temporary-tab cleanup, search and URL fallback limits, and stopping repeated verification once an authoritative page signal is present), visibility guidance, browser safety, and the browser confirmation policy for data transmission, account and permission changes, uploads, messages, purchases, browser permission prompts, and actions that require user hand-off.
- Topic guidance for screenshots, viewport, page assets, and WebMCP is listed in the documentation with when to read it and is returned by `agent.documentation.get(name)`. Topics are filtered by the backend's capabilities.
- Examples must match the actual asynchronous API shape. `tab.playwright` is described as a supported subset; methods absent from the bundled client, such as `locator.evaluate()`, are not encouraged.
- The skill and documentation must not describe APIs missing from the bundled client or unsupported by the backend. Plugin-root imports from other runtimes, alternate browser-control fallbacks, ordinary downloads, file choosers, uploads, raw CDP capability, and browsing history are not documented as capabilities.
- Model-facing text omits implementation details the model cannot act on, such as internal ids, process and reset mechanics, backend command names, and internal product names.
