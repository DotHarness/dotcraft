# DotCraft Desktop Browser Runtime Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-05-28 |
| **Parent Specs** | [AppServer Protocol](../protocols/appserver-protocol.md), [Desktop Client](../clients/desktop-client.md), [Chrome Browser Runtime](chrome-browser-runtime.md) |

Purpose: define the behavior contract for DotCraft Desktop's embedded in-app browser.

---

## 1. Scope

This spec covers the Desktop embedded browser backend identified as `desktop-iab`, including:

- Agent-visible Browser plugin and skill behavior.
- Browser and tab automation APIs exposed through the thread-bound Node REPL runtime.
- Browser capabilities, tab capabilities, viewport, visibility, screenshot, DOM, locator, CUA, and diagnostics behavior.
- User-visible browser tab states that make agent actions understandable.

This spec does not define the Chrome extension backend, except where shared browser runtime contracts require compatibility.

---

## 2. Runtime Contract

The Desktop embedded browser should keep a stable automation API shape across releases. When the embedded browser cannot support a capability, the method must fail with a clear unsupported error instead of being absent or silently ignored.

The built-in plugin and skill name is `browser`; no alternate plugin or skill id is defined.

---

## 3. Interaction Behavior

- Browser automation should be understandable when visible: active sessions expose a session name, recent action hint, and virtual cursor state where possible.
- Coordinate and locator-driven pointer actions must move the visible virtual cursor along a short path before click, double-click, drag, and scroll input is sent.
- The visible pointer path should be fast enough for automation but slow enough for a user watching the browser to perceive the movement.
- Native input events should follow the same path semantics as the virtual cursor when practical, so hover and pointer-target behavior match what the user sees.
- If the page cannot accept the injected cursor overlay, native input must still complete.

---

## 4. Acceptance Checklist

- CUA click and double-click visibly move the pointer before pressing.
- CUA drag emits movement across the supplied path rather than jumping to each endpoint.
- CUA scroll first moves to the scroll point before wheel input.
- Overlay injection failure or timeout does not prevent native input.
- Tests cover that click emits multiple `mouseMove` events before `mouseDown`.
- Manual browser navigation treats local development hosts such as `localhost:3000`, `127.0.0.1:5173`, and `[::1]:8080` as `http://` URLs.
