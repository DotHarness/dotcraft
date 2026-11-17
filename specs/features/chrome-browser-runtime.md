# DotCraft Chrome Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 0.10.0 |
| **Status** | Living |
| **Date** | 2026-11-17 |
| **Parent Specs** | [Plugin Architecture](../architecture/plugin-architecture.md), [Desktop In-App Browser Runtime](desktop-inapp-browser.md) |

Purpose: define how DotCraft automates the user's existing Chrome profile. Chrome is a second backend of the Desktop browser-use runtime: it shares the browser client, backend primitives, page operations, tab lifecycle, and error contract with the in-app browser, and differs only where a user-owned Chrome profile requires it.

## 1. Scope

This spec covers:

- The Chrome backend profile of the Desktop browser-use runtime and how it differs from the in-app browser.
- The CDP relay between Desktop, the Chrome native messaging host, and the DotCraft Chrome extension.
- Debugger attachment, connection recovery, automation overlay, setup diagnostics, and Chrome-specific recovery guidance.

This spec does not define:

- The browser client API, backend JSON-RPC primitives, page operations, tab ownership, timeouts, cancellation, or result limits; [Desktop In-App Browser Runtime](desktop-inapp-browser.md) owns them for every backend.
- Chrome Web Store publication, enterprise policy, or extension branding.
- Access to cookies, passwords, local storage, history, or other profile storage.

## 2. Goals

1. **One runtime**: Chrome and the in-app browser run the same client and page-operation code, so behavior and fixes stay aligned.
2. **Stable control**: a tab keeps one debugger attachment for as long as the session controls it; commands never attach and detach per call.
3. **Self-healing transport**: the extension and Desktop reconnect without user action after Chrome, the native host, or Desktop restarts.
4. **Observable automation**: a controlled tab shows an automation overlay and the agent cursor, like the in-app browser.
5. **Privacy preservation**: diagnostics never include page bodies, cookies, storage, full URLs, profile paths, pipe paths, extension ids, or native host manifest paths.

## 3. Architecture

| Layer | Owner | Responsibility |
|-------|-------|----------------|
| Browser client | `dotcraft.browserClientPath` | Discovers browser-use backends and exposes `agent.browsers.get("extension")` for Chrome. |
| Chrome backend | Desktop main process | A browser-use backend instance with the Chrome profile (§4). Serves the shared JSON-RPC primitives on its own browser-use pipe and drives Chrome tabs through the relay. |
| Native host | `scripts/native-host.mjs` | Started by Chrome. Bridges Desktop pipe clients and the extension's native messaging port without interpreting commands. |
| Extension | DotCraft Chrome extension | Executes relay methods with `chrome.tabs`, `chrome.debugger`, and `chrome.scripting`, and forwards CDP and tab events. |

Page semantics (navigation readiness, locators, DOM snapshots, DOM-CUA, coordinate input, clipboard, frames, screenshots, waits) run in the Desktop backend over CDP. The extension never implements page semantics.

## 4. Chrome Backend Profile

`getInfo` returns `id` and `type` `extension`, name `Chrome`, and `metadata.dotcraftSessionId`. The browser client selects it with `get("extension")`.

Differences from the in-app browser:

| Area | Chrome behavior |
|------|-----------------|
| Tabs | Backend tabs are Chrome tabs in the user's profile. Agent-created tabs open in the last focused normal window, in a tab group named after the session. The first created tab of a session is activated; later tabs open in the background. |
| User tabs | `getUserTabs` lists normal `http`, `https`, and `file` tabs with title, URL, and active state only. Listed tabs are not controlled until claimed. |
| Presentation | No Desktop viewer tabs, panel focus changes, or browser `visibility` capability. |
| Navigation policy | Desktop in-app browser domain approval does not apply; the user's own profile is the trust boundary. Internal `chrome:`, `devtools:`, extension, and Chrome Web Store pages cannot be controlled. |
| Viewport | The natural size is the tab's content size. The `viewport` capability applies and resets explicit overrides as in the in-app browser. |
| Screenshots | Captured through CDP. A background tab is activated in its window before capture. |
| Downloads, uploads, file choosers, history | Unsupported, as in the in-app browser. |

## 5. Relay Transport

The native host listens on one local pipe per process:

| Platform | Address Shape |
|----------|---------------|
| Windows | `\\.\pipe\dotcraft-chrome-<pid>-<nonce>` |
| macOS/Linux | `<tempdir>/dotcraft-chrome-<pid>-<nonce>.sock` |

Frames are 4-byte little-endian length-prefixed JSON. There is no TCP fallback.

- Request: `{ "id": 1, "method": "debugger.send", "params": { ... } }`
- Response: `{ "id": 1, "result": { ... } }` or `{ "id": 1, "error": { "message": "DebuggerUnavailable: ..." } }`
- Event: `{ "event": "debugger.event", "params": { ... } }`

The native host rewrites request ids per pipe client, routes responses back to the requesting client, and broadcasts events to all clients. When its last pipe client disconnects it tells the extension to release all tabs. Relay protocol version is `4`.

