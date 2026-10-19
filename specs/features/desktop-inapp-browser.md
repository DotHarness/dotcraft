# DotCraft Desktop In-App Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 0.9.1 |
| **Status** | Living |
| **Date** | 2026-10-19 |
| **Parent Specs** | [Chrome Browser Runtime](chrome-browser-runtime.md) |

Purpose: define the behavior contract for DotCraft Desktop's embedded in-app browser automation runtime. The runtime is exposed to AppServer as `desktop-iab` and presents a browser-use compatible `iab` backend inside the thread-bound Node REPL.

## 1. Scope

This spec covers:

- Desktop embedded browser automation exposed through `NodeReplJs`.
- Browser client loading, native pipe backend discovery, framed JSON-RPC transport, command lifecycle, cancellation, diagnostics, and cleanup semantics.
- Tab identity, ownership, navigation, screenshots, DOM access, Playwright-compatible helpers, coordinate input, DOM-CUA, and capability behavior.
- Viewer behavior that makes agent browser actions observable without stealing unrelated user focus.

This spec does not define:

- General user browsing UX unrelated to agent automation.
- Chrome extension or native host setup; see [Chrome Browser Runtime](chrome-browser-runtime.md).
- Automation against hidden profile data such as cookies, passwords, local storage, browser history, or cache databases.
- A model-visible browser API beyond the documented browser-use compatibility subset.

## 2. Goals

1. **Browser-use compatible API**: a DotCraft-owned browser client preserves the documented browser-use JavaScript shape; there is no separate model-visible shim.
2. **Thread-bound isolation**: browser sessions are isolated by thread/session metadata, while JavaScript bindings survive across Node REPL evaluations.
3. **Recoverable command failures**: navigation, locator, CDP, timeout, and unsupported-command errors fail the current browser promise, not the REPL runtime.
4. **Predictable tab ownership**: created, claimed, kept, released, and closed tabs have explicit lifecycle rules.
5. **Independent command readiness**: screenshots, DOM snapshots, locator waits, and coordinate actions use command-specific readiness instead of one global page-text gate.
6. **Observable automation**: agent actions remain visible through viewer tabs, automation state, and a virtual cursor.
7. **Safe diagnostics**: errors are actionable without leaking page bodies, credentials, hidden browser storage, pipe paths, or other sensitive data.

## 3. Architecture

The [Node REPL contract](node-repl.md) owns evaluation, lexical state, imports, task-process isolation and outer cancellation. Browser command failures reject only the command promise and preserve that environment.

The runtime has five layers:

1. **AppServer binding**: binds browser automation to a client connection that declared both `capabilities.nodeRepl` and `capabilities.browserUse`, advertises the backend as `desktop-iab`, and forwards `threadId`, `turnId`, `evaluationId`, and `browserSession` through `ext/nodeRepl/evaluate`.
2. **Desktop Node REPL manager**: routes each bound thread to a lazy persistent utility process while the Desktop connection is active, exposes `nodeRepl` and `dotcraft` (including `dotcraft.browserClientPath` and the active `dotcraft.browserSession`), and preserves REPL state across recoverable browser errors. It never pre-installs `agent`, `agent.browser`, `agent.browsers`, or `browser` globals; the browser client owns those APIs.
3. **DotCraft browser client**: provides `setupBrowserRuntime()`, discovers backends through `nodeRepl.nativePipe`, and returns an agent exposing `agent.browsers`, browser and tab handles, capabilities, and browser-use helpers. The Desktop backend appears as `iab`.
4. **Desktop IAB backend server**: runs in the Desktop main process, listens on a local native pipe, speaks length-prefixed JSON-RPC, and maps backend commands to viewer tabs, page CDP sessions, Desktop browser policy, virtual cursor state, and diagnostics.
5. **Viewer browser surface**: hosts in-app browser tabs as persistent page guests. Tab creation waits for guest readiness, including hidden automation tabs. Switching tasks or hiding the panel changes presentation without destroying pages; explicit tab or window closure disposes them. Ordinary menus never hide the page, and page viewport dimensions remain available while automation runs in the background. The viewer shows automation state, session name, last action hints, and the virtual cursor (§12), and keeps user focus stable after the initial agent-created tab open.

