# DotCraft Tool Result Presentation Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-05-19 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |
| **Related Specs** | [App Binding](app-binding.md), [Desktop Client](../clients/desktop-client.md), [Plugin Architecture](../extensions/plugin-architecture.md), [Session Core](../core/session-core.md) |

Purpose: define a safe, declarative way for Runtime Dynamic Tools, especially App Binding tools, to provide richer client-rendered UI for tool results without letting an agent or external app execute arbitrary UI code inside DotCraft.

This specification is the DotCraft contract for tool-result-oriented GenUI. It is intentionally not a server tool and not an embedded web-app runtime. The executable capability remains the tool. Presentation is a separate, optional, client-owned rendering layer.

---

## 1. Scope

This specification defines:

- Tool result presentation metadata declared by Dynamic Tool specs and App Binding tool catalog entries.
- The runtime `presentation` payload that a Dynamic Tool result may return.
- The boundary between model-visible structured results and client-only presentation data.
- The minimum renderer, action, fallback, security, and compatibility rules for Desktop and future AppServer clients.
- The first-version path for Oratorio-style board, task, and review-round result cards.

This specification does not define:

- A generic remote UI runtime.
- Arbitrary HTML, CSS, JavaScript, React, WebView, iframe, or plugin-provided component execution.
- A replacement for App Binding, Runtime Dynamic Tools, or MCP.
- Direct mutation from presentation actions to app-owned tools.
- Pixel-level Desktop design or frontend implementation details.

---

## 2. Product Goal

Agents often call external app tools whose raw JSON is useful to the model but poor for users to inspect. For example, an Oratorio board tool may return board items, local tasks, review rounds, and next actions. Desktop should be able to render that result as a compact board or task card while preserving the existing Dynamic Tool execution model.

The goal is:

1. Let an app describe how a specific tool result should be displayed.
2. Let Desktop render that display through trusted local components.
3. Keep all tool execution, approval, binding, and audit semantics server-owned.
4. Preserve useful fallback output for clients that do not support the presentation.

---

## 3. Architecture Model

Tool Result Presentation has two layers:

| Layer | Source | Purpose |
|-------|--------|---------|
| Static presentation contract | `DynamicToolSpec` and App Binding `toolCatalog` | Declares which presentation renderers and actions a tool is allowed to return. |
| Runtime presentation payload | `DynamicToolCallResult.presentation` | Carries one concrete declarative UI payload for the completed tool call. |

The static contract is configuration. The runtime payload is result data.

The agent does not select arbitrary UI code. The app or client-owned dynamic tool may return declarative data, and the DotCraft client decides whether it can render that data with a trusted renderer. If the client cannot render it, the conversation still shows ordinary tool output using `contentItems`, `structuredResult`, `errorCode`, and `errorMessage`.

---

## 4. Trust Boundary

Presentation data is treated as untrusted display data from the tool provider.

Requirements:

- The payload MUST be declarative JSON data only.
- The payload MUST NOT contain executable code, inline event handlers, script URLs, style blocks, HTML fragments, or arbitrary CSS.
- The payload MUST NOT be exposed to the model as part of the tool result.
- The payload MAY be persisted as part of the conversation item history so clients can rehydrate the UI.
- Clients MUST validate renderer ids, payload shape, action kinds, action targets, and size limits before rendering.
- Clients MUST fall back to generic tool rendering when validation fails.
- App providers MUST include enough text or structured fallback data for non-supporting clients.

---

## 5. Static Presentation Contract

Dynamic Tool specs MAY declare display and presentation metadata:

```json
{
  "namespace": "oratorio",
  "name": "ListBoardItems",
  "description": "List Oratorio board items.",
  "inputSchema": { "type": "object", "properties": {} },
  "outputSchema": {
    "type": "object",
    "properties": {
      "items": { "type": "array" }
    }
  },
  "display": {
    "icon": "oratorio",
    "title": "List board items",
    "subtitle": "Oratorio"
  },
  "presentation": {
    "renderers": [
      {
        "id": "dotcraft.kanban-list.v1",
        "placement": ["conversationCard"],
        "actions": ["openApp", "copy", "startTurn"]
      }
    ]
  }
}
```