| Method | Result |
|--------|--------|
| `hello` | `{ protocolVersion, extensionVersion }`. |
| `tabs.list` | Normal tabs: `{ tabId, windowId, url, title, loading, active, width, height }`. |
| `tabs.create` | `{ url?, active?, groupTitle? }` → tab. |
| `tabs.update` | `{ tabId, url?, active? }` → tab. |
| `tabs.close` | Closes a tab. |
| `debugger.attach` | Attaches CDP to a tab; succeeds when this extension already holds it. |
| `debugger.detach` | Releases the attachment. |
| `debugger.send` | `{ tabId, sessionId?, method, params }` → CDP result. |
| `overlay.set` | `{ tabId, active, sessionName? }` shows or hides the automation overlay. |
| `overlay.moveCursor` | `{ tabId, x, y, sequence, animate }`. |

| Event | Params |
|-------|--------|
| `debugger.event` | `{ tabId, sessionId?, method, params }` |
| `debugger.detached` | `{ tabId, reason }` |
| `tabs.updated` | Tab snapshot as in `tabs.list`, sent when a tab's URL, title, or loading state changes and for every tab in a window whose active tab changes. |
| `tabs.removed` | `{ tabId }` |
| `overlay.cursorArrived` | `{ tabId, sequence }` |

Relay failures carry the stable error categories in §9 as message prefixes; CDP command errors pass through unchanged.

## 6. Debugger Attachment

- The backend attaches a tab on its first CDP-backed command and keeps the attachment, so Chrome's debugging banner appears once and stays while the session controls the tab.
- Commands on one tab are serialized by the backend; the extension does not attach or detach around individual commands.
- Release, close, finalization, turn cleanup, and backend reset detach the tab. The extension detaches every tab and hides every overlay when its native port closes or the native host releases all tabs.
- A detach with reason `canceled_by_user` means the user stopped automation from Chrome's banner. Later commands on that tab fail with `CommandCancelled` until the session claims or creates a tab again; the backend never silently re-attaches.
- Another debugger on the tab, or an uncontrollable page, fails with `DebuggerUnavailable`.

## 7. Connection Recovery

- The extension connects to the native host when its service worker starts. After a disconnect it retries after 5 seconds and through a 30-second alarm until connected. The popup reports `connected`, `reconnecting`, or `disconnected`.
- Desktop connects to the relay lazily on the first Chrome command and keeps one connection. When it closes, pending relay calls fail with `BridgeDisconnected`, controlled tabs lose their attachments, and the next command rediscovers the relay.
- The browser client discovers the Chrome backend through the browser-use pipe namespace. Chrome backend availability does not depend on the relay being connected; relay failures surface on commands.

## 8. Automation Overlay

- A tab shows the overlay while it is automation-active, with the same activation rules as the in-app virtual cursor ([Desktop In-App Browser Runtime §12](desktop-inapp-browser.md#12-coordinate-input-virtual-cursor-and-dom-cua)).
- The overlay renders an edge glow and the agent cursor in a closed shadow root on the top document, above page content, with no pointer events. Snapshots, locators, DOM-CUA, and screenshots exclude it.
- `overlay.moveCursor` animates when the tab is visible and the window focused, and the extension emits `overlay.cursorArrived` for that sequence; otherwise the cursor snaps and arrival is immediate. The backend's arrival wait follows the in-app bound.
- Navigation removes the overlay with the document; the extension restores it on the new document while the tab is still active. Overlay failures never fail a command.

## 9. Setup Status, Diagnostics, and Errors

`dotcraft.chrome.checkSetup()` returns normalized setup checks:

```json
{
  "extension": { "ok": true, "code": "extensionReady", "message": "DotCraft Chrome extension is ready." },
  "nativeHost": { "ok": true, "code": "nativeHostReady", "message": "Chrome Native Host is installed." },
  "chromeRunning": { "ok": true, "code": "chromeRunning", "message": "Chrome is running." },
  "installedBrowsers": { "ok": true, "code": "chromeInstalled", "message": "Google Chrome is installed." },
  "backend": { "ok": true, "code": "backendConnected", "message": "Chrome backend is connected." }
}
```

Each check has `ok`, a stable `code`, a short user-safe `message`, and optional `action` and `safeDetails`. The `backend` check succeeds when a relay pipe answers `hello`. Overall status priority: Chrome missing, extension not ready, native host missing, Chrome not running, backend disconnected, connected.

Allowed diagnostics: session, turn, evaluation, and command ids; backend id, method, status, elapsed time, timeout, cancellation flag; error category; relay candidate count and protocol version. Forbidden diagnostics are listed in §2.

Chrome adds these categories to the shared in-app browser categories: `ChromeNotInstalled`, `ChromeNotRunning`, `ExtensionMissing`, `ExtensionDisabled`, `NativeHostInvalid`, `BridgeDisconnected`.

Agent recovery:

- `BridgeDisconnected`: run `dotcraft.chrome.checkSetup()`; if setup is otherwise healthy, ask the user to confirm Chrome is open and the DotCraft extension is enabled, then retry once.
- `CommandCancelled` after the user stopped Chrome's debugging banner: tell the user browser control was stopped in Chrome and ask before resuming.
- `DebuggerUnavailable`: ask the user to close DevTools or another tool debugging the tab, or choose a normal web page.
- Other categories follow [Desktop In-App Browser Runtime §15](desktop-inapp-browser.md#15-error-categories-and-diagnostics).
