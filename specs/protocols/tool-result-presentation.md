# DotCraft Interactive Tool UI Specification

| Field | Value |
|-------|-------|
| **Version** | 0.8.0 |
| **Status** | Stable |
| **Date** | 2026-06-10 |

Purpose: let an App Binding app present a **rich, interactive UI for tool results** by shipping a sandboxed UI resource that DotCraft Desktop renders in an iframe, with a postMessage JSON‑RPC bridge between the UI and the host. DotCraft adopts the MCP Apps interaction + security model and binds it to DotCraft's App Binding authority: the app plays the MCP‑server role (tools + `ui://` resources) over its trusted, locally‑installed loopback connection; AppServer brokers; Desktop is the host that renders and bridges. Because apps are trusted and locally installed, the sandbox is defense‑in‑depth — authority is still enforced by App Binding.

Interactive UI renders only on Desktop (iframe). Non‑Desktop clients (TUI, channels) receive the text fallback (§12).

---

## 1. Scope

Defines: capability negotiation (§3); UI resource declaration + tool linkage (§4); the tool‑result data‑audience split (§5); sandboxed iframe rendering (§6); the host ⇄ UI bridge (§7); host context / theme / display mode (§8); widget‑state persistence (§9); authorization of UI‑initiated tool calls (§10); security (§11); text fallback (§12); architecture & host/app responsibilities (§13); lifecycle & conversation placement (§14); the Oratorio validation contract (§15); acceptance (§16).

Does not define a declarative block/card vocabulary, cross‑client rich rendering (Desktop‑only), or a replacement for App Binding, Runtime Dynamic Tools, or core MCP.

**Blocking / elicitation cards are out of scope.** Interactive Tool UI cards are **non‑blocking** — the tool returns, the card renders, and the agent continues; the card drives further work through the bridge (`tools/call`, `ui/message`, …), never by pausing the turn. This matches MCP Apps (SEP‑1865), whose UI templates are non‑blocking; mid‑turn structured user input is **core MCP elicitation** (`elicitation/create`) — a separate, schema‑driven, host‑rendered concern that is not a `ui://` card and is not built here.

---

## 2. Model

| Concept | DotCraft binding |
|---------|------------------|
| MCP server (tools + UI resources) | An **App Binding** app: trusted, locally‑installed, bound to a thread over a loopback connection. |
| `resources/read` of a `ui://` resource | Served by the app and brokered by AppServer (§4). |
| `tools/call` from the UI | A call to an app‑bound Runtime Dynamic Tool, gated by App Binding scope/risk/approval/audit (§10). |
| Host consent for UI tool calls | DotCraft's existing approval flow + scope/risk policy. |

---

## 3. Capability Negotiation

Interactive UI is explicitly negotiated. A client that can render it sets the boolean `interactiveToolUi` capability at `initialize` (default `false`). Only DotCraft Desktop declares it; TUI and channel adapters do not and receive the text fallback (§12). For a client that did not declare it, AppServer does not honor the `ui/*` host methods (`ui/resource/read`, `ui/tool/call`, `ui/open-link`, `ui/update-model-context`, `item/widget-state/set`) — they are rejected as unsupported — so a non‑declaring client can neither serve nor drive an app's `ui://` surface.

---

## 4. UI Resources and Tool Linkage

### 4.1 UI resource
- **URI scheme:** `ui://` (e.g. `ui://oratorio/board.html`). Changing the URI is the cache‑bust / version lever.
- **MIME type:** `text/html;profile=mcp-app`.
- **Content:** an HTML document (inline style/script, or a root element plus a bundle whose origin is allowed by CSP, §11). Served by the app on `item/resource/read`, brokered from the host's `ui/resource/read`; predeclared so the host can prefetch and inspect.

### 4.2 Tool → UI linkage (`_meta.ui`)
A tool references its UI in its descriptor `_meta` (not in the result), so the host can preload before completion:

| `_meta.ui` field | Meaning |
|------------------|---------|
| `resourceUri` | The `ui://` resource to render for this tool's result. |
| `visibility` | Who may call the tool (§11) — default `["model","app"]`. |
| `csp` | Sandbox CSP allow‑lists: `connectDomains`, `resourceDomains`, `frameDomains` (§11). |
| `permissions` | Permissions‑Policy grants (e.g. camera, microphone, geolocation, clipboardWrite). |
| `prefersBorder` | Render the host frame with a border. |
| `domain` | The app's canonical domain (display/attribution). |

