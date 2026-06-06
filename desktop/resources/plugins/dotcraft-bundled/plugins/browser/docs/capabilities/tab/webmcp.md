# Tab Capability: webmcp

WebMCP tools exposed by the current page through `navigator.modelContext`. This is a current-page capability: call `await tab.capabilities.list()` first, and use WebMCP only when that list includes `webmcp`. Use `listTools()` to inspect page-defined tools, then either call `invokeTool({ toolName, input })` or invoke the returned tool object's `invoke(input)` helper. Only use tools that the page explicitly exposes in its current state.

```ts
const capabilities = await tab.capabilities.list();
if (!capabilities.some((capability) => capability.id === "webmcp")) {
  throw new Error("Capability is not available: webmcp");
}
const capability = await tab.capabilities.get("webmcp");

interface WebMcpTabCapability {
  invokeTool(options: {
    input?: unknown;
    timeoutMs?: number;
    toolName: string;
  }): Promise<unknown>;
  listTools(): Promise<Array<{
    name: string;
    title?: string;
    description?: string;
    inputSchema?: unknown;
    annotations?: Record<string, unknown>;
    origin?: string;
    pageUrl?: string;
    invoke(input?: unknown, options?: { timeoutMs?: number }): Promise<unknown>;
  }>>;
}
```

Desktop IAB does not synthesize tools from hidden browser state, extension storage, cookies, history, or local profile data. If the page does not provide `navigator.modelContext`, `webmcp` is omitted from `tab.capabilities.list()`. Forced `get("webmcp")`, `listTools()`, or `invokeTool()` calls fail with `Capability is not available: webmcp` instead of page JavaScript `TypeError` details.
