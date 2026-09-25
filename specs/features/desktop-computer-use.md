# DotCraft Desktop Computer Use Specification

| Field | Value |
|-------|-------|
| **Version** | 1.1.0 |
| **Status** | Draft |
| **Date** | 2026-09-25 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Node REPL](node-repl.md), [Desktop Client](../clients/desktop-client.md), [Plugin Architecture](../architecture/plugin-architecture.md) |

Purpose: define how DotCraft Desktop lets the agent observe and operate native desktop applications on the user's Windows computer through the thread-bound Node REPL.

---

## 1. Scope

This spec covers:

- The `dotcraft.computer` host API exposed inside `NodeReplJs` on Windows Desktop.
- The Desktop-owned computer use runtime: driver process lifecycle, request admission, timeouts, turn cleanup, lock handling and the stop affordance.
- Application identity, blocked applications and per-application authorization.
- The AppServer approval round trip used when an application has not been authorized.
- The bundled `computer` plugin and the Computer use settings surface.

This spec does not define:

- Browser automation; see [Desktop In-App Browser Runtime](desktop-inapp-browser.md) and [Chrome Browser Runtime](chrome-browser-runtime.md).
- macOS or Linux computer use.
- Remote control of Satellite machines; Satellite excludes remote input by design.
- Mentioning applications in the composer to pre-authorize them. That is a later extension.

---

## 2. Goals

1. **One execution path**: the model reaches desktop applications only through `dotcraft.computer`, whose driver is owned and supervised by Desktop main.
2. **Application consent**: every observation or input targets one resolved application. Unknown applications require an explicit user decision; blocked applications are never operated.
3. **Visible and stoppable**: while computer use is active the user sees a persistent indicator and can stop it with Escape.
4. **Bounded side effects**: one native request runs at a time on the machine, each request has a deadline, and turn end, cancellation and screen lock stop further input.
5. **Stable model surface**: the model-visible tool remains `NodeReplJs`; computer use adds a plugin skill, not a new tool schema.

---

## 3. Architecture

```
NodeReplJs ──ext/nodeRepl/evaluate──► Desktop main ──► thread REPL worker
                                                          └─ dotcraft.computer.* (host calls)
Desktop main: ComputerUseManager (one per Desktop process)
  ├─ API adapter ──► cua-driver (stdio MCP child process)
  ├─ application identity, blocked applications, authorization
  ├─ ext/nodeRepl/requestApproval ──► AppServer turn approval service
  └─ status pill and edge glow, Escape stop, lock handling
```

- **AppServer** exposes `NodeReplJs` to connections that declared `capabilities.nodeRepl` together with `capabilities.browserUse` or `capabilities.computerUse`, subject to plugin enablement (Section 8). It owns approval policy, approval items and the evaluate deadline.
- **Desktop main** owns the driver process, the application authorization state and all user-facing computer use surfaces. The REPL worker only relays host calls; it never receives the driver path.
- **cua-driver** is the execution layer. It is an external MIT-licensed binary pinned to one version and shipped with the Windows Desktop package.

The Node REPL is not an OS sandbox. The skill forbids launching the driver or native input tools directly; the authorization boundary described here applies to `dotcraft.computer`.

---

## 4. Host API

`dotcraft.computer` is present in the REPL only on Windows Desktop. Methods return promises; failures reject with an `Error` whose message starts with a stable code when one exists, for example `stale_element_token: ...`.

### 4.1 Types

- `App`: `{ id, displayName, isRunning? }`. `id` is the application identity (Section 5).
- `Window`: `{ id, pid, title, app: { id, displayName } }`. `id` is the native window handle. The runtime re-resolves `app` from `id` on every call and ignores values supplied by the model.
- Screenshot: `{ width, height }`, the pixel space of that observation.

### 4.2 Methods

