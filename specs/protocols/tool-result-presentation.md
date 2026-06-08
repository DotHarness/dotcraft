# DotCraft Tool Result Presentation Specification

| Field | Value |
|-------|-------|
| **Version** | 0.4.0 |
| **Status** | Draft |
| **Date** | 2026-06-08 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |
| **Related Specs** | [App Binding](app-binding.md), [Desktop Client](../clients/desktop-client.md), [Plugin Architecture](../extensions/plugin-architecture.md), [Session Core](../core/session-core.md) |

Purpose: define a safe, declarative **Dynamic Tool Card** contract so that Runtime Dynamic Tools, especially App Binding tools, can present richer client-rendered results without letting an agent or external app execute arbitrary UI code inside DotCraft.

A Dynamic Tool Card is a **safe projection of tool-call state and structured results** — not executable UI. A tool returns structured data; a card describes how to display that data and which controlled user actions it offers. Cards are declarative JSON over a fixed block vocabulary; they never contain JavaScript, HTML, CSS, iframes, or plugin components. The card is carried on the existing `presentation` field of a dynamic tool result (see [AppServer Protocol](appserver-protocol.md) §11.3); the card schema is identified by `schemaVersion: "dotcraft.card.v1"`.

This is the DotCraft contract for tool-result GenUI. The executable capability remains the tool (for the model) or the app's API (for human-initiated card actions). Presentation is a separate, optional, client-owned rendering layer. Microsoft Adaptive Cards is supported only as a **channel export adapter** (§14), not as the core contract.

---

## 1. Scope

This specification defines:

- A declarative **block vocabulary** (§7) for tool-result cards, rendered by trusted local components and the host theme.
- A **capability action** model (§8): a fixed set of controlled user actions, each re-validated at execution.
- The static card contract (§5) declared by Dynamic Tool specs and App Binding tool catalog entries, including optional **declared card templates with restricted path binding**.
- The runtime card payload (§6).
- The boundary between model-visible structured results and client-only presentation data (§3.1).
- Risk/approval alignment with App Binding (§9), security and resource limits (§10), and audit (§11).
- A built-in template catalog (§12) and multi-client rendering/fallback rules (§13).
- The Adaptive Cards export adapter (§14) and the Oratorio validation path (§15).

This specification does not define:

- A generic remote UI runtime, or arbitrary HTML, CSS, JavaScript, React, WebView, iframe, or plugin component execution.
- App-provided renderer code. A trusted plugin that needs full custom UI uses Desktop Extensions; a plain card is never upgraded into a mini plugin runtime.
- A general templating expression language. Binding is restricted to property/index paths (§5.3); no expressions, filters, or function calls.
- Card-initiated invocation of agent/dynamic tools (`tool.call`). Human-initiated "do something" actions call the app's API directly through `app.request` (§8.1), DotCraft-mediated.
- A sandbox. Safety comes from cards being fully declarative data, never executable code.
- A replacement for App Binding, Runtime Dynamic Tools, or MCP.

---

## 2. Product Goal

Agents often call external app tools whose raw JSON is useful to the model but poor for users. Desktop should render those results as compact, themed cards — board lists, created tasks, review rounds, diffs, errors, approval panels — while keeping tool execution, binding, approval, and audit server-owned.

The goal:

1. Let an app describe how a result is displayed, by composing it from a safe block vocabulary and/or a declared template bound to the tool's output.
2. Let DotCraft render that through trusted local components and the host theme, consistently across Desktop, TUI, and chat channels.
3. Keep all authority — tool exposure, scopes, risk, approvals, audit — server- and app-owned. A card is a request surface, never an authorization source.
4. Preserve useful fallback output for clients that do not support the card.

A fixed primitive vocabulary keeps DotCraft's implementation cost flat (each block implemented once) and lets any app — trusted or not — contribute cards using only declarative data.

---

## 3. Architecture Model

A Dynamic Tool Card has three sources, resolved in order:

| Source | Where | Purpose |
|--------|-------|---------|
| Inline runtime card | `DynamicToolCallResult.presentation` | A concrete card the tool returns for this call. |
| Declared template + binding | `DynamicToolSpec.presentation.template` / App Binding catalog | A card template the app declares once, bound to the tool's `structuredResult` via restricted paths (§5.3). |
| Built-in template | DotCraft client | Default cards derived from `dynamicToolCall` item state — pending, approval, error, app offline (§12). |

Resolution: if the result carries an inline card, render it. Else if the tool declares a template, bind it to `structuredResult` and render. Else if the item state matches a built-in template, render that. Else fall back to generic tool output (`contentItems` / `structuredResult` / error).

The agent does not select or inject UI code. The app contributes only declarative data. If the client cannot render a block, it uses the block's `fallback` or drops it; if it cannot render the card at all, it shows ordinary tool output.

### 3.1 Data Flow and Model Visibility

The card is client-only display data; it is never part of what the model sees.

- AppServer materializes the model-visible tool result from `contentItems` / `structuredResult` / error fields only, and **drops `presentation`** from that value.
- AppServer forwards `presentation` only on the client-facing `dynamicToolCall` / `pluginFunctionCall` item payload, so clients render and rehydrate it. See [AppServer Protocol](appserver-protocol.md) §4.1 and §11.3.
- The static card contract (`presentation`, `display`) on a tool spec is client-facing and MUST NOT enter the model-visible tool definition (name, description, inputSchema), preserving model tool-schema and prompt-cache stability.
- Sensitive display-only data MAY be carried in the card without being exposed to the model, mirroring the "metadata for the component, not the model" pattern.

---

## 4. Trust Boundary

Card data is treated as untrusted display data from the tool provider.

- The payload MUST be declarative JSON only. No executable code, inline event handlers, script URLs, style blocks, HTML fragments, or arbitrary CSS.
- Every block `type` MUST be a member of the vocabulary in §7. Unknown types use the block's `fallback`, else are dropped.
- All text is plain text (with an optional Markdown safe subset where a block allows it, §10). Styling is **semantic** only (`tone`, `size`); the client owns colors, spacing, fonts. The payload MUST NOT specify raw colors, pixel sizes, or fonts.
- The payload MUST NOT be exposed to the model (enforced by AppServer, §3.1). It MAY be persisted for rehydration.
- **A card never carries authority.** Action `kind`, `risk`, `scope`, and approval claims in the payload are display hints only. The executor re-derives the real risk/scope/approval from the binding catalog and enforces them server-side (§8, §9). A card cannot grant itself permissions.
- Clients MUST validate block types, props, action kinds, action targets, binding paths, nesting depth, and total size before rendering, and MUST fall back to generic rendering on failure.
- Every action `kind` used anywhere in the card MUST be allowed by the tool's declared `presentation.actions` whitelist (§5.2). Clients MUST drop disallowed or unknown actions and render the rest.
- App providers MUST include enough fallback (`fallbackText`, `contentItems`, `structuredResult`) for non-supporting clients.
- Clients MUST enforce the resource limits in §10 and fall back when exceeded.

---

## 5. Static Card Contract

### 5.1 `display`

A lightweight client hint, also used to label running tool activity before the result arrives.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `icon` | string | no | Icon key, emoji, or app-relative icon id. |
| `title` | string | no | User-visible tool title. |
| `subtitle` | string | no | User-visible secondary label. |

### 5.2 `presentation`

Declares the card contract for one tool.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `version` | string | no | Card schema the tool targets. Defaults to `dotcraft.card.v1`. |
| `actions` | string[] | no | Action kinds (§8) this tool's cards may use anywhere. Empty/omitted = no actions. |
| `template` | CardTemplate | no | Optional declared card template bound to the tool's output (§5.3). |

There are no renderer ids and no renderer code. A tool composes cards from the shared block vocabulary; it cannot select, name, or inject a renderer.

