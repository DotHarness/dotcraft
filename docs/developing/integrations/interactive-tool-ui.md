# Interactive Tool UI

An App Binding tool can ship a **rich, interactive UI for its results**. The tool declares a `ui://` HTML resource; DotCraft **Desktop** renders it in a sandboxed iframe and runs a small postMessage JSON-RPC bridge between the UI and the host. This adopts the **MCP Apps** model and binds it to App Binding authority: the iframe is isolated, and every UI-initiated action is re-validated against the thread's binding (scope, risk, approval) and audited.

Interactive UI renders **only on Desktop**. Clients that do not negotiate `interactiveToolUi`, including chat channels, fall back to the tool's text result — the UI is always an enhancement, never required for correctness.

> [!NOTE]
> Examples use **Oratorio** as the running example. Swap the namespace, tool names, and `ui://` paths for your own app.

## When to use it

Reach for an interactive UI when a tool result is something the user *acts on* — a board to scan and click, an item to inspect and operate, a status to refresh — not just text the model relays. Keep purely informational results as text.

## Add a UI to a tool — four steps

### 1. Declare `_meta.ui` on the tool

Point the tool's descriptor `_meta.ui` at a `ui://` resource. This is **client-facing** metadata — it never enters the model-visible tool description.

```json
{
  "name": "ListBoardItems",
  "_meta": {
    "ui": {
      "resourceUri": "ui://oratorio/board.html",
      "visibility": ["model", "app"],
      "prefersBorder": true,
      "csp": { "connectDomains": ["https://127.0.0.1:7777"] },
      "permissions": []
    }
  }
}
```

| `_meta.ui` field | Meaning |
|---|---|
| `resourceUri` | The `ui://` document to render. Changing the URI is the cache-bust / version lever. |
| `visibility` | Who may call the tool — `["model","app"]` (default), `["app"]` (UI-only, hidden from the model), or `["model"]`. |
| `csp` | Network/resource allow-lists for the iframe: `connectDomains`, `resourceDomains`, `frameDomains`. Default is network-denied. |
| `permissions` | Powerful features the iframe may use: `camera`, `microphone`, `geolocation`, `clipboardWrite`. Default denies all. |
| `prefersBorder` | Render the host card frame with a border. |

### 2. Serve the `ui://` resource

Ship the HTML in your app bundle and register the folder on your AppServer client:

```csharp
client.ServeStaticUiResources("ui://oratorio", Path.Combine(AppContext.BaseDirectory, "UiResources"));
```

Every file under the folder is served (`ui://oratorio/board.html`, `…/item.html`, …) with the right MIME and read on demand, so edits are picked up during development.

### 3. Return the result with the right audience

A tool result carries three payloads with **distinct audiences**:

| Field | Audience | Use |
|---|---|---|
| `contentItems` | Model only | Text/image the model reads and relays; also the non-Desktop text fallback. |
| `structuredResult` | Model + UI | Concise JSON the UI renders and the model can inspect (ids for follow-ups). Keep it small. |
| `_meta` | UI only | Larger or sensitive display data for the UI. **Never reaches the model.** |

**Always populate the text result** (`contentItems` / `structuredResult`) so non-Desktop clients and the model still work — the UI is an enhancement.

### 4. Speak the bridge from your HTML

Your document talks to the host over `window.parent` postMessage (JSON-RPC 2.0). Send `ui/initialize`, then handle the pushed `tool-result`:

```js
parent.postMessage({ jsonrpc: "2.0", id: 1, method: "ui/initialize", params: {} }, "*");

window.addEventListener("message", (event) => {
  const m = event.data;
  if (m.method === "ui/notifications/tool-result") render(m.params.structuredContent);
  if (m.method === "ui/notifications/host-context-changed") applyTheme(m.params.theme);
});
```

## The bridge

The host pushes `tool-input`, `tool-result`, and `host-context-changed` (theme/locale/display mode); your UI may send:

| Request | Use |
|---|---|
| `tools/call` | Invoke an app-bound tool (refresh, mutate). Gated by the binding, decoupled from the conversation, audited; the result returns to the UI only. |
| `ui/open-link` | Open `https:` / `mailto:` or your app's declared deep-link protocol. |
| `ui/message` | Add a visible user message and start a model turn (rate-limited). |
| `ui/update-model-context` | Feed UI state to the model's next turn (silent, last-write-wins). |
| `ui/request-display-mode` | Request `inline` / `pip` / `fullscreen` (host arbitrates). |

A button can also `fetch` your own loopback backend directly — allowed by `_meta.ui.csp.connectDomains` — instead of calling a tool. Not every action is a tool call.

## Security & negotiation

- **Negotiation:** only a client that declares the `interactiveToolUi` capability (Desktop) receives `ui://` resources and may drive the `ui/*` bridge; others get text.
- **Sandbox:** the iframe runs `sandbox="allow-scripts"` with no same-origin access — opaque origin, no host DOM, no Node.
- **CSP:** a restrictive per-resource CSP, widened **only** from your server-validated `_meta.ui.csp`. `script-src` is never widened — external scripts stay blocked.
- **Permissions:** the iframe is granted **only** the powerful features you declare in `_meta.ui.permissions`; everything else is denied.
- **Links:** `ui/open-link` honors a host-owned scheme policy (`https:`/`mailto:` + your app's declared protocol); all other schemes are rejected and audited.
- **Tool calls:** a UI `tools/call` is re-validated against the binding scope and tool visibility; a mutating tool raises the normal approval; cross-binding calls are rejected. Every call is audited.

## Loopback CORS

If your UI `fetch`es your own backend (data path B), that backend must allow the iframe's opaque origin: respond with `Access-Control-Allow-Origin: *`, no credentials, loopback only.

See also: [App Binding](./app-binding), [Build an App](./build-an-app), [AppServer Protocol](../protocols/appserver-protocol).