| Method | Behavior |
|---|---|
| `list_apps()` | Installed and running applications, excluding blocked applications. |
| `list_windows()` | On-screen top-level windows, excluding windows of blocked applications. |
| `get_window({ id })` | One window from `list_windows()` by handle. |
| `launch_app({ app })` | Launches an application by an `id` returned from `list_apps()` in the same turn. Requires authorization of that application. |
| `get_window_state({ window, include_screenshot = true, include_text = false })` | Returns `{ window, screenshots: [Screenshot], accessibility: { tree } \| null }`. At least one of the two includes must be true. Screenshots are attached to the evaluation's image output automatically; the model must not re-emit them. |
| `click({ window, element_index? , x?, y?, click_count = 1, mouse_button = "left" })` | Clicks an element from the latest accessibility observation of that window, or a pixel of its latest screenshot. |
| `type_text({ window, text })` | Types literal text into the focused control. |
| `press_key({ window, key })` | Presses a key or chord written as `+`-separated names such as `ctrl+s`, `Return`, `Tab`, `Escape`, `F5`. Modifiers are `ctrl`, `shift` and `alt`; key names follow the driver's vocabulary. Windows-logo keys (`super`, `win`, `meta`, `cmd`) are rejected. |
| `scroll({ window, x, y, scrollX = 0, scrollY = 0 })` | Scrolls at a screenshot pixel. Positive `scrollY` scrolls down and positive `scrollX` scrolls right, in wheel notches. |
| `set_value({ window, element_index, value })` | Sets the value of an element from the latest accessibility observation. |
| `drag({ window, from_x, from_y, to_x, to_y })` | Drags between screenshot pixels. |
| `activate_window({ window })` | Brings the window to the foreground. |

Element indexes are valid only for the latest observation of their window; any later observation replaces them, including one without a screenshot. Pixel coordinates are in the pixel space of the window's latest screenshot. Input methods use foreground delivery: the runtime activates the target window, injects input, and restores the previous foreground window and cursor where the driver can.

Every method that takes `window` or `app` is authorized first (Section 6). `list_apps`, `list_windows` and `get_window` are not authorization-gated.

### 4.3 Stable failure codes

| Code | Meaning |
|---|---|
| `computer_use_busy` | Another computer use request is running on this Desktop. The call is not queued. |
| `computer_use_stopped` | The user stopped computer use for this turn, or the desktop was locked. Every later call in the same turn fails with this code. |
| `app_blocked` | The target application is blocked (Section 6.1). |
| `app_not_approved` | The user declined, the approval timed out, or the thread policy denied access. |
| `app_unidentified` | The runtime could not resolve the window's application. |
| `invalid_key` | A key or modifier name is not supported, or the chord uses a Windows-logo key. |
| `driver_unavailable` | The driver could not start or stopped unexpectedly. |
| `timeout` | The request exceeded its deadline; its effect is unknown and must be observed before retrying. |

Driver error codes such as `stale_element_token` or `ambiguous_window_target` pass through unchanged.

---

## 5. Application Identity

- A packaged application is identified by its Application User Model ID. A desktop application is identified by the absolute path of its executable.
- Desktop main resolves identity from the window handle, not from driver-reported process names. For windows hosted by `ApplicationFrameHost.exe` the identity comes from the hosted core window's process.
- Display names come from the Start menu entry for packaged applications and from the executable's version information for desktop applications, falling back to the file name.
- Identities compare case-insensitively.
- Resolution results are cached per window handle until the turn ends. A window whose application cannot be resolved fails with `app_unidentified`.

---

## 6. Authorization

### 6.1 Blocked applications

DotCraft's own processes and the driver are never operated, listed or launched, and no approval is
requested. Desktop matches them by its own executable path and by executable file name. Every other
application is decided by authorization (Section 6.2).

### 6.2 Decision order

For each gated call, Desktop main:

1. Resolves the application and rejects blocked applications with `app_blocked`.
2. Allows applications listed in `computerUse.alwaysAllowedApps` in Desktop settings.
3. Allows applications already approved in the current turn.
4. Otherwise sends `ext/nodeRepl/requestApproval` for the evaluation (Section 7) and allows or fails with `app_not_approved` according to the result.

### 6.3 Persistence