## 4. AppServer and Session Metadata

Desktop identifies the embedded backend to AppServer as `desktop-iab`:

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

- `browserUse.backend` is the AppServer-visible backend id; inside Node REPL the backend id is `iab`.
- `browserSession.sessionId` is the isolation key and normally equals the thread id. `turnId` is forwarded when known.
- `evaluationId` changes for every Node REPL evaluation and drives command cancellation and late-result suppression.
- The backend `getInfo` result includes `metadata.dotcraftSessionId` equal to the active `browserSession.sessionId`, so the browser client selects the current session's backend.
- Missing `sessionId` or `evaluationId` fails backend commands with `SessionMetadataMissing`.
- Unknown `browserUse` capability fields are optional and forward-compatible.

## 5. Node REPL Environment

Desktop provides the browser client with:

| Global | Requirement |
|--------|-------------|
| `nodeRepl.nativePipe.createConnection(path)` | Opens a connection only to DotCraft-owned browser-use native pipes. |
| `nodeRepl.env` | Browser-client environment toggles. |
| `nodeRepl.tmpDir` | Platform temp directory used for pipe discovery. |
| `nodeRepl.write(value)` | Writes explicit text output to the evaluation logs. |
| `nodeRepl.emitImage(image)` | Displays screenshots and other images. |
| `nodeRepl.createElicitation(request)` | Routes browser-use confirmation prompts through Desktop approval UX. |
| `nodeRepl.fetch` | Optional, for browser-client compatibility; ambient network checks stay disabled. |
| `dotcraft.browserClientPath` | Absolute path to the bundled browser client entrypoint. |
| `dotcraft.browserSession` | Active browser session metadata for the current evaluation. |

`setupBrowserRuntime()` returns an agent for an explicit lexical binding.

Desktop serializes Node REPL evaluations per thread: an overlapping browser cell queues behind the active one instead of failing. Cancelling or resetting an active evaluation also cancels queued evaluations waiting on the same browser state.

Default browser-client environment:

- `BROWSER_USE_AVAILABLE_BACKENDS=iab`
- `BROWSER_USE_DISABLE_AMBIENT_NETWORK=1`
- `BROWSER_USE_SECURITY_MODE=disabled-for-local-testing`

## 6. Native Pipe Transport

The backend listens on one local pipe per Desktop process:

| Platform | Address Shape |
|----------|---------------|
| Windows | `\\.\pipe\dotcraft-browser-use-dotcraft-<pid>-<nonce>` |
| macOS/Linux | `<tempdir>/dotcraft-browser-use/dotcraft-<pid>-<nonce>.sock` |

Rules:

- The browser client discovers the address; on macOS/Linux it scans `<tempdir>/dotcraft-browser-use/` for matching sockets.
- Connections use 4-byte little-endian length-prefixed frames carrying JSON-RPC 2.0 requests, responses, and notifications. There is no newline-delimited JSON or TCP port.
- `nodeRepl.nativePipe.createConnection` rejects paths outside the DotCraft-owned browser-use pipe namespace.
- Pipe paths never appear in diagnostics or UI.

Example exchange:

```json
{ "jsonrpc": "2.0", "id": 1, "method": "getInfo",
  "params": { "session_id": "thread_abc", "turn_id": "turn_123" } }
```

```json
{ "jsonrpc": "2.0", "id": 1,
  "result": { "id": "iab", "name": "DotCraft In-App Browser",
              "metadata": { "dotcraftSessionId": "thread_abc" } } }
```

## 7. Backend Primitives

The backend implements the primitives the browser-use client expects. Public JavaScript APIs are owned by the browser client; primitives are not model-visible tools.