`_meta.ui` is client‑facing metadata and MUST NOT enter the model‑visible tool description.

---

## 5. Tool Result Data Audience

A dynamic tool result carries three payloads with distinct audiences:

| DotCraft field | Audience | Purpose |
|----------------|----------|---------|
| `contentItems` | **Model only** | Text/image narration the model reads/relays; also the non‑Desktop text fallback. |
| `structuredResult` | **Model + UI** | Concise JSON the UI renders and the model can inspect (ids for follow‑ups). Keep it minimal. |
| `_meta` | **UI only** | Larger or sensitive display data for the UI. **Never reaches the model.** |

The bridge presents these to the iframe as `content`, `structuredContent`, and `_meta`. AppServer MUST exclude `_meta` from the model‑visible value. Oversized `structuredResult` degrades model performance and slows rendering — keep it small.

---

## 6. Rendering

DotCraft Desktop renders the UI resource in a **sandboxed iframe served by a privileged host scheme**:

- **Host scheme + own CSP.** The host registers a privileged scheme (`dotcraft-app://`) and serves the app's `ui://` HTML through it, applying a **per‑resource CSP** to that response so the document carries its **own** CSP, independent of the app‑shell CSP. A `srcdoc` / `blob:` iframe is not used: it would inherit the embedding document's CSP (which forbids inline scripts in production).
- **Sandbox.** `sandbox="allow-scripts"`, **without** `allow-same-origin`: opaque origin; no access to parent DOM/cookies/storage; cannot navigate the parent; no Node.
- **CSP source.** Restrictive by default (network‑denied); widened only from the server‑validated `_meta.ui.csp` (§11): `connectDomains`→`connect-src`, `resourceDomains`→img/style/font/media‑src, `frameDomains`→`frame-src`. The CSP is built host‑side, never from the iframe. The app‑shell CSP must allow framing the host scheme.
- **No runtime injection.** The host injects nothing into the iframe. The app's own HTML/bundle speaks the bridge (§7) to `window.parent` via `postMessage`; the renderer is the host‑side bridge peer and validates both `event.source` and the per-document bridge token returned by the initial handshake.
- The app's bundle mounts into its own root element; DotCraft does not restyle the inner UI, handing theme/locale via host context (§8). The host owns only the surrounding frame (quiet tool/app attribution; sizing).

---

## 7. Host ⇄ UI Bridge

The UI and host communicate via **JSON‑RPC 2.0 over `window.postMessage`** — a `ui/`‑prefixed dialect with reused core methods (`tools/call`). DotCraft implements the host side.

