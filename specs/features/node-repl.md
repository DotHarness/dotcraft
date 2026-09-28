# Desktop Node REPL

| Field | Value |
|---|---|
| Version | 0.7.8 |
| Status | Living |
| Date | 2026-09-28 |

The Desktop implements `NodeReplJs` with one lazily created Electron utility process per task. This execution contract is shared by the in-app browser and Chrome clients and by Windows computer use. The existing evaluate/cancel request and text, image, logs, result and error response fields remain unchanged.

## Evaluation

- V8's native inspector REPL mode evaluates each complete code cell inside a Node context. Top-level `const`, `let`, destructuring, functions, closures and classes persist across calls and turns.
- Top-level `await` and dynamic `import()` use native Node semantics. Module caches and working directories belong to the task process.
- Lexical bindings may be redeclared across calls. Duplicate declarations inside a single cell and top-level `return` report errors.
- `nodeRepl.write(value)` and console output produce logs. Expression completion values populate `resultText`. `await nodeRepl.emitImage(image)` uses the existing Desktop image conversion and output handling.
- Syntax errors, JavaScript errors and browser command failures preserve the process and side effects already performed. They do not implicitly reset or roll back the environment.
- This is a Node execution environment, not an OS sandbox. Browser control still goes through the browser client's documented APIs and host policy boundary.

Assignment to `const` is an error. Existing closures observe a redeclared binding.

`nodeRepl.cwd` exposes the active workspace directory and `nodeRepl.homeDir` exposes the host user's home directory. Host-specific paths remain available through `dotcraft`.

The model-visible declaration uses the existing `[ToolDeclaration]` generator for its name, description and parameter schema. The plugin descriptor adds runtime plugin identity and namespace; invocation remains a plugin-native proxy to preserve multimodal results, metadata and binding leases. `code` remains required; `timeoutSeconds` remains an optional integer with no schema default. Generated declarations reject additional properties.

## Process ownership and cancellation

The manager owns task routing, a serial queue per task, and browser coordination. The worker client owns IPC and process lifecycle. The worker owns native evaluation and its task-local host globals. Electron main-process globals are never rewritten to support imported modules.

Evaluation and host messages include task identity, evaluation identity and process generation. Host capabilities and output are scoped to the active evaluation; old callbacks, replies and process results cannot contribute to a later call. Worker startup and cancellation acknowledgments belong to their process channel.

Outer timeout, cancellation, explicit reset, task deletion and Desktop connection teardown terminate the task process. Before termination the manager cancels IAB operations and requests cancellation of pending Chrome commands. Worker acknowledgment has a bounded grace period so a synchronous loop cannot prevent termination. Subsequent calls start a new lexical environment and module cache. Other tasks remain unaffected. Pending calls invalidated by reset or teardown do not execute in the replacement process.

Reset does not close delivered user pages. Capability owners receive cancellation before process termination and retain responsibility for resources whose lifetime extends beyond an evaluation.

## Browser bootstrap

The host supplies task-scoped client-module paths, current runtime metadata, image output and elicitation. Client modules return handles for explicit lexical bindings; the host does not install model-facing `agent` or `browser` globals. Imported modules read the current task's capabilities inside its process. A replaced process requires fresh bootstrap; a command or handle failure does not reset the environment.

## Computer use host API

Host calls carry the active task, turn and evaluation identities. Capability owners supply their APIs, authorization and resource cleanup. Screenshots join the evaluation's image output. While an AppServer approval is pending, the manager pauses the evaluation's outer timeout.