| Method | Requirement |
|--------|-------------|
| `ping` | Health check for discovery and reconnect. |
| `getInfo` | Returns backend id `iab`, protocol metadata, safe capabilities, and `metadata.dotcraftSessionId`. |
| `getTabs` | Lists tabs owned by the current session. |
| `getUserTabs` | Lists claimable user tabs with safe title, URL, and recency metadata only. |
| `getUserHistory` | Always fails with `UnsupportedApi`; Desktop never reads browser history. |
| `claimUserTab` | Adopts a tab from the latest `getUserTabs` result for the same session. |
| `createTab` | Creates a viewer browser tab and returns a backend tab id. |
| `finalizeTabs` | Applies typed keep, release, and close rules. |
| `nameSession` | Updates viewer automation session labels. |
| `attach` / `detach` | Manages top-level CDP attachment for a tab. |
| `attachTarget` / `detachTarget` | Manages target-scoped CDP sessions for frames and related targets. |
| `executeCdp` | Runs a CDP command with timeout, cancellation, and result-size limits. |
| `moveMouse` | Moves the virtual cursor only; never dispatches page input. |
| `executeUnhandledCommand` | Handles backend-specific commands the browser client does not implement directly (§10). |

Backend tab ids are stable numeric ids scoped to the browser session, independent of viewer tab ids.

Target-scoped CDP sessions exist only when the debugger can attach the target and returns a concrete `sessionId`; otherwise frame or OOPIF targets fail with `UnsupportedApi` rather than silently routing to the top-level page. Out-of-process frames are auto-attached as their own execution targets. A cross-origin frame that shares its parent's process is reached through an isolated world in the parent's session, recreated once if it no longer exists.

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

- Agent-created tabs are regular viewer tabs. Creating a session's first tab may focus it; later automation never steals focus.
- At finalization, agent-created tabs close and claimed tabs release by default. Release removes agent registrations, debugger connections, listeners, and page caches without destroying or reloading the page, which stays discoverable for re-acquisition.
- Stale or guessed tab ids are rejected with `TabStale` or `InvalidArgument`.
- Only a turn that used browser commands triggers automatic cleanup on completion, failure, or cancellation; evaluation completion does not. `tab.markDeliverable()` releases the page from agent management at cleanup, leaving it a user page. `tab.markHandoff()` keeps a temporary page under agent management until the next browser-using turn. The latest mark wins and is consumed at cleanup.
- `browser.tabs.finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })` performs early cleanup and writes equivalent marks. Keep entries must be typed when typed finalize is advertised.
- Temporary inspection tabs are closed with `tab.close()` or by finalization. `browser.tabs.content({ urls })` is preferred for read-only fetches; its temporary pages are hidden: they never appear in the tab strip, steal focus, or affect first-tab focus.
- Visible automation tabs appear in the viewer when exposed and disappear when closed by `tab.close()`, finalize, reset, or cleanup.

## 9. Navigation and Readiness

Navigation is command-scoped and never depends on a successful DOM snapshot.

- Local development hosts such as `localhost:3000`, `127.0.0.1:5173`, and `[::1]:8080` default to `http://` when no scheme is supplied.
- Navigation is subject to Desktop browser policy: external-domain approval, allowed domains, and blocked domains.
- A main-frame load failure becomes a structured navigation failure with safe error code, description, and requested and final URL summaries.
- After a failed navigation, snapshots report the actual page URL or error-page URL, never the failed target as loaded. Chromium error pages are never reported as successful navigation.
- DOM, locator, and accessibility commands use their own readiness. DOM snapshots may proceed for an `interactive` or `complete` document with a body, even when text and interactable-element heuristics are empty.
- `waitForLoadState("domcontentloaded")` completes for an already loaded tab when `document.readyState` is `interactive` or `complete` and a body exists; readiness sampling does not depend on animation frames, which hidden tabs throttle.
- No page-text heuristic is a global precondition for unrelated commands.

## 10. Page Data and API Compatibility

The model-visible Browser API is defined by the bundled browser client and the bundled Browser skill documentation (§16).

API shape:

- `agent.browsers`, browser and tab handles, locators, CUA, DOM-CUA, capabilities, and helpers are model-facing only once `setupBrowserRuntime()` returns them.
- Capabilities use explicit handles: `const visibility = await browser.capabilities.get("visibility"); await visibility.set(true);`.
- Unsupported APIs fail with `UnsupportedApi` and a stable English fallback message instead of being absent, hanging, or silently ignored.
- `browser.tabs.list()` and `browser.user.openTabs()` return serializable `TabInfo` objects; callers use `browser.tabs.get(info.id)` or `browser.user.claimTab(info)` to obtain handles.
- `browser.tabs.selected()` returns the active automation tab handle or `undefined`; it never creates a tab.
- `tabs.new(url?)` is a Desktop IAB extension; with a URL it performs exactly one backend navigation.
- `tab.screenshot()` uses the dedicated backend screenshot command; raw `Page.captureScreenshot` remains a low-level primitive.
- `waitForLoadState` observes the backend load state; it is never a client-side no-op. Navigation and wait APIs consume real CDP events.

Supported subset: `tabs.new/selected/list/get/content/finalize`, `browser.user.openTabs/claimTab`, browser `visibility` and `viewport` capabilities, `tab.goto/back/forward/reload/title/url`, screenshots, virtual clipboard `readText/writeText/read/write`, `playwright.evaluate(fnOrExpression, arg?, options?)`, `domSnapshot`, `waitForURL`, `waitForLoadState`, `waitForTimeout`, `expectNavigation`, common locator reads and actions, cached `locator.all()`, `locator.filter/and/or`, scoped `locator(selector, options)`, `getByRole/Text/Label/Placeholder/TestId`, `frameLocator` for same-origin, cross-origin, and out-of-process frames, coordinate CUA, DOM-CUA, `pageAssets.list/bundle`, and page-defined WebMCP tools through `tab.capabilities.get("webmcp")` when the current page advertises them. Playwright-compatible helpers are backed by CDP primitives; a helper that cannot be implemented safely is not claimed as supported.

`executeUnhandledCommand` handles only commands the bundled client sends: tab marks, browser visibility and viewport, tabs content, dev logs, screenshots, clipboard, DOM snapshot, load-state waits, locator operations, DOM-CUA node info, focused-frame evaluation, and tab close/back/forward/reload. Every other command type, including `tab_content_export`, fails with `UnsupportedApi`.

Restricted capabilities:

- `playwright.evaluate` is bounded read-only evaluation. It rejects common navigation, DOM mutation, storage mutation, network-send, scroll, click, focus, and form side effects; interaction belongs to locators, CUA, DOM-CUA, navigation, or waits.
- Agent downloads (`waitForEvent("download")`, CUA media download), file choosers, uploads, `browser.user.history()`, and complex content exports fail with `UnsupportedApi`. Ordinary page-initiated user downloads still work. `pageAssets.bundle()` is the supported automation file-transfer path, using the client's file-transfer prompt, Desktop approval, and safe temp output.
- WebMCP is limited to tools the current page exposes through `navigator.modelContext`. `tab.capabilities.list()` omits `webmcp` unless the page exposes usable `getTools` and `executeTool`. Tools are never synthesized from hidden browser state, extension storage, cookies, or profile data.

Snapshots and frames:

- `domSnapshot()` returns a string from the host's shared observation implementation; callers parse JSON explicitly. JSON snapshots order top-level fields `title`, `url`, `bodyText`, `accessibilitySnapshot`, `elements`. Snapshot and DOM-CUA identifiers refer to the same observed elements and are opaque.
- Snapshots, locators, and DOM-CUA reach every frame. A frame the page cannot enter is read through its frame execution target: its elements follow the parent's, its accessibility tree appears as an indented `- iframe "<name or src>" [frameSelectors=<JSON array>]:` entry, and its body text is appended to `bodyText` within the 4000-character bound. An unreadable frame is recorded as `... : unavailable (<reason>)` and never fails the snapshot. Snapshots read at most 200 elements and 16 such frames.
- Every element inside a frame carries `frameSelectors`, the owner frame selectors from the top document inward, usable in order with chained `frameLocator()` calls. Top-document elements omit the field.
- `frameLocator` scopes resolve in the page until reaching a frame the page cannot enter, then continue inside that frame's execution target.
- `playwright.evaluate` and `tabs.content` read only the top document.
- Bounding boxes and locator click points from any frame are top-level viewport CSS pixels, mapped through each frame owner's current content quad when the snapshot or action completes, so moved, scrolled, and transformed frames map correctly.
- Virtual clipboard copy, cut, and paste act in the focused frame, following the active element through frames the page cannot enter.