Layering note: method names in this section are the Desktop host ⇄ iframe bridge dialect, not AppServer wire methods. When bridge actions need server authority, Desktop forwards them to AppServer using the server RPC names in [AppServer Protocol](appserver-protocol.md#113-interactive-tool-ui-host-methods): `tools/call` → `ui/tool/call`, UI resource fetch → `ui/resource/read`, widget state persistence → `item/widget-state/set`, and link/context actions → `ui/open-link` / `ui/update-model-context`. `ui/message` and display-mode negotiation are host responsibilities unless a separate AppServer method is specified.

### 7.1 Handshake / lifecycle
1. UI → host **`ui/initialize`** (app capabilities) → host result: **`bridgeToken`** (an unguessable per-frame token for later UI→host requests), **`hostContext`** (§8), **`hostCapabilities`** (`openLinks`, `serverTools`, `updateModelContext`, `message`, `logging`), and the restored **`widgetState`** (§9).
2. Host → UI **`ui/notifications/tool-input`** (the call's arguments), then **`ui/notifications/tool-result`** (`content` + `structuredContent` + `_meta` + `isError`).
3. Teardown: host → UI **`ui/resource-teardown`** (§14).

### 7.2 Host → UI notifications
- `ui/notifications/tool-input`, `ui/notifications/tool-input-partial` (streamed/healed partial args), `ui/notifications/tool-result`.
- `ui/notifications/host-context-changed` (theme / locale / display mode / dimensions changed, §8).
- `ui/resource-teardown`.

### 7.3 UI → host requests

Every UI→host bridge request that asks the host to act (`tools/call`, `ui/open-link`, `ui/message`, `ui/update-model-context`, `ui/request-display-mode`, `ui/set-widget-state`) MUST include the top-level `bridgeToken` returned by the first successful `ui/initialize`. The initial handshake must occur before the first iframe load completes; duplicate or late initialization is rejected and disables the bridge. Host actions before initialization, after bridge disablement, or with a missing/wrong token are rejected or ignored. The host also disables the bridge when the iframe navigates away from the served resource so a new document in the same frame/window proxy cannot inherit the previous app document's authority.

| Request | Use | DotCraft handling |
|---------|-----|-------------------|
| `tools/call` | Invoke an app‑bound dynamic tool | Forwarded by Desktop as AppServer `ui/tool/call`, gated by App Binding (§10), **decoupled** from the conversation (no turn/item), audited; result returned to the UI only. The model learns of UI state only via `ui/update-model-context` or `ui/message`. |
| `ui/open-link` | Open a URL | **No tool call.** Host‑owned scheme policy (§11): `https:` / `mailto:` and the bound app's declared deep‑link protocol; all others rejected. When server policy/audit is needed, Desktop forwards the action as AppServer `ui/open-link`. |
| `ui/message` | Send a follow‑up user message → triggers a model turn | Added as a **visible** user message and a normal turn, **rate‑limited**; host MAY request consent. The iframe gesture is not host‑verifiable, so it is not verified. |
| `ui/update-model-context` | Feed UI state to the model's next turn | Forwarded by Desktop as AppServer `ui/update-model-context`; recorded as an App Binding context block (`visibility:"model"`), keyed to the originating item, **last‑write‑wins**, size‑bounded; **removed on teardown**. No turn/item. |
| `ui/request-display-mode` | Request `inline` / `pip` / `fullscreen` | Host returns the **granted** mode (may differ; §8). Must be user‑initiated. |

Not every action is a tool call: a button may open a link, `fetch` the app's own backend (under CSP `connect-src`, §11), message the thread, or call a tool — the app chooses per action.

### 7.4 Host introspection (from the UI)
- `getHostContext()` → `theme`, `displayMode`, `locale`, `timeZone`, `platform`, `containerDimensions`, `availableDisplayModes`.
- `getHostCapabilities()` → the static capability flags above.
- `getHostVersion()` → `{ name, version }`.

---

## 8. Host Context, Theme & Display Mode

The host pushes a context object at `ui/initialize` and on change via `ui/notifications/host-context-changed`:

- `theme` (`light`/`dark`), `locale`, `timeZone`, `platform`.
- `displayMode` (`inline`/`pip`/`fullscreen`) + `availableDisplayModes`.
- `maxHeight`, `safeArea.insets`, `containerDimensions`.
- Host CSS variables, so the UI can match the desktop theme if it opts in.

**Live push.** When Desktop theme, locale, or display mode changes, the host re‑emits `host-context-changed`; the UI re‑themes/re‑localizes **without reload**.

**Display modes.** `ui/request-display-mode` is user‑initiated; the host arbitrates and returns the granted mode. `inline` is the default in‑conversation surface; `pip` is a floating corner window; `fullscreen` is a portal overlay over the conversation (backdrop + close). On a narrow window the host coerces `pip`→`fullscreen`. While a card is expanded, its inline slot shows a placeholder with a Collapse affordance, so only one live iframe exists per card; re‑mounting in the expanded surface relies on `widgetState` restore (§9) to preserve state.

---

## 9. Widget State Persistence

- The UI may persist a `widgetState` (UI‑only state: selected row, expanded panel, staged input) via the bridge; the host persists it **keyed to the originating `dynamicToolCall` item**, asynchronously (the UI need not await). Desktop writes the server-side value through AppServer `item/widget-state/set` when the state must survive reload or cross-client reads.
- Because the canonical thread rollout is append‑only / event‑sourced, `widgetState` is stored in a **dedicated mutable per‑thread side store**, surfaced on the item's payload on `thread/read`, and written back via that decoupled set method (no turn/item).
- It is **restored in the `ui/initialize` result** (alongside `hostContext`), so it is present at/before first paint — no flash of a stale or empty card. Restore survives scroll‑away, thread reload, and app restart.
- **UI‑only** — it never reaches the model unless the UI explicitly calls `ui/update-model-context`. **Size‑bounded** (≤ 8 KB per item; oversized updates rejected).
- It is layered on top of the server‑authoritative `structuredResult`, which is re‑applied from the tool result each turn. `widgetState` is never authoritative data.

---

## 10. Authorization

A UI‑initiated bridge `tools/call` carries no authority of its own; DotCraft re‑derives and enforces it at the AppServer `ui/tool/call` boundary:

- The target tool MUST be app‑bound to the current thread, `app`‑visible (§11), and within the binding's granted scope.
- **Risk gating.** A `read` / no‑approval tool proceeds. A `mutate` / `externalWrite` tool (one that declares an approval descriptor) raises a **decoupled approval** that reuses Desktop's existing approval surface; the `ui/tool/call` awaits the decision, then dispatches or rejects. The approval is **transient** (keyed to the thread + approval id) — no turn is created and no persisted conversation item — so decoupling is preserved; every decision is audited.
- Cross‑binding / cross‑app tool calls from a UI are rejected; out‑of‑scope or non‑`app`‑visible calls are rejected with an error the UI receives.
- Every UI‑initiated `tools/call`, approval, and `ui/open-link` is recorded on the App Binding audit trail.
- `ui/message` and `ui/update-model-context` inherit normal turn / context‑block semantics.

The UI's access to its own app backend (direct `fetch`) is governed by CSP `connect-src` (§11), not DotCraft tool authority — the app talks to itself over its declared loopback origin.

---

## 11. Security

- **Visibility** (`_meta.ui.visibility`, default `["model","app"]`): `["app"]` = UI‑only (callable from the UI, hidden from the model); `["model"]` = model‑only. AppServer enforces visibility when building the model tool list and validating UI `tools/call`.
- **Sandbox & CSP:** mandatory iframe sandbox; restrictive default CSP, widened only from the server‑validated `_meta.ui.csp`. Widening one iframe's CSP must not affect other iframes or the app shell.
- **Permissions:** the iframe is granted **only** the powerful features the app declares in `_meta.ui.permissions` (`camera`, `microphone`, `geolocation`, `clipboardWrite`), mapped from the server‑validated descriptor onto the iframe's Permissions‑Policy `allow`. Unknown tokens are dropped; with none declared, every powerful feature is denied (deny‑by‑default).
- **Links:** `ui/open-link` is governed by a **host‑owned scheme policy** — `https:`, `mailto:`, and the bound app's declared `nativeApplication.protocol` deep‑link scheme (binding‑scoped — a vetted catalog declaration, not an ad‑hoc per‑app scheme). `javascript:` / `data:` / `file:` and every other scheme are forbidden. Blocked opens are audited.
- **Loopback fetch (data path B):** an app backend serving the iframe's direct `fetch` must allow the iframe's opaque origin (CORS, loopback only, no credentials). This is the app's responsibility.
- **Bridge authorization:** `event.source` is necessary but not sufficient because a sandboxed iframe can self-navigate while retaining the same frame/window proxy. Desktop mints a per-frame `bridgeToken` during the initial `dotcraft-app://` document handshake, requires it on all UI→host actions, and disables the bridge on duplicate initialization or iframe navigation.
- **Auditable + inspectable:** all UI→host traffic is JSON‑RPC (loggable); predeclared `ui://` resources are inspectable before render. The host bounds resource size, iframe count, and message rate.

---

## 12. Fallback (non‑Desktop)

Clients that did not negotiate `interactiveToolUi` (TUI, chat channels) and any failure to render MUST fall back to the tool result's text — `contentItems` / `structuredResult` / error fields, with `_meta` excluded. Apps MUST always return useful text; the interactive UI is an enhancement, never required for correctness.

---

## 13. Architecture & Host/App Responsibilities

Three actors: **AppServer** brokers MCP between the host and the app; the **app** plays the MCP‑server role (tools + `ui://` resources) over its App Binding connection; **Desktop** is the host that renders and bridges.

The host owns:
- **Sandbox iframe + host scheme** — one sandboxed iframe per UI‑bearing `dynamicToolCall`; the privileged `dotcraft-app://` scheme with a per‑resource CSP; no runtime injection; `maxHeight` / display‑mode enforcement.
- **Bridge runtime** — the `ui/*` + `tools/*` JSON‑RPC peer over postMessage; push `hostContext` / notifications; service the UI→host requests; validate `event.source` plus `bridgeToken`, and disable the bridge when the iframe navigates away.
- **Resource fetch + cache** — broker `ui/resource/read`; cache by URI; refetch when the URI changes.
- **Tool‑call proxy + consent** — enforce visibility + scope/risk/approval; forward the call; return the result; hide `app`‑only tools from the model.
- **State persistence** — persist `widgetState` keyed to the item; route `ui/update-model-context` (deferred) and `ui/message` (immediate turn).
- **Theme / display handoff** — compute and push theme/locale/displayMode/dimensions; arbitrate display‑mode requests; expose host CSS variables.

The app owns: the HTML template / root element / bundle and its own bridge code; the tool + `ui://` resource declarations; the result audience split (§5); the choice of `widgetState` vs `structuredResult` vs `_meta`. The SDK provides folder/prefix static serving so an app exposes a folder of `ui://` resources without per‑URI boilerplate.

Net: the host is the trust / arbitration boundary; the app owns its declarations, bundle, and in‑iframe logic.

---

## 14. Lifecycle & Placement

- A UI instance is bound to a tool‑call result. The host renders after the `dynamicToolCall` item completes and sends `tool-result`. Results are atomic; optional streaming partial args flow via `tool-input-partial`.
- **Placement.** Once a turn settles, a non‑blocking interactive card is **pinned** out of the collapsed turn summary — rendered standalone above the final agent message — rather than folded into the intermediate "Processed" disclosure like ordinary tool output. Only the last completed interactive card of a turn is pinned (earlier, superseded cards stay collapsed); pinning composes with the plan‑card pin. Deep‑history turns beyond the recent window trim their tool content (including the card's result data), so pinning applies to the live render path only.
- On thread close, item teardown, or navigation away, the host sends `ui/resource-teardown` when still talking to the original document, disables/disposes the iframe bridge, and clears the model‑context block (§10).

---

## 15. Oratorio Validation Contract

Oratorio is the first validating app. It ships UI resources in its bundle, declares `_meta.ui` on its catalog tools, and always returns `structuredResult` text for non‑Desktop fallback.

| Tool | `resourceUri` | Behavior |
|------|---------------|----------|
| `ListBoardItems` | `ui://oratorio/board.html` | Interactive board; "Open in Oratorio" via `ui/open-link`; refresh via `tools/call`. |
| `GetBoardItem` | `ui://oratorio/item.html` | One item + activity; app‑open via `ui/open-link`. |
| `QueueReviewRound` | `ui://oratorio/review.html` | Queue via `tools/call` (`externalWrite` → decoupled approval, §10). |

---

## 16. Acceptance

- Desktop negotiates `interactiveToolUi`; other clients fall back to tool‑result text.
- A tool with `_meta.ui.resourceUri` renders its `ui://` resource in a sandboxed iframe.
- The bridge supports the tokenized handshake + `tools/call`, `ui/open-link`, `ui/message`, `ui/update-model-context`, `ui/request-display-mode`, and host introspection.
- Audience split enforced: `_meta` never reaches the model; `structuredResult` reaches model + UI; `contentItems` is the model / text fallback.
- `visibility:["app"]` tools are hidden from the model but callable from the UI.
- UI‑initiated `tools/call` is gated by scope/risk/approval and audited; mutating calls raise a decoupled approval; cross‑binding calls rejected.
- Sandbox + CSP + permissions + `ui/open-link` scheme policy enforced.
- Host context (theme/locale/displayMode) is pushed live; `widgetState` persists and restores per item.
- A non‑blocking interactive card is pinned out of the collapsed turn summary.
- The interactive UI is never required for correctness; text fallback always present.