- **Always**: Desktop settings `computerUse.alwaysAllowedApps` is the only persistent store: `[{ id, displayName }]`. Desktop adds an entry when the user answers a computer use approval with `acceptAlways`. The Settings page lists entries and can remove them; it cannot add them.
- **This thread**: `acceptForSession` is recorded by the AppServer session approval scope of the thread.
- The AppServer does not persist `acceptAlways` for `approvalType = "computerUse"`.

---

## 7. Approval Round Trip

- `ext/nodeRepl/requestApproval` is a client-to-server request tied to an in-flight evaluation. Its protocol shape is defined in [AppServer Protocol Section 11.5](../protocols/appserver-protocol.md#115-node-repl-runtime).
- The AppServer uses the approval service of the turn that issued the evaluation. The approval request uses `approvalType = "computerUse"`, `operation = "use"`, `target = <application id>` and `targetLabel = <display name>`; `scopeKey` is derived from the application id.
- Thread approval policy applies unchanged: `autoApprove` allows, `deny` declines, hooks may decide, and a prior `acceptForSession` for the same application in the thread allows without a new request.
- While the approval is pending, the evaluate deadline is paused on both the AppServer and Desktop. The approval uses the thread approval timeout; timing out declines.
- When an approval request arrives for the thread and turn that currently own computer use, Desktop brings its main window to the front and shows that thread.
- The approval UI offers three choices: always allow, allow for this thread, and decline. They map to `acceptAlways`, `acceptForSession` and `decline`.

---

## 8. Runtime

### 8.1 Enablement

- Desktop declares `capabilities.computerUse` only on Windows.
- The bundled `computer` plugin contributes the computer use skill. `NodeReplJs` counts the `computer` plugin as a runtime plugin only for connections with `capabilities.computerUse`.
- The Settings page's "Any app" toggle installs the plugin when missing and enables or disables it.

### 8.2 Driver process

- Desktop starts `cua-driver.exe mcp --direct` lazily on the first gated or listing call, with telemetry and update checks disabled, and verifies the reported version before use.
- The runtime speaks the minimal MCP subset required: `initialize` and `tools/call`. Only the driver tools needed by Section 4 are called.
- A driver process serves at most one turn. When the turn that owns computer use ends, Desktop stops admitting calls for it, waits a bounded time for the in-flight call and closes the driver. A call from another turn closes the previous turn's driver first.
- Desktop teardown and application quit close the driver.

### 8.3 Admission and deadlines

- One computer use request runs at a time per Desktop process. A concurrent request fails immediately with `computer_use_busy`.
- `launch_app` and `list_apps` have a 15 second deadline and other requests have 10 seconds. A request that exceeds its deadline fails with `timeout`; the driver is terminated and restarted on the next call.
- Cancelling or timing out the outer evaluation abandons the in-flight request and discards its late result.

### 8.4 Stop affordance and lock

- From the first computer use call until the turn ends, Desktop shows a non-focusable status pill reading "DotCraft is using your computer · Esc to cancel" at the top of the primary display, and a breathing brand-gradient glow around that display's edges. Both ignore the pointer and are excluded from screen capture; the driver's own agent cursor is the only cursor overlay.
- While the pill is visible, Escape stops computer use for the turn. Desktop suspends its Escape shortcut while the runtime itself injects Escape.
- Locking the workstation stops computer use for the turn.
- Stopping terminates the driver, hides the pill and glow, and makes later calls in the same turn fail with `computer_use_stopped`. The turn itself continues so the model can report what happened.

---

## 9. Presentation

- Screenshots produced by `get_window_state` appear in the `NodeReplJs` tool result image output.
- The computer use approval card asks whether DotCraft may use the named application and offers the three choices in Section 7.
- Settings › Computer use shows the Control group (Any app, Chrome) and the Always-allowed apps group with an empty state.

---

## 10. Packaging

- The Windows build downloads the pinned `cua-driver` release archive for the target architecture, verifies its SHA-256 and Authenticode signer, and packages `cua-driver.exe` with its MIT license under the Desktop resources `bin/cua-driver` directory.
- No other driver components, installers or scheduled tasks are shipped or registered.