Result limits: page text, DOM snapshots, console logs, and evaluate results are size-limited before crossing the REPL boundary, by default 1 MB unless `capabilities.browserUse.maxBrowserResultBytes` advertises a lower cap. `ResultTooLarge` includes the limit and coarse size metadata when known, never the content.

## 11. Viewport and Screenshots

Viewport rules:

- A visible tab lays out at the panel's content size. A hidden tab keeps its last visible size; a never-visible tab uses 1280x720.
- `viewport.set({ width, height })` applies an explicit viewport (scale factor 1, non-mobile) to the session's selected tab, or its first controlled tab. Width is clamped to 240-4096 and height to 160-4096. `viewport.reset()` returns the tab to its natural size. Both resolve after the emulation is applied.
- When the session controls no tab, `set` and `reset` are held and applied once to the next tab the session creates or controls; turn end discards a held request.
- Releasing a tab clears its explicit viewport; deactivation keeps it.
- Emulation updates per tab are serialized, superseded updates are skipped, every CDP command waits for pending updates, and emulation is reapplied when the debugger attaches.
- A visible tab with an explicit viewport is scaled down, together with its virtual cursor, to fit the panel.
- Agent-facing documentation describes the default viewport without fixed dimensions.

Screenshot rules:

- Screenshots work on empty, loading, or error pages and never require body text.
- Viewport screenshots never change the page's layout size; image dimensions equal the CSS viewport used by coordinate input.
- Screenshots are JPEG at quality 80 and only return frames produced after the request, within the operation deadline.
- A visible tab in a shown, unminimized window is captured in place. Any other tab is painted on an invisible, non-interactive capture surface at its current layout size, without changing panel visibility or focus.
- Full-page and cropped screenshots, including low-level `Page.captureScreenshot` with `captureBeyondViewport` and a clip, use a capture surface no smaller than the current layout size, so a small crop does not reflow the page.
- Visible pages and active automation are not background-throttled.
- Screenshot diagnostics carry bounded metadata (stage, elapsed time, dimensions, throttling, outcome), never image data or page content. Cancellation, timeout, debugger detachment, and page closure release all temporary resources; late results cannot affect a later operation.
- Screenshot commands serialize per page. A raw CDP screencast prevents an internal one from starting, and internal screencast events are never exposed to raw CDP subscribers.

## 12. Coordinate Input, Virtual Cursor, and DOM-CUA

Coordinate and DOM-CUA behavior is independent of the DOM snapshot path.

Virtual cursor rules:

- Backend commands that act on a tab mark it automation-active. Turn cleanup, release, retention, and cancellation deactivate it.
- The viewer renders the cursor in a non-interactive overlay above the page. It is never part of the page DOM, page screenshots, or DOM reads.
- The cursor is shown only while its tab is automation-active and visible. Deactivation hides it; release clears its position.
- `moveMouse` assigns each move an increasing sequence number. When the owner window is focused and the tab is visible, the move animates and the backend waits, bounded by 1500 ms, for the renderer's arrival acknowledgement for that sequence; otherwise the cursor snaps and the call returns immediately. `waitForArrival: false` never waits. Late acknowledgements for older sequences are ignored.
- Cursor failures never fail an action, and a cursor wait never exceeds the arrival bound.

Input rules:

- Coordinate actions require object-shaped finite coordinates such as `{ x: 940, y: 444 }` in CSS pixels of the current viewport, the same space as viewport screenshots. Positional calls such as `tab.cua.click(940, 444)` fail with `InvalidArgument` and a message showing the object form.
- Page input is dispatched only through CDP `Input.dispatchMouseEvent`, `Input.dispatchKeyEvent`, and `Input.insertText`; other `Input.*` methods fail with `UnsupportedApi`. Input waits for pending viewport emulation and runs with focus emulation enabled.
- Pointer sequences:
  - move: cursor move, then a button-less mouse move;
  - click: cursor move, mouse move, then one press/release pair per click count with increasing `clickCount`. If the page starts loading within 250 ms, the click waits up to 3 seconds for `DOMContentLoaded` or `load` without failing on timeout; a blocked navigation fails it;
  - drag: move and press at the start, a held-button move per later point, then release; a failure releases the button best-effort;
  - scroll: cursor move, mouse move, then a wheel event at the origin with the distance as `deltaX`/`deltaY`.
- Typing dispatches a synthetic paste to the focused element; when the page does not cancel it, the text is inserted into the focused input, textarea, or editable element.
- Key presses parse `+`-joined chords with a US keyboard layout; keys go down in order and up in reverse with full key metadata, and a key held with non-Shift modifiers produces no text. Copy, cut, and paste chords act on the virtual clipboard; other native clipboard shortcuts fail.
- Copy and cut from credential-like fields copy and delete nothing: input, textarea, or select elements that are hidden or whose `type`, `autocomplete`, `id`, `name`, `placeholder`, `aria-label`, or `title` names a username, email, one-time code, password, OTP, 2FA/MFA, or phone field. Copied markup drops the `value` of such inputs.
- Locator clicks require a visible, enabled target, scroll it into view, wait for a stable box, then use the click sequence. Locator `fill`, `type`, and `press` focus their target through the DOM without moving the cursor; `fill` and `type` then use the paste path and `press` the key sequence.
- CUA scroll treats `x`/`y` as viewport origin and `scrollX`/`scrollY` or `deltaX`/`deltaY` as distance. Zero-distance CUA scroll fails clearly.
- DOM-CUA scroll without a `node_id` treats `x`/`y` as distance from the viewport center. With a `node_id`, the node center is the origin and `x`/`y`, `scrollX`/`scrollY`, or `deltaX`/`deltaY` are distance aliases.

DOM-CUA node rules:

- Visible node discovery uses CDP DOM, accessibility, and layout data, not a parsed snapshot string.
- Node ids are numeric strings from a per-tab counter, never reused within a tab, and bound to their element's document (frame and loader), so the same element in the same document keeps its id across snapshots. An id fails with `NodeStale` when its element is absent from the latest snapshot, its document changed, or the element is detached. Remembered ids are dropped on top-level document change, beyond 5000 remembered ids, and on tab close.
- Actions resolve the current element box at action time and fail with `TabStale`, `NodeStale`, or `LocatorStrictModeViolation` when the target is invalid or ambiguous.

## 13. Timeouts, Cancellation, and Recovery

| Level | Owner | Effect |
|-------|-------|--------|
| Evaluation timeout/cancel | Desktop Node REPL manager | Cancels backend commands, then terminates the task process under the Node REPL contract. |
| Browser command timeout/cancel | Browser client/backend | Fails only the current browser promise and preserves thread REPL state. |

Rules:

- Browser commands carry a command id and the active `sessionId`, `turnId`, and `evaluationId`. Timeouts are clamped to `1..120000` ms.
- `CommandTimeout` errors include safe structured data such as operation, command type, CDP method, tab id, and URL summary when available.
- Operations that cannot safely overlap on one page or CDP session run through an ordered per-tab queue.
- `ext/nodeRepl/cancel` and outer evaluation timeout first cancel pending backend commands for the matching `evaluationId`; late results are ignored.
- Debugger CDP events and detachment are forwarded as `onCDPEvent` notifications with `{ tabId, sessionId? }` source metadata.
- Recoverable browser errors never clear `browser`, `tab`, or unrelated user-defined bindings.
- Outer process ownership and recovery follow [Node REPL](node-repl.md#process-ownership-and-cancellation).

## 14. Security, Privacy, and Policy

Desktop browser policy is authoritative.

- External navigation follows approval, allowed-domain, and blocked-domain settings.
- Browser automation never exposes cookies, passwords, localStorage, IndexedDB, browser history, cache databases, or hidden profile paths through diagnostics or helper APIs.
- URL diagnostics use safe summaries when a URL may contain credentials, tokens, search params, or fragments.
- Page bodies, DOM text, request and response bodies, and console payloads appear in diagnostics only as an explicitly requested command result within result-size limits.
- Native pipe paths, process ids with nonces, extension ids, and local profile paths never appear in UI or ordinary logs.
- Ambient browser-client network checks are disabled by default; policy enforcement never depends on them.

## 15. Error Categories and Diagnostics

Stable IAB error categories: `IabBackendUnavailable`, `SessionMetadataMissing`, `PolicyBlocked`, `ApprovalDenied`, `NavigationFailed`, `CommandTimeout`, `CommandCancelled`, `ResultTooLarge`, `DebuggerUnavailable`, `UnsupportedApi`, `InvalidArgument`, `LocatorStrictModeViolation`, `TabStale`, `NodeStale`, `PageClosed`.

Client-visible errors carry a stable `code`, short English fallback text, and safe structured params when useful, without the sensitive fields in [Section 14](#14-security-privacy-and-policy). The browser client preserves the backend `code` and safe `data` on the JavaScript `Error`, so callers can inspect fields such as navigation `validatedURL`, `finalURL`, and safe error descriptions.

Agent recovery guidance:

- `IabBackendUnavailable`: retry browser runtime setup in the current REPL context; if it repeats, ask the user to restart Desktop.
- `NavigationFailed`: inspect the safe error code and final URL summary before retrying; Chromium error pages are not success.
- `CommandTimeout`: narrow the command or increase that command's timeout; resetting the REPL is not the first step.
- `ResultTooLarge`: filter page-side, request smaller content, or chunk.
- `UnsupportedApi`: use the documented compatibility subset.
- `InvalidArgument`: fix the call shape before retrying.

## 16. Browser Skill Contract

The bundled Browser skill and the runtime documentation returned by `browser.documentation()` are part of the runtime contract because they teach the model how to call the browser API.

- The bundled plugin resource and the browser client's bundled documentation are the sources of truth; workspace-installed copies are derived artifacts.
- The skill covers surface choice, user-facing progress wording, bootstrap through `dotcraft.browserClientPath`, the `NodeReplJs` tool and the `iab` browser id, output through `nodeRepl.write` and `nodeRepl.emitImage`, persistent bindings, and stale-tab recovery. It states that `agent` comes from the bundled browser client and requires reading the complete runtime documentation before the first browser action.
- The runtime documentation contains the supported API (tabs, the Playwright-compatible subset, DOM-CUA, coordinate actions), operating guidance (observation, locator discipline, strict locator failures, search-result narrowing, bounded evaluate, tab reuse, temporary-tab cleanup, search and URL fallback limits, and stopping repeated verification once an authoritative page signal is present), visibility guidance, browser safety, and the confirmation policy for data transmission, account and permission changes, uploads, messages, purchases, browser permission prompts, and actions that require user hand-off.
- Topic guidance for screenshots, viewport, page assets, and WebMCP is listed with when to read it, returned by `agent.documentation.get(name)`, and filtered by backend capabilities.
- Examples match the actual asynchronous API shape, including the explicit capability-handle form. `tab.playwright` is described as a supported subset; methods absent from the bundled client, such as `locator.evaluate()`, are not encouraged.
- The documentation never describes APIs missing from the client or unsupported by the backend. Plugin-root imports from other runtimes, alternate browser-control fallbacks, ordinary downloads, file choosers, uploads, raw CDP capability, and browsing history are not documented as capabilities.
- Model-facing text omits details the model cannot act on, such as internal ids, process and reset mechanics, backend command names, and internal product names.
