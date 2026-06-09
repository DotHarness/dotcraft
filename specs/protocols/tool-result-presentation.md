# DotCraft Interactive Tool UI Specification (MCP Apps)

| Field | Value |
|-------|-------|
| **Version** | 0.7.0 |
| **Status** | Draft |
| **Date** | 2026-06-09 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |
| **Related Specs** | [App Binding](app-binding.md), [Desktop Client](../clients/desktop-client.md), [Plugin Architecture](../extensions/plugin-architecture.md), [Session Core](../core/session-core.md) |
| **Aligns with** | MCP Apps — SEP‑1865 (`io.modelcontextprotocol/ui`, [`ext-apps`](https://github.com/modelcontextprotocol/ext-apps)); OpenAI Apps SDK as the secondary/interop reference |

Purpose: let an App Binding app present a **rich, interactive UI for tool results** by shipping a sandboxed UI resource that DotCraft Desktop renders in an iframe, with a postMessage JSON‑RPC bridge between the UI and the host. This spec **adopts the MCP Apps model (SEP‑1865)** and binds it to DotCraft's App Binding authority model.

> **Direction note.** This replaces the earlier *declarative Dynamic Tool Card* approach (a fixed block vocabulary, v0.1–v0.4), which was removed. Full MCP Apps alignment is chosen for maximum UI flexibility and MCP‑ecosystem interop. Trade‑off accepted: interactive UI renders only on Desktop (iframe); non‑Desktop clients fall back to text (§12).

> **Standard‑alignment rule.** New surfaces use the **MCP Apps standard** identifiers verbatim (`ui://`, `text/html;profile=mcp-app`, `_meta.ui.*`, `ui/*` + `tools/*` bridge methods). The OpenAI Apps SDK aliases (`openai/outputTemplate`, `text/html+skybridge`, `window.openai.*`) are documented only where a future interop shim (§13.7) would map onto them. Where this spec reuses **existing** DotCraft dynamic‑tool wire fields (`contentItems`, `structuredResult`), it keeps those names and documents the standard mapping (§5).

---

## 1. Scope

Defines: capability negotiation (§3); UI resource declaration + tool linkage (§4); the tool‑result **data audience** split (§5); sandboxed iframe rendering (§6); the host ⇄ UI **bridge** (§7); host context / theme / display mode (§8); widget state persistence (§9); authorization of UI‑initiated tool calls (§10); security (§11); text fallback (§12); **architecture & host responsibilities** (§13); lifecycle (§14).

Does not define: a declarative block/card vocabulary, `app.request`, or `cardSurfaceRoutes` (all removed); cross‑client rich rendering (Desktop‑only); a replacement for App Binding, Runtime Dynamic Tools, or core MCP.

---

## 2. Relationship to MCP Apps (SEP‑1865)

DotCraft adopts the MCP Apps interaction + security model. The normative protocol is [`modelcontextprotocol/ext-apps`](https://github.com/modelcontextprotocol/ext-apps) (extension `io.modelcontextprotocol/ui`); this document defines the DotCraft bindings:

| MCP Apps concept | DotCraft binding |
|------------------|------------------|
| MCP server (tools + UI resources) | An **App Binding** app: a trusted, locally‑installed app bound to a thread, reachable over a loopback connection. |
| `resources/read` of a `ui://` resource | Served by the app and brokered by AppServer (§4, [AppServer Protocol](appserver-protocol.md)). |
| `tools/call` from the UI | A call to an **app‑bound Runtime Dynamic Tool**, dispatched via `item/tool/call`, gated by App Binding scope/risk/approval/audit (§10). |
| Host consent for UI tool calls | DotCraft's existing approval flow + scope/risk policy. |

Because DotCraft apps are **trusted, locally‑installed** (not arbitrary remote servers), the sandbox is defense‑in‑depth; authority is still enforced by App Binding.

---

## 3. Capability Negotiation

Optional and explicitly negotiated. A client that can render interactive UI advertises it at `initialize`:

```json
"capabilities": { "interactiveToolUi": { "mimeTypes": ["text/html;profile=mcp-app"] } }
```

Only DotCraft **Desktop** advertises `interactiveToolUi`. TUI/channel adapters do not and receive the text fallback (§12). AppServer MUST NOT send UI resources or expect bridge traffic for a client that did not negotiate it.

---

## 4. UI Resources and Tool Linkage

### 4.1 UI Resource
- **URI scheme:** `ui://` (e.g. `ui://oratorio/board.html`). Changing the URI is the cache‑bust / version lever.
- **MIME type:** `text/html;profile=mcp-app`.
- **Content:** an HTML document (inline `<style>`/`<script>`, or a root div + `<script src=…>` whose origin is allowed by CSP `resourceDomains`, §11). Served by the app on **`item/resource/read`**, brokered from the host's **`ui/resource/read`** ([AppServer Protocol §11.3.1](appserver-protocol.md)); predeclared so the host can prefetch and inspect.

### 4.2 Tool → UI linkage (`_meta.ui`)
A tool references its UI in its descriptor `_meta` (not in the result), so the host can preload before completion:

```json
{
  "namespace": "oratorio",
  "name": "ListBoardItems",
  "inputSchema": { "type": "object", "properties": {} },
  "_meta": {
    "ui": {
      "resourceUri": "ui://oratorio/board.html",
      "visibility": ["model", "app"],
      "prefersBorder": true,
      "csp": { "connectDomains": ["https://127.0.0.1"], "resourceDomains": [], "frameDomains": [] },
      "permissions": [],
      "domain": "oratorio.dotharness.com"
    }
  }
}
```

| `_meta.ui` field | Meaning | OpenAI alias |
|------------------|---------|--------------|
| `resourceUri` | `ui://` resource to render for this tool's result | `openai/outputTemplate` |
| `visibility` | Who may call the tool (§7) | `openai/visibility` |
| `csp` | Sandbox CSP allow‑lists (§11) | `openai/widgetCSP` |
| `permissions` | Permissions‑Policy grants (§11) | — |
| `prefersBorder` | Render the host frame with a border | `openai/widgetPrefersBorder` |
| `domain` | App's canonical domain | `openai/widgetDomain` |

`_meta.ui` is client‑facing metadata and MUST NOT enter the model‑visible tool description.

---

## 5. Tool Result Data Audience

A dynamic tool result carries three payloads with **distinct audiences**, mirroring MCP Apps' `content` / `structuredContent` / `_meta`:

| DotCraft field (wire) | MCP Apps name | Audience | Purpose |
|-----------------------|---------------|----------|---------|
| `contentItems` | `content` | **Model only** | Text/image narration the model reads/relays; also the non‑Desktop text fallback. |
| `structuredResult` | `structuredContent` | **Model + UI** | Concise JSON the UI renders and the model can inspect (ids for follow‑ups). Keep it minimal. |
| `_meta` | `_meta` | **UI only** | Larger or sensitive display data for the UI. **Never reaches the model.** |

- DotCraft keeps the existing wire names `contentItems` / `structuredResult` (used across all dynamic tools) and adds **`_meta`** for UI‑only data. The bridge presents them to the iframe under the standard names (§7): `structuredContent` ← `structuredResult`, plus `_meta`.
- AppServer MUST exclude `_meta` from the model‑visible value, exactly as it already excludes client‑facing metadata. (Naming note: keeping `contentItems`/`structuredResult` vs. renaming to the standard `content`/`structuredContent` is an open preference — see §13.7.)
- Keep `structuredResult` small; oversized payloads degrade model performance and slow rendering.

---

## 6. Rendering

DotCraft Desktop renders the UI resource in a **sandboxed iframe served by a privileged host scheme**:

- **Host scheme + own CSP.** The host registers a privileged scheme (`dotcraft-app://`) and serves the app's `ui://` HTML through it, applying a **per‑resource CSP** to that response so the document has its **own** CSP, independent of the app‑shell CSP. A `srcdoc` / `blob:` iframe is **not** used: it would inherit the embedding document's CSP (which forbids inline scripts in production), and a `<meta>` CSP can only further restrict it.
- **Sandbox.** `sandbox="allow-scripts"` and **without** `allow-same-origin`: opaque origin; no access to the parent DOM/cookies/storage; cannot navigate the parent; no Node.
- **CSP source.** Restrictive by default; widened only from `_meta.ui.csp` (§11): `connectDomains`→`connect-src`, `resourceDomains`→img/style/font/media‑src, `frameDomains`→`frame-src`. The app‑shell CSP must allow framing the host scheme (`frame-src`).
- **No runtime injection.** The host injects nothing into the iframe. The app's own HTML/bundle speaks the bridge (§7) to `window.parent` via `postMessage`; the **renderer is the host‑side bridge peer** and validates `event.source` is the iframe. Apps MAY use `@mcp-ui/client` / the ext‑apps App Bridge, but that is the app's choice, not host‑injected.
- The app's bundle mounts into its own root element; DotCraft does not restyle the inner UI, handing theme/locale via host context (§8). The host owns only the surrounding frame (quiet tool/app attribution; sizing). See [DESIGN.md](../clients/DESIGN.md) → Interactive Tool UI.

---

## 7. Host ⇄ UI Bridge

The UI and host communicate via **JSON‑RPC 2.0 over `window.postMessage`** — a `ui/`‑prefixed dialect of MCP per SEP‑1865, with reused core methods (`tools/call`). DotCraft implements the host side; the app's UI uses it directly or via `@mcp-ui/client` / the ext‑apps App Bridge.

### 7.1 Lifecycle / handshake
1. UI → host: **`ui/initialize`** (app capabilities) → host result: **`hostContext`** (§8) + **`hostCapabilities`** (`openLinks`, `serverTools`, `updateModelContext`, `message`, `logging`). In M‑ii these capabilities are all `false` (read‑only host); M‑iii flips them on.
2. Host → UI: **`ui/notifications/tool-input`** (the call's arguments), then **`ui/notifications/tool-result`** (`content` + `structuredContent` + `_meta` + `isError`), sent after the `ui/initialize` result. (`ui/notifications/initialized` from the UI is optional; the host does not await it.)
3. Teardown: host → UI **`ui/resource-teardown`** (§14).

### 7.2 Host → UI notifications
- `ui/notifications/tool-input`, `ui/notifications/tool-input-partial` (streamed/healed partial args), `ui/notifications/tool-result`.
- `ui/notifications/host-context-changed` (theme / display mode / locale / dimensions changed, §8).
- `ui/resource-teardown`.

### 7.3 UI → host requests (the flexible action surface)

| Request | Use | DotCraft handling |
|---------|-----|-------------------|
| `tools/call` | Invoke an app‑bound dynamic tool (MCP Apps `callTool`) | Host forwards to AppServer as `ui/tool/call` ([AppServer Protocol](appserver-protocol.md) §11.3.2): gated by App Binding (§10), dispatched via `item/tool/call`, **decoupled from the conversation** (no turn/item) and audited; result returned to the UI. The model learns of UI state only via `ui/update-model-context` or `ui/message`. |
| `ui/open-link` | Open an `https:` / `mailto:` URL | **No tool call.** Host‑owned scheme policy (§11); apps do not declare custom schemes. |
| `ui/message` | Send a follow‑up user message → triggers a model turn | Routed through `turn/start` / `turn/enqueue`. Shape: `{ role:"user", content:[{type:"text",text}] }`. Added as a **visible** user message, **rate‑limited**; host MAY request consent. The iframe gesture is not host‑verifiable, so it is not verified (aligns with MCP Apps `ui/message`). |
| `ui/update-model-context` | Feed UI state to the model's next turn (silent, deferred, last‑write‑wins) | Recorded as an App Binding context block; no Turn/Item. |
| `ui/request-display-mode` | Request `inline`/`pip`/`fullscreen` | Host returns the **granted** mode (may differ; §8). Must be user‑initiated. |

**Not every action is a tool call.** The UI is the app's code: a button may open a link, `fetch` the app's own backend (under CSP `connect-src`, §11), message the thread, or call a tool — the app chooses per action. `tools/call` is one capability among several.

### 7.4 Host introspection (from the UI)
- `getHostContext()` → dynamic: `theme`, `displayMode`, `locale`, `timeZone`, `platform`, `containerDimensions`, `availableDisplayModes`.
- `getHostCapabilities()` → static capability flags (above).
- `getHostVersion()` → `{ name, version }`.

---

## 8. Host Context, Theme & Display Mode

The host pushes a context object to the UI at `ui/initialize` and on change via `ui/notifications/host-context-changed`:

- `theme` (`"light" | "dark"`), `locale`, `timeZone`, `platform`.
- `displayMode` (`"inline" | "pip" | "fullscreen"`) + `availableDisplayModes`.
- `maxHeight`, `safeArea.insets` (`top/bottom/left/right`), `containerDimensions`.
- Host CSS variables so the UI can match the desktop theme via `var(--…)` if it opts in.

`ui/request-display-mode` returns the **granted** mode; the host may reject (and SHOULD coerce/limit on constrained surfaces). The UI must request mode changes only from a user gesture.

---

## 9. Widget State Persistence

- The UI may persist a `widgetState` (UI‑only state: selected row, expanded panel) via the bridge; the host persists it **keyed to the originating `dynamicToolCall` item** in the thread, asynchronously (the UI need not await).
- On re‑open of that item, the host restores `widgetState` into the UI's initial context.
- `widgetState` is layered on top of the server‑authoritative `structuredResult`, which is re‑applied from the tool result each turn. `widgetState` MUST NOT be treated as authoritative data.

---

## 10. Authorization

UI‑initiated `tools/call` carries no authority of its own; DotCraft re‑derives and enforces it:

- The target tool MUST be app‑bound to the current thread, `app`‑visible (§7/§11 visibility), and within the binding's granted scope.
- Risk gating reuses App Binding ([app-binding.md §5.4](app-binding.md)): `read` may proceed (consent per `readOnlyHint`); `mutate`/`externalWrite` require explicit user approval through the existing approval flow; `externalWrite` prefers propose→record→approve→app‑writes. **Phased delivery:** M‑iii ships UI tool calls **read‑only** (mutating calls rejected, since a decoupled call has no turn/item to host the approval); the decoupled mutate‑approval UX lands in M‑v ([M‑iii §9](tool-result-presentation-m3.md)).
- DotCraft MUST reject cross‑binding / cross‑app tool calls from a UI.
- Every UI‑initiated `tools/call`, approval, and `ui/open-link` is recorded on the App Binding audit trail.
- `ui/message` and `ui/update-model-context` inherit normal turn / context‑block semantics.

The UI's access to its **own app backend** (direct `fetch`) is governed by CSP `connect-src` (§11), not DotCraft tool authority — the app talks to itself over its declared loopback origin.

---

## 11. Security

- **Visibility** (`_meta.ui.visibility`, default `["model","app"]`): `["app"]` = UI‑only (callable from the UI, hidden from the model); `["model"]` = model‑only. AppServer enforces visibility when building the model tool list and validating UI `tools/call`.
- **Sandbox & CSP:** mandatory iframe sandbox; restrictive default CSP, widened by `_meta.ui.csp` — `connectDomains`→`connect-src`, `resourceDomains`→`img/script/style/font/media-src`, `frameDomains`→`frame-src`, `baseUriDomains`→`base-uri`.
- **Permissions:** `_meta.ui.permissions` (`camera`, `microphone`, `geolocation`, `clipboardWrite`) → Permissions‑Policy.
- **Links:** `ui/open-link` is governed by a **host‑owned scheme policy** — `https:` and `mailto:` only; `javascript:`/`data:`/`file:` and every other scheme forbidden. Apps do **not** declare custom schemes (consistent with MCP Apps / OpenAI Apps SDK, which leave scheme handling to the host); app deep‑links route through the app‑handoff mechanism, not `ui/open-link`.
- **Auditable + inspectable:** all UI→host traffic is JSON‑RPC (loggable); predeclared `ui://` resources are inspectable before render. The host SHOULD bound resource size, iframe count, and message rate.

---

## 12. Fallback (non‑Desktop)

Clients that did not negotiate `interactiveToolUi` (TUI, chat channels) and any failure to render MUST fall back to the tool result's text — `contentItems` / `structuredResult` / error fields. Apps MUST always return useful text; the interactive UI is an enhancement, never required for correctness.

---

## 13. Architecture & Host Responsibilities

The interactive‑UI system spans three actors. **AppServer** brokers MCP between the host and the app; the **app** plays the MCP‑server role (tools + `ui://` resources) over its App Binding connection; **Desktop** is the host that renders + bridges.

```
model ──tools/call──▶ AppServer ──item/tool/call──▶ app (tool handler)
                         │  resources/read(ui://)  ▲
Desktop (host)           ▼                          │
  iframe(UI) ◀─bridge─ renderer ──tools/call (UI)──┘  (gated by App Binding)
```

Desktop Host owns six subsystems (host‑owns vs app‑owns):

| Subsystem | Host owns | App owns |
|-----------|-----------|----------|
| **(a) Sandbox iframe + host scheme** | One sandboxed iframe per UI‑bearing `dynamicToolCall`; `sandbox="allow-scripts"` (no `allow-same-origin`); the app HTML is served via the privileged `dotcraft-app://` scheme with a **per‑resource CSP** built from `_meta.ui.csp`; **no runtime injection**; enforce `maxHeight` / display mode. | HTML template, root element, JS/CSS bundle (its own bridge code), mounting. |
| **(b) Bridge runtime** | Implement the `ui/*` + `tools/*` JSON‑RPC peer over postMessage; push `hostContext`/notifications; service `tools/call`, `ui/open-link`, `ui/message`, `ui/update-model-context`, `ui/request-display-mode`, introspection; validate `event.source` is the iframe. | Consume via `useApp()`/the App Bridge. |
| **(c) Resource fetch + cache** | On a tool with `_meta.ui.resourceUri`, the `dotcraft-app://` handler brokers **`ui/resource/read`** (→ app `item/resource/read`, [AppServer §11.3.1](appserver-protocol.md)); cache by URI; refetch when the URI changes. | `item/resource/read` handler serving `ui://` HTML; `ui://` naming; bundle hashing for versions. |
| **(d) Tool‑call proxy + consent** | Receive UI `tools/call`; enforce visibility + binding scope/risk/approval; forward via `item/tool/call`; return result; hide `app`‑only tools from the model. | Declare `visibility`; tool handlers; result audience split (§5). |
| **(e) State persistence** | Persist `widgetState` keyed to the thread item; restore on re‑open; re‑apply `structuredResult` each turn; route `ui/update-model-context` (deferred) and `ui/message` (immediate turn). | Choose `widgetState` (UI) vs `structuredResult` (model+UI) vs `_meta` (UI‑only). |
| **(f) Theme / display handoff** | Compute + push `theme`/`locale`/`displayMode`/`maxHeight`/`safeArea`; re‑emit on change; expose host CSS vars; arbitrate `ui/request-display-mode`. | React to context; use host CSS vars; request modes from user gestures only. |

**Net:** the host is the trust/arbitration boundary (sandbox, CSP, bridge, consent on tool calls + link opens, state keyed to the conversation, display arbitration); the app owns the tool/resource declarations, the bundle, and in‑iframe logic.

### 13.7 Open preferences (to confirm)
- **Result field names:** keep DotCraft `contentItems`/`structuredResult` (+ add `_meta`) vs. rename to the standard `content`/`structuredContent`. Recommendation: keep existing names + document the mapping (this spec) to avoid churn across all dynamic tools.
- **`window.openai` interop shim:** whether the host also injects an OpenAI‑compatible `window.openai` global (so apps built for ChatGPT run unchanged). Recommendation: ship the standard `ui/*` bridge first; add the shim later if interop is wanted.

---

## 14. Lifecycle

- A UI instance is bound to a tool‑call result. The host renders after the `dynamicToolCall` item completes and sends `ui/notifications/tool-result`.
- On thread close, item teardown, or navigation away, the host sends `ui/resource-teardown` and disposes the iframe.
- Dynamic tool results are atomic; optional streaming partial args flow via `ui/notifications/tool-input-partial`.

---

## 15. Oratorio Validation Contract

Oratorio is the first validating app. It ships UI resources in its bundle and declares `_meta.ui.*` on its catalog tools; it always returns text (`structuredResult`) for non‑Desktop fallback.

| Tool | `_meta.ui.resourceUri` | Expected value |
|------|------------------------|----------------|
| `oratorio.ListBoardItems` | `ui://oratorio/board.html` | Interactive board; "Open in Oratorio" = `ui/open-link` (no tool call); refresh = the UI `fetch`es its loopback backend (CSP `connectDomains`). |
| `oratorio.GetBoardItem` | `ui://oratorio/item.html` | One item + activity; app‑open via `ui/open-link`. |
| `oratorio.QueueReviewRound` | `ui://oratorio/review.html` | Queue via `tools/call` (`externalWrite` → approval) or an app‑side operation request. |

---

## 16. Acceptance Checklist

- Desktop negotiates `interactiveToolUi`; other clients fall back to tool‑result text.
- A tool with `_meta.ui.resourceUri` renders its `ui://` resource (fetched via `resources/read`) in a sandboxed iframe.
- The host ⇄ UI bridge supports the lifecycle + `tools/call`, `ui/open-link`, `ui/message`, `ui/update-model-context`, `ui/request-display-mode`, and host introspection.
- Tool‑result audience split enforced: `_meta` never reaches the model; `structuredResult` reaches model + UI; `contentItems` is the model/text‑fallback.
- `_meta.ui.visibility: ["app"]` tools are hidden from the model but callable from the UI.
- UI‑initiated `tools/call` is gated by App Binding scope/risk/approval and audited; cross‑binding calls rejected.
- Sandbox + CSP + permissions enforced; `ui/open-link` scheme allow‑list enforced.
- Host context (theme/displayMode/locale) is pushed and updated; `widgetState` persists + restores per item.
- The interactive UI is never required for correctness; text fallback always present.

## 17. Delivery Milestones

This spec ships in milestones; each has a dedicated behavior‑contract spec. Implement one milestone at a time and validate against its acceptance checklist.

| Milestone | Scope | Status | Spec |
|-----------|-------|--------|------|
| **M‑i** | C# protocol: `_meta.ui` + `visibility`, `ui/resource/read`, `ui/tool/call` | ✅ Delivered | this spec §4–§5, §10; [AppServer §11.3.1–§11.3.2](appserver-protocol.md) |
| **M‑ii** | Desktop iframe host (read‑only): `dotcraft-app://` scheme + per‑resource CSP, sandboxed iframe, bridge handshake + tool‑input/result push | ✅ Delivered | this spec §6–§8 |
| **M‑iii** | Bridge actions: `ui/tool/call` (read‑only), `ui/open-link`, `ui/message`, `ui/update-model-context`, data‑path‑B CSP widening | ✅ Delivered | [M‑iii](tool-result-presentation-m3.md) |
| **M‑iv** | Live host‑context push, `widgetState` persistence, display mode (pip + fullscreen overlay) | ✅ Delivered | [M‑iv](tool-result-presentation-m4.md) |
| **M‑v** | Decoupled mutate‑approval + SDK folder‑serving (`ServeStaticUiResources`) + non‑Desktop fallback test (Oratorio = external validation) | ✅ In‑repo delivered | [M‑v](tool-result-presentation-m5.md) |
| **M‑vi** | Capability negotiation, security & acceptance hardening | Planned (not designed) | [M‑vi](tool-result-presentation-m6.md) |

M‑i–M‑iv are implementation‑complete and conformance‑tested (C# protocol gating + Desktop bridge/store unit tests; the `InteractiveToolSample` app exercises `ui/open-link`, `ui/update-model-context`, `widgetState`, and `ui/request-display-mode`). M‑v is design‑locked (its decoupled‑approval UI is de‑risked — the Desktop approval composer was generalized for reuse). M‑vi is not yet designed.

## 18. Implementation Landmarks (as built: M‑i, M‑ii, M‑iii)

Where the delivered M‑i/M‑ii/M‑iii code lives, so M‑iv+ extends the right places.

**M‑iii additions (bridge actions)**
- New host→server wire types `UiOpenLinkParams`/`UiOpenLinkResult`, `UiUpdateModelContextParams`/`UiUpdateModelContextResult`, and `UiResourceReadResult.Csp` (host‑populated descriptor CSP): `src/DotCraft.Core/Protocol/AppServer/AppServerProtocol.cs`.
- `OpenLink` (https/mailto scheme policy + audit), `UpdateModelContext` (context block keyed `ui:<callId>`, kind `uiModelContext`, last‑write‑wins, cleared on teardown), the read‑only gate in `InvokeUiToolAsync` (`spec.Approval != null` ⇒ `AppBindingApprovalRequired`), and CSP fill in `ReadUiResourceAsync`: `src/DotCraft.Core/AppBinding/AppBindingService.cs`. New kind `AppContextBlockKinds.UiModelContext` + error `AppBindingErrorCodes.ApprovalRequired`: `AppBindingModels.cs`. Method routes `ui/open-link` / `ui/update-model-context`: `AppBindingProtocolExtension.cs`.
- Desktop main: per‑resource CSP widening from `result.csp` (`buildInteractiveToolCsp(csp)` + `sanitizeCspDomains`): `desktop/src/main/dotcraftAppProtocol.ts`.
- Desktop renderer: live bridge peer (`tools/call`, `ui/open-link`, `ui/update-model-context`, `ui/message` with rate‑limit, teardown clear, `hostCapabilities` flipped on): `components/conversation/InteractiveToolView.tsx`. `ui/message` reuses `utils/startTurn.ts`; tool calls/links go through `window.api.appServer.sendRequest` / `window.api.shell.openExternal`.

**M‑i / M‑ii (read‑only host)**

**AppServer / core (C#)**
- `_meta.ui` model (`UiToolMeta`, `UiToolCsp`, `UiToolVisibility`), result `_meta`, wire types (`UiResourceReadParams`/`UiResourceReadResult`/`UiResourceContent`, `UiToolCallParams`), and the server→app `item/resource/read` constant: `src/DotCraft.Core/Protocol/AppServer/AppServerProtocol.cs`.
- Item payload `_meta` + `ui` descriptor fields: `src/DotCraft.Core/Protocol/ItemPayloads.cs`.
- `ui/resource/read` + `ui/tool/call` brokering, audit, and the model‑visibility filter: `src/DotCraft.Core/AppBinding/AppBindingService.cs` (`ReadUiResourceAsync`, `InvokeUiToolAsync`, `CreateRuntimeToolsForThread`); host routes in `AppBindingProtocolExtension.cs`. The same `_meta`/`ui` carry‑through also exists in `Protocol/AppServer/WireDynamicToolProxy.cs` (client‑declared dynamic tools).

**SDK (C#)** — `sdk/dotnet/src/DotCraft.Sdk/AppServer/`
- `RegisterResourceHandler` + `ResourceReadRequest`/`ResourceContent`/`ResourceReadResult`: `DotCraftClient.cs` + `AppServerModels.cs`.
- `_meta` on `DynamicToolSpec`/`DynamicToolResult` (+ `DynamicToolMeta`/`DynamicToolUiMeta`/`DynamicToolUiCsp`): `AppServerModels.cs`.

**Desktop main** — `desktop/src/main/`
- `dotcraft-app://` privileged scheme + handler + per‑resource CSP: `dotcraftAppProtocol.ts`.
- Scheme registration + parent CSP `frame-src 'self' dotcraft-app:` (and preserving the handler's CSP): `index.ts`.

**Desktop renderer** — `desktop/src/renderer/`
- iframe host + read‑only bridge peer: `components/conversation/InteractiveToolView.tsx`.
- `toolUi`/`meta` types, `normalizeToolUiDescriptor`, and history mapping: `types/conversation.ts`.
- ⚠️ **Two item mappers** — any new `dynamicToolCall` field must be carried in **both** `wireItemToConversationItem` (history/thread‑read load) **and** `buildToolLikeItem` + the `onItemCompleted` per‑field merge in `stores/conversationStore.ts` (the live `item/started → item/completed` path). M‑ii initially missed the live path, so cards rendered only on reload.
- Routing to the iframe (`!isRunning && hasInteractiveToolUi`): `components/conversation/ToolCallCard.tsx`.
- 7‑locale strings `interactiveTool.*`: `desktop/src/shared/locales/messages/*.ts`.

**Sample app** — `sdk/dotnet/samples/InteractiveToolSample/`: one‑command auto‑bind‑and‑serve (+ handoff mode), plugin manifest, and a self‑contained bridge HTML card. The reference for app authors and for end‑to‑end verification.