### 5.3 Declared Card Templates and Path Binding

A `template` is a card body that DotCraft renders by binding the tool's `structuredResult` into it. Apps declare the template once instead of constructing a full card in every result; tool results then only need to return `structuredResult`.

A declared template is a normal card (`title`, `summary`, `body`, `actions`; §6) whose string-valued fields MAY be **binding paths**:

- A string beginning with `$` is a binding path resolved against the current binding context. The root context is `structuredResult`. To emit a literal leading `$`, escape it as `$$`.
- Path grammar is restricted to property and array-index access: `$.a.b`, `$.items`, `$.rows[0].id`. No filters, wildcards, expressions, comparisons, or function calls.
- Collection blocks (`List`, `Table`) take a `source` array path; each element becomes the binding context for that block's item/row template, where `$` resolves to the element.
- A binding path that does not resolve yields an empty string (or an empty collection for `source`), never an error.

Example declared template (Oratorio `ListBoardItems`):

```json
{
  "presentation": {
    "version": "dotcraft.card.v1",
    "actions": ["app.open", "copy", "app.request"],
    "template": {
      "title": "Board items",
      "summary": "$.summaryText",
      "body": [
        {
          "type": "List",
          "source": "$.items",
          "item": {
            "title": "$.title",
            "subtitle": "$.shortId",
            "badges": [{ "label": "$.status", "tone": "info" }],
            "fields": [{ "label": "Owner", "value": "$.owner" }],
            "actions": [
              { "id": "open", "label": "Open", "kind": "app.open", "target": "$.deepLink" }
            ]
          }
        }
      ]
    }
  }
}
```

### 5.4 App Binding Tool Catalog

App Binding catalog entries MAY also declare `display` and `presentation` ([app-binding.md §5.5](app-binding.md)). The accepted catalog entry defines the **maximum** card authority — specifically the maximum allowed action kinds and the surface routes a card may call (§8.1). The runtime `DynamicToolSpec` attached via `app/binding/attachTools` MAY narrow this but MUST NOT expand it. DotCraft MUST reject or ignore runtime action kinds or routes that exceed the accepted catalog authority.

---

## 6. Runtime Card Payload

A successful or failed Dynamic Tool result MAY include a `presentation` card:

```json
{
  "success": true,
  "contentItems": [{ "type": "text", "text": "Found 8 open board items." }],
  "structuredResult": { "items": [] },
  "presentation": {
    "schemaVersion": "dotcraft.card.v1",
    "kind": "tool.result",
    "cardId": "oratorio.listBoardItems.result",
    "tool": {
      "namespace": "oratorio",
      "name": "ListBoardItems",
      "callId": "call_123",
      "bindingId": "binding_abc"
    },
    "risk": "read",
    "status": "succeeded",
    "title": "Board items",
    "summary": "Found 8 open items.",
    "body": [ { "type": "List", "items": [] } ],
    "actions": [
      { "id": "open-board", "label": "Open in app", "kind": "app.open", "target": "oratorio://dotcraft/board" }
    ],
    "fallbackText": "Found 8 open board items."
  }
}
```

`DynamicToolCard`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | string | yes | Card schema id. M1 is `"dotcraft.card.v1"`. |
| `kind` | string | no | Card kind (§6.1). Defaults to `tool.result`. |
| `cardId` | string | no | Stable card id for telemetry/audit and client keys. |
| `tool` | object | no | Provenance: `namespace`, `name`, `callId`, `bindingId`. Filled/validated by DotCraft against the real call. |
| `risk` | `"read" \| "mutate" \| "externalWrite"` | no | Coarse risk of the underlying result/actions, aligned with App Binding (§9). Display hint only; never an authorization. |
| `status` | string | no | Tool-call status: `pending` \| `succeeded` \| `failed` \| `cancelled`. |
| `title` | string | no | Card title. Plain text. |
| `summary` | string | no | One-line summary. Plain text. |
| `body` | Block[] | yes | The block tree (§7). |
| `actions` | Action[] | no | Card-level actions (§8). |
| `fallbackText` | string | no | Human-readable fallback when the card cannot render. |
| `source` | object | no | Optional extra provenance (e.g. external item ids). |

