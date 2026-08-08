# MCP Apps

MCP Apps let an MCP tool attach an interactive result view. The MCP server declares a `ui://` resource, and DotCraft Desktop renders it in a sandbox through the standard AppBridge contract. Other clients keep using the tool's text fallback.

App Binding does not define a separate UI protocol. An App Binding app exposes its tools and views from its binding-scoped Streamable HTTP MCP server.

## Declare a view

Add stable UI metadata to the MCP tool:

```json
{
  "name": "ListBoardItems",
  "inputSchema": { "type": "object" },
  "_meta": {
    "ui": {
      "resourceUri": "ui://example/board.html",
      "visibility": ["model", "app"]
    }
  }
}
```

Use `visibility` to publish the tool to the model, the app, or both. DotCraft treats malformed metadata as unavailable rather than widening access.

## Serve the resource

Return the resource from standard MCP `resources/list` and `resources/read`. MCP App HTML uses this MIME type:

```text
text/html;profile=mcp-app
```

Bundle scripts and styles with the server. Declare CSP domains and browser permissions in the resource's `_meta.ui`; Desktop denies capabilities that the resource does not declare.

## Return a useful fallback

Every call should return concise `content` for the model and non-visual clients. Put machine-readable result data in `structuredContent`; reserve result `_meta` for the MCP App.

```json
{
  "content": [{ "type": "text", "text": "Found 4 board items." }],
  "structuredContent": { "itemIds": ["OR-12", "OR-15"] },
  "_meta": { "selectedItemId": "OR-12" }
}
```

The UI communicates through the official `@modelcontextprotocol/ext-apps` client. Use AppBridge operations such as `tools/call`, link opening, display-mode requests, and host-context notifications. Do not create a private postMessage protocol.

## Security boundaries

- The iframe has an opaque origin and no host DOM or Node access.
- Resource CSP and permissions are explicit, capability-checked metadata.
- App-initiated tool calls stay within the originating MCP server and live authority.
- Revoking an App Binding closes its MCP session and views immediately.

## Related docs

- [DotCraft App](./app-binding)
- [AppServer protocol](../protocols/appserver-protocol)
