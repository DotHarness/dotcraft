# Interactive Tool UI — M‑iii: Bridge Actions

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Planned |
| **Date** | 2026-06-09 |
| **Parent Spec** | [Interactive Tool UI](tool-result-presentation.md) |
| **Milestone** | M‑iii — Bridge actions (make the UI interactive) |
| **Depends on** | M‑i (delivered), M‑ii (delivered) |

## 1. Overview

M‑ii delivered a **read‑only** host: Desktop renders an app's `ui://` resource in a sandboxed `dotcraft-app://` iframe, answers `ui/initialize`, and pushes `tool-input` / `tool-result` over the bridge. M‑iii makes the iframe **act**: the UI→host request surface ([parent §7.3](tool-result-presentation.md)) goes live, the host advertises those capabilities, and apps may load data directly from their own backend.

This milestone is a behavior contract; protocol shapes live in the parent spec ([§7](tool-result-presentation.md) bridge, [§10](tool-result-presentation.md) authorization, [AppServer §11.3.2](appserver-protocol.md) `ui/tool/call`).

## 2. Goal

An app's iframe can perform real, authority‑bounded actions — invoke app tools, open links/the app, push UI state to the model, and inject a follow‑up turn — and may `fetch` its own loopback backend, all gated by App Binding.

## 3. Scope

- **`tools/call` (UI → host) → `ui/tool/call`**: forwarded to AppServer, gated by App Binding scope + the tool's `app` visibility, **audited**, **decoupled** (no conversation turn or item), result returned to the UI via `ui/notifications/tool-result`.
- **`ui/open-link`**: host opens an `https:` URL or a bound app's declared deep‑link scheme. Scheme allow‑list only; `javascript:` / `data:` / `file:` forbidden. Powers "Open in Oratorio".
- **`ui/update-model-context`**: UI pushes UI‑derived state the model should see → recorded as an App Binding context block; no turn/item; last‑write‑wins; size‑bounded.
- **`ui/message`**: UI injects a user message → a real agent turn (`turn/start` / `turn/enqueue`); surfaces as a normal, visible user message.
- **Data path B (direct fetch)**: the `dotcraft-app://` document's CSP is widened from `_meta.ui.csp` (`connectDomains`→`connect-src`, `resourceDomains`→img/style/font/media, `frameDomains`→`frame-src`), enabling the UI to `fetch` its loopback backend. With no `_meta.ui.csp`, the default remains network‑denied (M‑ii baseline).
- **`hostCapabilities`** in the `ui/initialize` result report the now‑available actions (`openLinks`, `serverTools`, `updateModelContext`, `message`) as `true`.

## 4. Non‑goals

- Live host‑context push, `widgetState` persistence, display modes → **M‑iv**.
- New app UIs / SDK ergonomics → **M‑v**.
- Strict `interactiveToolUi` negotiation + full security sign‑off → **M‑vi** (basic per‑action gating is in scope here).

## 5. Behavioral contract

- A UI action produces the expected effect: refresh re‑fetches; a button mutates app state via a tool call; "Open in X" opens the app/link; "discuss this" injects a turn.
- UI‑initiated tool calls are bounded by the binding's granted scope + the tool's `app` visibility; out‑of‑scope, non‑`app`‑visible, or cross‑binding calls are **rejected with an error the UI receives** — never executed.
- `ui/open-link` opens only allow‑listed schemes; a blocked scheme is rejected, never executed.
- `ui/message` yields a visible user message + a normal agent turn; it must not silently act as the user (user‑gesture gated, rate‑limited).
- `ui/update-model-context` changes what the model sees next turn but creates **no visible conversation item**.
- The model is **not** implicitly informed of `ui/tool/call` results (decoupled); only `ui/update-model-context` and `ui/message` reach the model.

## 6. Required workflow / lifecycle

Per parent [§7](tool-result-presentation.md) (UI→host) + [§10](tool-result-presentation.md) (authorization). Each action: validate binding active → check scope / visibility → apply risk/consent policy → execute or broker → respond to the UI (result or notification). Every `ui/tool/call` is written to the App Binding audit trail.

## 7. Constraints & compatibility

- **Prompt‑cache stability**: UI tool calls must not alter the model‑visible tool surface.
- **Decoupling invariant**: `ui/tool/call` never creates turns/items (it is `callTool`, not a conversation message).
- **Per‑resource CSP**: widening one iframe's CSP must not affect other iframes or the app shell.
- **Loopback CORS**: an app backend serving data path B must allow the iframe's opaque origin; this is the app's responsibility (to be documented for app authors).

## 8. Acceptance checklist

- [ ] UI `tools/call` reaches the app via `ui/tool/call`, gated + audited, with **no** conversation turn/item, and the result is delivered to the UI.
- [ ] Out‑of‑scope / non‑`app`‑visible / cross‑binding UI tool calls are rejected with an error the UI receives.
- [ ] `ui/open-link` opens `https:` + declared app schemes and rejects `javascript:`/`data:`/`file:`.
- [ ] `ui/message` injects a visible user message + agent turn, is user‑gesture gated and rate‑limited.
- [ ] `ui/update-model-context` updates model context for the next turn with no visible item; last‑write‑wins; size‑bounded.
- [ ] An app UI can `fetch` its loopback backend under the widened CSP; default (no `connectDomains`) still blocks network.
- [ ] `hostCapabilities` reflects the enabled actions.
- [ ] Conformance tests cover `ui/tool/call` gating and each UI→host action.

## 9. Open questions

- `ui/message` gating: require an explicit in‑UI user gesture, or trust the app? (The host cannot verify a real click inside a sandboxed iframe.)
- `ui/update-model-context` representation: reuse the App Binding context‑block mechanism, or a dedicated per‑item "model‑visible widget state" channel?
- Loopback CORS: provide an SDK helper / documented expectation for app backends, or leave entirely to each app?