### 5.1 `display`

`display` is a lightweight client hint.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `icon` | string | no | Icon key, emoji, or app-relative icon id. Clients may ignore it. |
| `title` | string | no | User-visible tool title. |
| `subtitle` | string | no | User-visible secondary label. |

### 5.2 `presentation`

`presentation` declares the result presentation contract for one tool.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `renderers` | ToolPresentationRendererContract[] | no | Renderers this tool is allowed to return. Empty or omitted means no rich presentation is declared. |

`ToolPresentationRendererContract`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Renderer id, for example `dotcraft.entity-list.v1`. |
| `placement` | string[] | no | Allowed placements. M1 uses `conversationCard`. |
| `actions` | string[] | no | Allowed presentation action kinds for this renderer. |
| `dataSchema` | object | no | Optional JSON Schema for the runtime `presentation.data`. |

Renderer ids are stable protocol identifiers, not component import paths. DotCraft-owned generic renderers use the `dotcraft.` prefix. App-specific renderer ids may exist only when the target client explicitly supports them; third-party apps cannot inject renderer code through this contract.

### 5.3 App Binding Tool Catalog

App Binding catalog entries MAY also declare `display` and `presentation`. The catalog declaration is used for discovery, consent, and validation. The concrete runtime `DynamicToolSpec` attached through `app/binding/attachTools` remains authoritative for the exact executable schema.

For app-bound tools, the accepted App Binding catalog entry defines the maximum presentation authority. The attached runtime `DynamicToolSpec` MAY repeat or narrow the presentation contract for the concrete tool attachment, but it MUST NOT expand renderer ids or action kinds beyond the accepted catalog entry. DotCraft MUST reject or ignore runtime presentation renderer ids and action kinds that exceed that catalog authority.

---

## 6. Runtime Presentation Payload