### 6.1 Card Kinds

| `kind` | Meaning |
|--------|---------|
| `tool.result` | Default. A normal successful or informational result card. |
| `tool.error` | A failed result; clients render error styling and prefer an `Error` block. |
| `tool.approval` | A card requesting an approval decision; includes an `ApprovalPanel` and `approve`/`reject` actions. |
| `externalWrite.proposal` | A proposed external write following the App Binding propose→approve→app-writes flow (§9). |

`pending` and `app.offline` cards are produced by built-in templates from item state (§12), not by tool payloads.

### 6.2 Versioning

`schemaVersion` is the card schema identifier. The block vocabulary and action set evolve additively (new block types, props, action kinds) without changing `schemaVersion`; forward-compatibility is handled per-block via `fallback` (§7). A breaking change to the envelope publishes a new `schemaVersion` (for example `dotcraft.card.v2`). A client that does not recognize a `schemaVersion` MUST fall back to generic rendering.

### 6.3 Lifecycle

A `presentation` card describes a completed tool result (success or failure). While the tool call runs, clients show the built-in `tool.pending` template from `display` and item state; the card is applied after the `dynamicToolCall` item completes. Dynamic tool results are atomic — there is no streaming partial card. A failed result MAY include a card only when `fallbackText` is also present.

---

## 7. Block Vocabulary

The card `body` is an array of blocks rendered top to bottom. Each block is a semantic content unit that owns its own internal layout; there are no free-form layout containers. DotCraft renders each block with a trusted, theme-aware local component, reusing existing Desktop renderers where applicable (`Diff` → inline diff view, `CodeBlock` → ANSI-aware code view, `ApprovalPanel` → the approval surface).

### 7.1 Common Fields

| Field | Type | Description |
|-------|------|-------------|
| `type` | string | Required. One of §7.3. |
| `id` | string | Optional. Stable id; `toggle` action target and client key. |
| `fallback` | Block \| `"drop"` | Optional. Rendered when `type` is unsupported. Absent → dropped. |
| `hidden` | boolean | Optional. Initial visibility; flipped by a `toggle` action. |

### 7.2 Shared Value Types

| Type | Shape / Values | Description |
|------|----------------|-------------|
| `Tone` | `"neutral" \| "success" \| "warning" \| "danger" \| "info" \| "accent"` | Semantic color; unknown → `neutral`. |
| `Fact` | `{ "label": string, "value": string, "mono"?: boolean }` | One key/value row. |
| `Badge` (value) | `{ "label": string, "tone"?: Tone }` | A status pill used inside `List` items, `ApprovalPanel`, or as the `Badge` block. |

### 7.3 Block Types

**`Text`** — `text` (req), `size` (`small`/`default`/`medium`/`large`), `weight` (`default`/`bold`), `tone`, `wrap` (default true), `mono`, `maxLines`, `markdown` (boolean; safe subset, §10).

**`Badge`** — `label` (req), `tone`. Renders inline; consecutive badges wrap.

**`KeyValue`** — `title` (opt), `facts: Fact[]`.

**`List`** — a list of entity rows. Either `items: ListItem[]` (inline) or `source` (array path) + `item: ListItem` template (§5.3). `emptyText` (opt). `ListItem`: `{ id?, icon?: Image, title, subtitle?, badges?: Badge[], fields?: Fact[], actions?: Action[] }`.

**`Table`** — `columns: TableColumn[]`, and either `rows: Row[]` or `source` (array path). `emptyText` (opt). `TableColumn`: `{ key?, label, value?: path, align?: "start"|"end", mono?: boolean }`. `Row`: `{ id?, cells: { [key]: string }, actions?: Action[] }`. Sorting is client-local.

