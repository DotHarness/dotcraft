# Interactive Tool UI — M‑iii: Bridge Actions

| Field | Value |
|-------|-------|
| **Version** | 1.0.0 |
| **Status** | ✅ Delivered |
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

- **`tools/call` (UI → host) → `ui/tool/call`**: forwarded to AppServer, gated by App Binding scope + the tool's `app` visibility, **audited**, **decoupled** (no conversation turn or item), result returned to the UI as the JSON‑RPC response to its `tools/call` request.
  - **M‑iii restriction — read‑only only.** UI‑initiated tool calls are limited to **read / no‑approval** tools. A tool that declares an approval descriptor (i.e. `mutate` / `externalWrite`) is **rejected** from the UI with an error the UI receives, because the decoupled call has no turn/item on which to host the existing approval flow. The decoupled mutate‑approval UX ships with the first real mutating app in **M‑v** (Oratorio `QueueReviewRound`). See [§9](#9-resolved-decisions).
- **`ui/open-link`**: host opens an `https:` or `mailto:` URL. **Scheme handling is host‑owned policy** — apps do **not** declare ad‑hoc custom schemes (consistent with MCP Apps / OpenAI Apps SDK, which both leave scheme handling to the host). `javascript:` / `data:` / `file:` and all other schemes are forbidden in M‑iii. *(M‑v extends this host policy with one binding‑scoped allowance: the bound app's declared `nativeApplication.protocol` becomes an allowed deep‑link scheme — see [M‑v](tool-result-presentation-m5.md).)*
- **`ui/update-model-context`**: UI pushes UI‑derived state the model should see → recorded as an **App Binding context block** (`visibility: "model"`), with the `blockId` derived from the originating `dynamicToolCall` item id; no turn/item; **last‑write‑wins**; size‑bounded by the existing context‑block caps. The block is **removed on `ui/resource-teardown`** (the card's UI state stops reaching the model once the card is gone).
- **`ui/message`**: UI injects a user message → a **visible** user message + a normal agent turn (`turn/start`). Rate‑limited per binding/item. Because a sandboxed iframe's gesture cannot be host‑verified, M‑iii does **not** attempt gesture verification; visibility + rate‑limit + audit are the safeguards. The host **MAY** request user consent (aligns with MCP Apps `ui/message`: *Host SHOULD add the message… MAY request user consent*).
- **Data path B (direct fetch)**: the `dotcraft-app://` document's CSP is widened from `_meta.ui.csp` (`connectDomains`→`connect-src`, `resourceDomains`→img/style/font/media, `frameDomains`→`frame-src`), enabling the UI to `fetch` its loopback backend. The widened CSP is built host‑side from the **server‑validated** descriptor (returned by `ui/resource/read`), never from the iframe. With no `_meta.ui.csp`, the default remains network‑denied (M‑ii baseline). Loopback **CORS** is the app backend's responsibility (documented for app authors); an SDK helper is deferred to M‑v.
- **`hostCapabilities`** in the `ui/initialize` result report the now‑available actions (`openLinks`, `serverTools`, `updateModelContext`, `message`) as `true`.

## 4. Non‑goals

- Live host‑context push, `widgetState` persistence, display modes → **M‑iv**.
- New app UIs / SDK ergonomics → **M‑v**.
- Strict `interactiveToolUi` negotiation + full security sign‑off → **M‑vi** (basic per‑action gating is in scope here).

## 5. Behavioral contract

- A UI action produces the expected effect: refresh re‑fetches (data path B); a read tool call returns app data to the UI; "Open in X" opens the link; "discuss this" injects a turn.
- UI‑initiated tool calls are bounded by the binding's granted scope + the tool's `app` visibility; out‑of‑scope, non‑`app`‑visible, or cross‑binding calls are **rejected with an error the UI receives** — never executed.
- **M‑iii: a mutating tool call from the UI (tool with an approval descriptor) is rejected with an error the UI receives** — never executed (decoupled mutate‑approval is M‑v).
- `ui/open-link` opens only `https:` / `mailto:`; any other scheme (incl. `javascript:`/`data:`/`file:`) is rejected, never executed.
- `ui/message` yields a visible user message + a normal agent turn; it is rate‑limited, and the host MAY request consent. M‑iii does not host‑verify the iframe gesture.
- `ui/update-model-context` changes what the model sees next turn but creates **no visible conversation item**; re‑pushing overwrites (last‑write‑wins); the block is removed when the card tears down.
- The model is **not** implicitly informed of `ui/tool/call` results (decoupled); only `ui/update-model-context` and `ui/message` reach the model.

## 6. Required workflow / lifecycle

Per parent [§7](tool-result-presentation.md) (UI→host) + [§10](tool-result-presentation.md) (authorization). Each action: validate binding active → check scope / visibility → apply risk/consent policy → execute or broker → respond to the UI (result or notification). Every `ui/tool/call` is written to the App Binding audit trail.

## 7. Constraints & compatibility

- **Prompt‑cache stability**: UI tool calls must not alter the model‑visible tool surface.
- **Decoupling invariant**: `ui/tool/call` never creates turns/items (it is `callTool`, not a conversation message).
- **Per‑resource CSP**: widening one iframe's CSP must not affect other iframes or the app shell.
- **Loopback CORS**: an app backend serving data path B must allow the iframe's opaque origin; this is the app's responsibility (to be documented for app authors).

## 8. Acceptance checklist

- [x] A read/no‑approval UI `tools/call` reaches the app via `ui/tool/call`, gated + audited, with **no** conversation turn/item, and the result is delivered to the UI.
- [x] A mutating UI `tools/call` (tool with an approval descriptor) is rejected with an error the UI receives, never executed.
- [x] Out‑of‑scope / non‑`app`‑visible / cross‑binding UI tool calls are rejected with an error the UI receives.
- [x] `ui/open-link` opens `https:`/`mailto:` and rejects `javascript:`/`data:`/`file:` and every other scheme.
- [x] `ui/message` injects a visible user message + agent turn and is rate‑limited.
- [x] `ui/update-model-context` updates model context for the next turn with no visible item; last‑write‑wins; size‑bounded; removed on teardown.
- [x] An app UI can `fetch` its loopback backend under the widened CSP (built host‑side from the server‑validated descriptor); default (no `connectDomains`) still blocks network.
- [x] `hostCapabilities` reflects the enabled actions.
- [x] Conformance tests cover `ui/tool/call` gating (read allowed, mutate rejected), `ui/open-link` scheme policy, and `ui/update-model-context` upsert/clear.

## 9. Resolved decisions

The M‑iii open questions were resolved against the MCP Apps standard (researched: ext‑apps `apps.mdx`, SEP‑1865, OpenAI Apps SDK), favouring standard‑aligned, minimal‑surface choices:

- **`ui/message` gating** → **trust + visible + rate‑limit.** The host cannot verify a real click inside a sandboxed `allow-scripts` iframe, so M‑iii does not pretend to. The message is added as a normal **visible** user turn, **rate‑limited** per binding/item, and audited; the host MAY request consent. This matches MCP Apps (`Host SHOULD add the message… MAY request user consent`) — gesture verification is explicitly out of scope for both the standard and M‑iii.
- **`ui/update-model-context` representation** → **reuse the App Binding context‑block mechanism**, with the `blockId` derived from the originating item id (last‑write‑wins per card) and the block **removed on teardown**. This reuses existing persistence / prompt‑injection / size caps / change notification, matches parent [§7.3](tool-result-presentation.md) and MCP Apps' deferred‑context semantics, and avoids reinventing a per‑item channel.
- **`ui/tool/call` risk** → **M‑iii is read‑only.** Mutating tool calls from a UI are rejected (no turn/item to host approval); the decoupled mutate‑approval UX is designed in **M‑v** with the first real mutating app (Oratorio).
- **`ui/open-link` schemes** → **host‑owned scheme policy** (`https:`/`mailto:` in M‑iii); no ad‑hoc per‑app schemes (consistent with MCP Apps and the OpenAI Apps SDK, neither of which lets servers declare custom schemes). M‑v extends the host policy per binding with the bound app's declared `nativeApplication.protocol` (already a vetted catalog declaration, not an ad‑hoc scheme).
- **Loopback CORS** → **documented for app authors in M‑iii** (opaque iframe origin ⇒ the app backend must send `Access-Control-Allow-Origin: *`, no credentials, loopback only); an **SDK helper is deferred to M‑v** (SDK ergonomics).