A successful or failed Dynamic Tool result MAY include a `presentation` object:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Found 6 active board items." }
  ],
  "structuredResult": {
    "items": []
  },
  "presentation": {
    "schemaVersion": 1,
    "renderer": "dotcraft.kanban-list.v1",
    "title": "Oratorio board",
    "subtitle": "6 active items",
    "data": {
      "columns": []
    },
    "actions": [
      {
        "id": "open-board",
        "label": "Open in Oratorio",
        "kind": "openApp",
        "target": "oratorio://dotcraft/board"
      }
    ],
    "fallbackText": "Found 6 active board items."
  }
}
```

`ToolResultPresentation`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | number | yes | Presentation payload schema version. M1 is `1`. |
| `renderer` | string | yes | Renderer id selected for this result. |
| `title` | string | no | Card title. Plain text only. |
| `subtitle` | string | no | Card subtitle. Plain text only. |
| `data` | object | yes | Renderer-specific declarative data. |
| `actions` | ToolPresentationAction[] | no | User-clickable actions. |
| `fallbackText` | string | no | Human-readable fallback when the presentation cannot render. |
| `source` | object | no | Optional provenance such as `appId`, `namespace`, `toolName`, and external item ids. |

Clients MUST treat `title`, `subtitle`, labels, and data text as plain text unless a renderer explicitly defines a sanitized rich-text field. M1 renderers should prefer plain text.

---

## 7. Presentation Actions

Presentation actions are declarative user actions. They are not hidden tool calls.

`ToolPresentationAction`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | yes | Stable action id unique within the presentation payload. |
| `label` | string | yes | User-visible label. |
| `kind` | string | yes | Action kind. |
| `target` | string | no | URL, app deep link, text, or client route target depending on `kind`. |
| `input` | object | no | Additional action data. |
| `enabled` | boolean | no | Defaults to true. |
| `description` | string | no | Optional tooltip or accessible explanation. |

M1 action kinds:

| Kind | Behavior |
|------|----------|
| `openUrl` | Open an `http` or `https` URL through normal client link handling. |
| `openApp` | Open an app deep link whose protocol is declared by the bound app descriptor. |
| `copy` | Copy `target` or `input.text` to the clipboard. |
| `startTurn` | Submit or enqueue a normal DotCraft turn using client-provided input parts from `input`. |
| `openFile` | Open a local file through the client's normal file-viewer authorization path. |

Action constraints:

- Actions MUST require an explicit user click or keyboard activation.
- `openApp` MUST NOT accept arbitrary local executables, file paths, shell commands, or unregistered protocols.
- `startTurn` MUST use the ordinary `turn/start` or `turn/enqueue` flow and inherit all normal thread-running, approval, and notification behavior.
- Presentation actions MUST NOT directly invoke app-bound tools in M1. A future direct app-action contract must define authorization, audit, error handling, and revocation before it can be added.

---

## 8. M1 Renderer Catalog

Desktop clients that implement this specification SHOULD support a small generic renderer catalog before adding app-specific renderers.

| Renderer | Purpose |
|----------|---------|
| `dotcraft.summary-card.v1` | Compact result with title, status, metadata, and actions. |
| `dotcraft.entity-list.v1` | List of entities such as tasks, issues, board items, or reviews. |
| `dotcraft.data-table.v1` | Tabular structured results with sortable local columns. |
| `dotcraft.kanban-list.v1` | Small board-like grouping for status columns and item cards. |
| `dotcraft.timeline.v1` | Ordered events such as review rounds, comments, or state changes. |
| `dotcraft.key-value.v1` | Compact details panel for structured object fields. |

M1 renderers are conversation-card renderers. They may support local expand/collapse, filtering, sorting, and details disclosure, but those interactions are client-local unless represented by an explicit action.

---

## 9. Oratorio Validation Contract

Oratorio is the first validating app for this specification.

Expected presentation mapping:

| Tool | Preferred renderer | Expected user value |
|------|--------------------|---------------------|
| `oratorio.ListBoardItems` | `dotcraft.kanban-list.v1` or `dotcraft.entity-list.v1` | Show board items by state with short ids, titles, source, repository, assignee, and next action. |
| `oratorio.GetBoardItem` | `dotcraft.summary-card.v1` plus `dotcraft.timeline.v1` | Show one item, important metadata, latest activity, and app-open action. |
| `oratorio.CreateBoardTask` | `dotcraft.summary-card.v1` | Show created task title, short id, state, and open/copy actions. |
| `oratorio.QueueReviewRound` | `dotcraft.summary-card.v1` or `dotcraft.timeline.v1` | Show queued round id, target item, status, and open action. |

Oratorio presentation actions should prefer `openApp` and `copy`. Follow-up work should use `startTurn` rather than directly invoking another app tool from the card.

---

## 10. Compatibility

- Clients that do not implement this spec MUST continue to render tool results from `contentItems`, `structuredResult`, and error fields.
- Servers SHOULD preserve unknown presentation fields only when they are inside the opaque renderer `data` object.
- Servers and clients MAY ignore unsupported renderer ids or action kinds without failing the tool call.
- Apps MUST NOT rely on presentation support for correctness. Presentation is an enhancement to the conversation UX.

---

## 11. Acceptance Checklist

- Dynamic Tool results can carry optional client-only presentation JSON.
- Presentation does not change model-visible tool output.
- App Binding tool catalogs can declare which renderers and actions are allowed.
- Desktop can render supported presentation payloads and fall back safely.
- Presentation actions are explicit user actions and do not bypass AppServer turn, approval, binding, or audit rules.
- Oratorio board/task results can be represented without raw JSON exposure as the primary user experience.
