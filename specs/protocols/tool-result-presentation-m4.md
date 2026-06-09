# Interactive Tool UI — M‑iv: Host Context, State, Display Mode

| Field | Value |
|-------|-------|
| **Version** | 0.3.0 |
| **Status** | ✅ Delivered |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑iv — Host context, state, display mode |
| **Depends on** | M‑iii |

> **Delivery split.** M‑iv shipped in two passes. **Pass 1:** live host‑context push (theme/locale → `ui/notifications/host-context-changed`, no reload) and `widgetState` persistence (the `item/widget-state/set` AppServer method, the `item_widget_state` SQLite side store, `thread/read` enrichment, restore in the `ui/initialize` result). **Pass 2:** `requestDisplayMode` arbitration with **`pip` = floating corner window** and **`fullscreen` = portal overlay** over the conversation (not the DetailPanel — that is a typed viewer‑tab subsystem and an invasive fit). The expanded surface re‑mounts the iframe; because re‑parenting an iframe reloads it, `widgetState` restore (Pass 1) is what preserves the user's state across the mode switch.

## 1. Overview

M‑ii/M‑iii render and act. M‑iv makes the embedded surface **persistent and adaptive**: it stays in sync with Desktop's theme/locale, preserves its own UI state across re‑renders and reloads, and can request a larger display mode. Protocol shapes: parent [§8](tool-result-presentation.md) (host context), [§9](tool-result-presentation.md) (widget state).

## 2. Goal

An interactive card looks native (matches Desktop theme/locale live), remembers where the user was, and can expand when content warrants — without losing state or breaking the conversation surface.

## 3. Scope

- **Live host‑context push**: when Desktop theme, locale, or display mode changes, the host sends `ui/notifications/host-context-changed` with the new context; the UI re‑themes **without reload**. The renderer subscribes to the existing `THEME_CHANGED_EVENT` (and its `locale` prop) rather than polling — see [§6](#6-required-workflow--lifecycle).
- **`widgetState` persistence**: the UI's widget state (the UI‑only state of parent [§9](tool-result-presentation.md), distinct from `ui/update-model-context`) is persisted by the host, **keyed to the originating `dynamicToolCall` item** (by `callId`), and restored into the iframe on re‑render (scroll‑away → back, thread reload, app restart). Because the canonical thread rollout is **append‑only / event‑sourced** (no item‑update event), `widgetState` is stored in a **dedicated mutable per‑thread side store** (SQLite, like context‑usage tokens / thread goals) — *not* the rollout JSONL. The client‑facing contract is unchanged: the host surfaces it as a free‑form `widgetState` field on the `dynamicToolCall` payload on `thread/read` (alongside the existing UI‑only `_meta` / `ui`), and the UI writes updates back via a new decoupled `item/widget-state/set` AppServer method. **UI‑only — never reaches the model**; **size‑bounded** (≤ 8 KB per item; oversized updates rejected).
- **`requestDisplayMode`** (`inline` / `pip` / `fullscreen`): the UI requests; the host **arbitrates and returns the granted mode** (may differ). `fullscreen` renders the iframe in a **portal overlay** over the conversation (backdrop + close); `pip` renders it in a **floating corner window**; `inline` is the default in‑conversation surface. On a narrow window the host coerces `pip` → `fullscreen`. While a card is expanded (pip/fullscreen) its inline slot shows a **placeholder** with a Collapse affordance, so only one live iframe exists per card. Re‑mounting the iframe in the new surface restores its `widgetState` from Pass 1.

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

Per parent [§8](tool-result-presentation.md) and [§9](tool-result-presentation.md).

- **Host‑context push**: the renderer subscribes to `THEME_CHANGED_EVENT` and re‑emits `ui/notifications/host-context-changed` on theme/locale/displayMode change; no reload.
- **`widgetState` write path**: the UI sends `widgetState` over the bridge → host writes it back via `item/widget-state/set` (decoupled, no turn/item) → persisted in the dedicated mutable per‑thread side store keyed by `callId`.
- **`widgetState` restore path**: delivered in the **`ui/initialize` result** (alongside `hostContext`), so it is present at/before first paint — no flash of a stale/empty card. The host reads it from the side store and surfaces it on the `dynamicToolCall` payload loaded by `thread/read`.
- **Display mode**: `requestDisplayMode` is user‑initiated; the host arbitrates, returns the granted mode, and clamps to `maxHeight`. `fullscreen` is hosted in the `DetailPanel`.

## 7. Constraints & compatibility

- `widgetState` is **UI‑only** — it never reaches the model unless the UI explicitly calls `ui/update-model-context`.
- Item‑keyed state must be **size‑bounded** so persistence does not bloat the thread store.
- Display‑mode requests must be user‑initiated; mobile/narrow layouts may coerce `pip`→`fullscreen`.
- Theme uses the neutral host surface and tokens ([DESIGN.md](../clients/DESIGN.md)); the app must not assume host internals.

## 8. Acceptance checklist

- [x] Theme/locale changes push `host-context-changed`; the UI re‑themes without reload. *(displayMode push lands with Pass 2.)*
- [x] `widgetState` persists in the side store (via `item/widget-state/set`) and restores on re‑render, thread reload, and app restart — delivered in the `ui/initialize` result.
- [x] `widgetState` is size‑bounded (≤ 8 KB); oversized updates are rejected and never reach the model.
- [x] `requestDisplayMode` returns the granted mode; `fullscreen` = portal overlay, `pip` = floating window; `pip` coerces to `fullscreen` on a narrow window; the inline slot shows a Collapse placeholder while expanded.
- [x] Conformance tests cover context push, `widgetState` round‑trip (set → reload → restore + size cap), and display‑mode arbitration.

## 9. Resolved decisions

- **`widgetState` persistence** → stored in a **dedicated mutable per‑thread side store** (SQLite, keyed by `callId`) — the canonical rollout is append‑only and cannot hold a mutated payload field. The client‑facing contract is a free‑form, UI‑only `widgetState` field surfaced on the `dynamicToolCall` payload on `thread/read` (mirroring `_meta` / `ui`); the host writes UI updates back via a new decoupled `item/widget-state/set` AppServer method. **Per‑item size cap ≤ 8 KB.** (Server‑authoritative + cross‑client; uses the established side‑store pattern of context‑usage tokens / thread goals.)
- **`pip` / `fullscreen` UX** → `fullscreen` = a **portal overlay** over the conversation (backdrop + close); `pip` = a **floating corner window**; `inline` stays the default. `pip` coerces to `fullscreen` on a narrow window. (Grounding rejected the earlier DetailPanel option: that is a typed viewer‑tab subsystem — file/browser/terminal — and threading an app‑card kind through it is invasive; a portal overlay is the conventional, self‑contained fullscreen surface.) Re‑mount in the expanded surface relies on Pass‑1 `widgetState` restore to preserve state.
- **Restore delivery** → returned in the **`ui/initialize` result** alongside `hostContext`, so state is present at/before first paint (no flash). (A follow‑up notification was rejected for the flash risk.)