**`CodeBlock`** — `code` (req), `language` (opt), `ansi` (boolean; render via the ANSI-aware view), `maxLines` (opt).

**`Diff`** — `path` (opt), `patch` (unified diff text) or `{ before, after }`. Rendered via the inline diff view.

**`Image`** — `url` or `icon` (one required), `alt`, `size` (`small`/`medium`/`large`), `shape` (`square`/`circle`). URL/scheme constraints in §10.

**`Progress`** — `value` (0..1) or `indeterminate` (boolean), `label` (opt). Used by `tool.pending` and long-running results.

**`Error`** — `message` (req), `code` (opt), `detail` (opt). Error styling.

**`ApprovalPanel`** — `title`, `summary` (opt), `risk` (`read`/`mutate`/`externalWrite`), `details: Fact[]` (opt), `actions: Action[]` (must be `approve`/`reject`, optionally `app.request`). Surfaces an approval/proposal; see §9.

**`ActionBar`** — `actions: Action[]`. A row of actions embedded in the body.

---

## 8. Capability Actions

Actions are declarative requests for a fixed set of controlled capabilities — never callbacks or code. Every action is re-validated at execution; the card's claims are not trusted.

`Action`: `{ "id": string, "label": string, "kind": string, "enabled"?: boolean, "description"?: string, "confirm"?: string, ...kind-specific }`.

| Kind | Behavior | Enforcement at execution |
|------|----------|--------------------------|
| `app.open` | Open an app deep link (`target`) whose protocol is declared by the bound app descriptor. | Protocol must be a registered app protocol; no executables/file paths/shell. |
| `openExternal` | Open an `https:` URL (`target`). | Scheme allow-list (§10). |
| `copy` | Copy `target` or `input.text` to the clipboard. | Plain text only. |
| `thread.enqueueInput` | Enqueue normal turn input (`input.parts`, `InputPart[]`) via the ordinary turn/enqueue flow. | Inherits all thread-running, approval, notification behavior. |
| `approve` / `reject` | Respond to a pending approval or external-write proposal (`target` = request/proposal id). | Routed through the existing approval flow ([AppServer Protocol](appserver-protocol.md) §7); re-checked server-side. |
| `app.request` | Request an authorized call to the bound app's API. M1: read-only (§8.1). | DotCraft-mediated; binding/scope/risk/route enforced (§8.1). |
| `toggle` | Client-only. Show/hide the block whose `id` equals `target`. | No server round-trip. |

`tool.call` (card-initiated invocation of an agent/dynamic tool) is intentionally **not** an action kind. Human-initiated "do something" goes through `app.request` directly to the app API; model-initiated tool use stays in the dynamic-tool layer.

Action constraints:

- Actions MUST require explicit user activation.
- Every action `kind` MUST be in the tool's declared `presentation.actions` whitelist; clients drop the rest.
- `confirm`, when present, requires a client confirmation step before executing.

### 8.1 `app.request` (DotCraft-mediated app API call)

A plain card is not trusted code and cannot make network calls or hold credentials. `app.request` is a **declaration**; DotCraft's main process performs the authorized loopback call after enforcement, and audits it.

`app.request` fields: `{ "kind": "app.request", "scope": string, "method": "GET", "path": string, "query"?: object, "body"?: object, "refresh"?: boolean }`. M1 restricts `method` to `GET` (read). Write methods (`POST`/`PATCH`/`DELETE`, for `mutate`/`externalWrite`) are M2.

Authorization reuses App Binding primitives — DotCraft enforces, the card claims nothing:

1. The binding for `tool.bindingId` MUST be `active`.
2. `scope` MUST be granted to that binding, and its declared `risk` MUST permit the request method (M1: `read`/`GET` only).
3. `path` MUST resolve under the app's declared **card-callable surface routes** (an App Binding descriptor declaration mapping route → required scope; see [app-binding.md](app-binding.md)). This prevents a card from under-declaring its scope to reach a higher-risk route.
4. DotCraft issues the call to the app's `surfaceEndpoints` using the workspace+user+app connection credential (the same authorization model as Desktop Extension surface reads/writes — `connectOrigins` for GET, `surfaceWriteScopes` for future writes).
5. DotCraft audits the call (§11).

A read `app.request` with `refresh: true` re-binds the tool's declared template (§5.3) to the response and replaces the card body — the canonical "Refresh this card" interaction. The response is treated as untrusted data and validated before rendering.

For `mutate`/`externalWrite` (M2), DotCraft requires explicit user approval before issuing, and `externalWrite` SHOULD follow the propose→record→approve→app-writes pattern (§9). After a successful write, DotCraft MAY post an app context block so the model learns the user acted (see [app-binding.md](app-binding.md) app context blocks).

---

## 9. Risk and Approval

Card `risk` aligns with App Binding's scope/tool risk ([app-binding.md §5.4](app-binding.md)):

| Risk | Card behavior |
|------|---------------|
| `read` | Rendered and read-refreshed (`app.request` GET) directly. |
| `mutate` | Deferred by default; any mutating action requires explicit user confirmation. (Write actions are M2.) |
| `externalWrite` | Deferred; uses an `ApprovalPanel` / `externalWrite.proposal` card and the propose → record → human approve → app writes flow. |

`approve` / `reject` actions and the `tool.approval` / `externalWrite.proposal` kinds reuse the existing approval flow and the App Binding operation-request mechanism; cards surface and trigger approvals but never decide them. The real risk/scope/approval requirement is always re-derived from the binding catalog, regardless of what the card states.

---

## 10. Security and Resource Limits

- **URL schemes:** only `https:` and DotCraft-internal schemes are allowed for links and `app.request`. `javascript:`, `data:`, and `file:` are forbidden by default. `app.open` protocols must be app-declared.
- **Images:** `Image` URLs must be `https:` or app-relative icon ids. External images MAY be blocked by default or routed through an allow-list/proxy; clients SHOULD not auto-load arbitrary external images.
- **Markdown:** `Text` with `markdown: true` allows a sanitized safe subset only (emphasis, inline code, lists, and `https:` links/images); everything else is escaped. No raw HTML.
- **Resource limits (client-enforced, fall back when exceeded):** maximum total blocks, maximum `List`/`Table` rows (with truncation + "show more"), maximum text length, maximum actions per block, maximum code length, and maximum image size. These prevent oversized cards from being used for UI denial-of-service.
- **No trusted authority in the payload:** as in §4, `kind`/`risk`/`scope`/approval claims are display hints; enforcement is server-side from the binding catalog.

---

## 11. Audit

Dynamic Tool Cards are an audit surface, not only a display. DotCraft SHOULD record, reusing the existing App Binding audit trail:

- Card render (cardId, tool namespace/name, callId, bindingId, payload hash).
- Action activation (action id/kind, cardId, user).
- Approvals/rejections (request/proposal id, decision).
- `app.request` calls (scope, method, route, request body hash, result digest).

---

## 12. Built-in Template Catalog

DotCraft ships built-in templates so common cases need no app-authored card. They render from `dynamicToolCall` item state and/or the result.

| Template | Used for |
|----------|----------|
| `tool.pending` | Running tool call (from `display` + `Progress`). |
| `tool.approval` | A pending approval decision. |
| `tool.result.summary` | Title + summary + key facts + actions. |
| `tool.result.table` | Tabular structured result. |
| `tool.result.diff` | Diff result. |
| `tool.error` | Failed result. |
| `app.offline` | The bound app is offline/unavailable. |
| `externalWrite.proposal` | A proposed external write awaiting approval. |

These cover the majority of Dynamic Tools without plugin-authored cards.

---

