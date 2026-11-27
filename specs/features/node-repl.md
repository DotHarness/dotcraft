# Desktop Node REPL

| Field | Value |
|---|---|
| Version | 0.10.0 |
| Status | Living |
| Date | 2026-11-27 |

The Desktop exposes `NodeReplJs` through a local, in-process MCP server (`node_repl`) backed by one lazily created Electron utility process per task. This execution contract is shared by the in-app browser and Chrome clients and by Windows computer use. Evaluation runs as a standard MCP `tools/call` carrying `_meta["dotcraft/callContext"]`, returning text, base64 image blocks, structured surface metadata, and MCP error results.

## Evaluation

- V8's native inspector REPL mode evaluates each complete code cell inside a Node context. Top-level `const`, `let`, destructuring, functions, closures and classes persist across calls and turns.
- Top-level `await` and dynamic `import()` use native Node semantics. Module caches and working directories belong to the task process.
- Lexical bindings may be redeclared across calls. Duplicate declarations inside a single cell and top-level `return` report errors.
- `nodeRepl.write(value)` and console output produce logs. Expression completion values populate `resultText`. `await nodeRepl.emitImage(image)` adds an image to the evaluation's output.
- Syntax errors, JavaScript errors and browser command failures preserve the process and side effects already performed. They do not implicitly reset or roll back the environment.
- This is a Node execution environment, not an OS sandbox. Browser control still goes through the browser client's documented APIs and host policy boundary.

Assignment to `const` is an error. Existing closures observe a redeclared binding.

`nodeRepl.cwd` exposes the active workspace directory and `nodeRepl.homeDir` exposes the host user's home directory. Host-specific paths remain available through `dotcraft`.

Desktop attaches the `node_repl` MCP server to local threads via runtime-only `additionalMcpServers` whenever at least one of the bundled `browser`, `chrome`, or (on Windows) `computer` plugins is enabled for the workspace. Remote workspace connections never receive it. SubAgent child threads inherit their parent's runtime `additionalMcpServers` in memory.

The `node_repl` MCP server exposes two tools:

- `NodeReplJs` (model-visible): `code` is required, `title` is an optional short, user-facing description of the run, and `timeoutMs` is an optional integer timeout in milliseconds. Annotations mark the tool non-destructive and closed-world so MCP policy does not require a generic outer approval prompt, while browser and computer-use approvals remain fine-grained inside the runtime.
- `turn_ended` (app-only, `_meta.ui.visibility = ["app"]`, hidden from the model): invoked idempotently by the bundled plugins' `Stop`, `Interrupt`, and `SubagentStop` `mcp_tool` hooks (`skipIfDormant: true`). For main threads it runs browser turn finalization and ends any active computer-use turn. For SubAgent threads it disposes the task's worker and browser tabs.

Because one MCP tool serves every enabled plugin, `NodeReplJs` reports `structuredContent.surface` (`"computer"`, `"chrome"`, or `"browser"` when applicable). Desktop recognizes MCP calls from `node_repl` / `NodeReplJs`, labels each call with its `title` or surface fallback, derives the row icon and tool group from the surface, and shows result text and images rather than code when expanded.

Standard MCP `notifications/cancelled` maps directly to cancelling the in-flight evaluation for that thread and call. Thread deletion (`thread/deleted`) and Desktop connection teardown reset the thread's REPL worker and release resources.

## Process ownership and cancellation

The manager owns task routing, a serial queue per task, and browser coordination. The worker client owns IPC and process lifecycle. The worker owns native evaluation and its task-local host globals. Electron main-process globals are never rewritten to support imported modules.

Evaluation and host messages include task identity, evaluation identity and process generation. Host capabilities and output are scoped to the active evaluation; old callbacks, replies and process results cannot contribute to a later call. Worker startup and cancellation acknowledgments belong to their process channel.

Outer timeout, cancellation, explicit reset, task deletion and Desktop connection teardown terminate the task process. Before termination the manager cancels pending browser backend operations for the evaluation, in-app and Chrome alike. Subsequent calls start a new lexical environment and module cache. Other tasks remain unaffected. Pending calls invalidated by reset or teardown do not execute in the replacement process.

Reset does not close delivered user pages. Capability owners receive cancellation before process termination and retain responsibility for resources whose lifetime extends beyond an evaluation.

## Browser bootstrap

The host supplies task-scoped client-module paths, current runtime metadata, image output and elicitation. Client modules return handles for explicit lexical bindings; the host does not install model-facing `agent` or `browser` globals. Imported modules read the current task's capabilities inside its process. A replaced process requires fresh bootstrap; a command or handle failure does not reset the environment.

## Computer use host API

Host calls carry the active task, turn and evaluation identities. Capability owners supply their APIs, authorization and resource cleanup. Screenshots join the evaluation's image output. While an MCP elicitation approval is pending, the manager pauses the evaluation's outer timeout.
