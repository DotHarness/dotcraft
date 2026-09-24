# Desktop Node REPL

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

Reset does not close delivered user pages. Browser ownership and turn cleanup follow [the IAB lifecycle](desktop-inapp-browser.md) and [the Chrome lifecycle](chrome-browser-runtime.md).

## Browser bootstrap

Both clients return an agent directly. They do not install model-facing `agent` or `browser` globals:

```js
const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
const agent = await setupBrowserRuntime();
const browser = await agent.browsers.get("iab");
nodeRepl.write(await browser.documentation());
```

Chrome uses `dotcraft.chromeBrowserClientPath` and the `extension` selector. Stable bindings use `const`; handles that need reacquisition use `let`. An invalid tab or an empty tab list does not justify reinitializing the runtime. A browser disconnect requires reconnecting the browser, while process replacement requires fresh bootstrap.

The host supplies client paths, current browser-session metadata, image output, elicitation and Chrome setup/cancellation integration. Imported modules read the current task's capabilities inside its process. `globalThis` remains a normal JavaScript feature, not a required persistence technique.

## Computer use host API

On Windows, the worker exposes `dotcraft.computer`, whose methods are host calls routed to the Desktop computer use runtime. Calls carry the current task, turn and evaluation identity, and their screenshots join the evaluation's image output. The API, authorization and lifecycle are defined in [Desktop Computer Use](desktop-computer-use.md).

When the runtime asks the AppServer for approval during an evaluation, the manager pauses that evaluation's outer timeout until the approval resolves.

## Build and validation

The evaluator uses Node's context-level default module loader, currently marked experimental by Node; the packaged worker test covers this integration on the pinned Electron version.

`nodeReplWorker` is an Electron main-build entry, included with its chunks by the existing `out/**/*` packaging rule.

Targeted tests cover lexical state, module isolation, current metadata, errors, cancellation, late callbacks, IAB/Chrome client calls and delivered-page preservation. `npm run test:repl-worker` runs the built worker from an ASAR archive through Electron utilityProcess without opening an application window.