## 13. Client Rendering and Fallback

A single card contract serves all DotCraft clients; each renders natively:

- **Desktop:** full card with all blocks, themed via the host config; actions wired to the capabilities in §8.
- **TUI:** text rendering — tables as text tables, diffs as text diffs, approval panels as approval prompts, actions as selectable commands.
- **Chat channels (QQ, WeCom, Feishu, WeChat, Telegram):** map to the platform's native card/blocks where available, otherwise plain text plus the `fallbackText`. Approvals map to each platform's native approval rendering.

Clients that do not implement this spec, or cannot render a given card, MUST fall back to `contentItems` / `structuredResult` / error fields.

---

## 14. Adaptive Cards Adapter (Optional, Channel Interop)

Adaptive Cards is an **interop target, not the source of truth**. An optional adapter (`DotCraft.Cards.AdaptiveCards`) does two things only:

1. **Export:** convert the safe subset of a DotCraft card to Adaptive Card JSON for Microsoft channels (Teams / Outlook / Copilot). Target the **v1.5** subset for Teams desktop/bot; fall back to the **v1.2** subset for Teams mobile. Do not rely on v1.6 features (Carousel, Charts) as core, due to inconsistent cross-renderer support.
2. **Ingest:** when receiving external Adaptive Card JSON, convert only the supported safe subset into DotCraft blocks. DotCraft Desktop MUST NOT run the Adaptive Cards JS renderer or any external renderer in-process.

The DotCraft card contract remains canonical; Adaptive Cards is a boundary format for one channel family.

---

## 15. Oratorio Validation Contract

Oratorio is the first validating app.

| Tool | Card | Expected value |
|------|------|----------------|
| `oratorio.ListBoardItems` | `List` (declared template, §5.3) | Board items with short id, title, status badge, owner, and an `app.open` per row; `app.request` GET `refresh` to reload. |
| `oratorio.GetBoardItem` | `tool.result.summary` (`KeyValue` + `List`/`Diff`) | One item, key metadata, latest activity, app-open. |
| `oratorio.CreateBoardTask` | `tool.result.summary` | Created task title, short id, status, open/copy. |
| `oratorio.QueueReviewRound` | `tool.result.summary` or `externalWrite.proposal` | Queued round id, target item, status, open. |

Oratorio cards prefer `app.open`, `copy`, and read-only `app.request` in M1. "Do something" follow-ups use `thread.enqueueInput` in M1; direct write `app.request` lands in M2.

---

## 16. Compatibility

- Clients that do not implement this spec MUST render from `contentItems` / `structuredResult` / error fields.
- Unknown `schemaVersion` → generic fallback (use `fallbackText`).
- Unknown block `type` → the block's `fallback`, else drop it and render the rest.
- Unknown or disallowed action `kind` → drop that action, render the rest.
- Unresolved binding paths → empty value/collection, never an error.
- None of these cases fail the tool call. Apps MUST NOT rely on card support for correctness.

---

## 17. Acceptance Checklist

- Dynamic Tool results can carry an optional client-only `dotcraft.card.v1` card; declared templates can bind to `structuredResult` via restricted paths.
- Cards never change model-visible tool output (enforced by AppServer, §3.1).
- The block vocabulary is a fixed whitelist; text is plain/safe-subset; styling is semantic; the client owns the theme.
- Actions are capabilities, re-validated at execution; the card carries no authority. `tool.call` is not an action kind.
- `app.request` is DotCraft-mediated and gated by binding/scope/risk/route; M1 is read-only.
- Risk/approval/audit align with and reuse App Binding mechanisms.
- App Binding catalogs bound the allowed action kinds and callable routes; runtime cannot exceed them.
- Built-in templates cover common cases; Desktop/TUI/channels each render or fall back safely.
- Adaptive Cards is supported only as a channel export/ingest adapter, never run in-process.
- Oratorio board/task/review results render as cards without raw JSON as the primary experience.
