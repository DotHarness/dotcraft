# Interactive Tool UI — M‑iv: Host Context, State, Display Mode

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Planned |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑iv — Host context, state, display mode |
| **Depends on** | M‑iii |

## 1. Overview

M‑ii/M‑iii render and act. M‑iv makes the embedded surface **persistent and adaptive**: it stays in sync with Desktop's theme/locale, preserves its own UI state across re‑renders and reloads, and can request a larger display mode. Protocol shapes: parent [§8](tool-result-presentation.md) (host context), [§9](tool-result-presentation.md) (widget state).

## 2. Goal

An interactive card looks native (matches Desktop theme/locale live), remembers where the user was, and can expand when content warrants — without losing state or breaking the conversation surface.

## 3. Scope

- **Live host‑context push**: when Desktop theme, locale, or display mode changes, the host sends `ui/notifications/host-context-changed` with the new context; the UI re‑themes **without reload**.
- **`widgetState` persistence**: the UI's widget state (the UI‑only state of [§9](tool-result-presentation.md), distinct from `ui/update-model-context`) is persisted by the host, **keyed to the conversation item**, and restored into the iframe on re‑render (scroll‑away → back, thread reload, app restart per the §9 durability rules).
- **`requestDisplayMode`** (`inline` / `pip` / `fullscreen`): the UI requests; the host **arbitrates and returns the granted mode** (may differ); `maxHeight` and resize behavior are enforced (the UI reports desired size; the host clamps).

## 4. Non‑goals

- New bridge actions → M‑iii.
- App‑specific UIs → M‑v.
- Security/capability sign‑off → M‑vi.

## 5. Behavioral contract

- Toggling Desktop light/dark re‑themes all open cards live; changing locale updates their language.
- A card's UI state (selected tab, scroll position, staged form input) survives scrolling away and reopening the thread, and survives an app restart.
- An "expand"/fullscreen request is honored, or gracefully denied with the granted mode; a card never exceeds host‑allowed bounds.
- Restored state never causes a flash of the wrong theme or a stale result (context + state arrive before/at first paint where feasible).

## 6. Required workflow / lifecycle

Per parent [§8](tool-result-presentation.md) and [§9](tool-result-presentation.md). `host-context-changed` fires on every relevant host change. `widgetState` durability follows §9 (what persists, where, and for how long). Display‑mode requests are user‑initiated and arbitrated by the host.

## 7. Constraints & compatibility

- `widgetState` is **UI‑only** — it never reaches the model unless the UI explicitly calls `ui/update-model-context`.
- Item‑keyed state must be **size‑bounded** so persistence does not bloat the thread store.
- Display‑mode requests must be user‑initiated; mobile/narrow layouts may coerce `pip`→`fullscreen`.
- Theme uses the neutral host surface and tokens ([DESIGN.md](../clients/DESIGN.md)); the app must not assume host internals.

## 8. Acceptance checklist

- [ ] Theme/locale/displayMode changes push `host-context-changed`; the UI re‑themes without reload.
- [ ] `widgetState` persists keyed to the item and restores on re‑render, thread reload, and app restart.
- [ ] `requestDisplayMode` returns the granted mode; the UI respects it; `maxHeight`/resize are enforced.
- [ ] `widgetState` never leaks to the model.
- [ ] Conformance tests cover context push, state round‑trip, and display‑mode arbitration.

## 9. Open questions

- Where `widgetState` persists (thread/session metadata vs a dedicated store) and its per‑item size cap.
- `pip` / `fullscreen` UX in Desktop (overlay modal vs docked panel) — needs a UX decision.
- Whether restore is delivered in the `ui/initialize` result or via a follow‑up notification.
