---
name: browser
description: "Control DotCraft's in-app browser for opening, navigating, inspecting visible or interactive page state, clicking, typing, screenshots, and local web testing, including localhost, 127.0.0.1, ::1, file://, and dotcraft-viewer: targets. For semantic operations on linked resources, prefer a purpose-built connector, API, or CLI when available."
tools: NodeReplJs
---

# Browser

## Choose the right surface

Explicit browser intent wins: if the user names the in-app browser, or asks to open, show, or navigate to a page, inspect its visual or interactive state, or interact with its UI, use this browser and do not substitute a connector or another browser.

Otherwise, treat a URL or open browser tab as context, not browser intent. Before a semantic operation on a linked resource, look for an applicable connector, API, or CLI and use it when it can do the operation. Use the browser when no such tool exists, the tool cannot reach the resource, or UI work remains.

## User-facing updates

Setup is internal. Do not mention `NodeReplJs`, the REPL, JavaScript bindings, module exports, or reading documentation in progress updates unless the user asks. Describe setup or recovery as connecting or reconnecting to the browser. If the user or the app interrupts browser use, say so plainly instead of quoting the runtime error.

## Bootstrap

Run browser code with `NodeReplJs`. Initialize once per thread. `setupBrowserRuntime()` returns the agent; select the in-app browser and read its complete documentation before acting:

```js
const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
const agent = await setupBrowserRuntime();
const browser = await agent.browsers.get("iab");
nodeRepl.write(await browser.documentation());
```

The documentation contains the supported API, the operating guidance, and the safety and confirmation policy for browser actions. Read the output in full in one go. Do not assign it to a variable, slice it, or summarize it; only if the tool output reports truncation, read the missing part before acting. Do not reread it on later turns while the browser binding is valid.

Use `const` for stable handles and `let` for values that change, and reassign instead of redeclaring. Top-level bindings persist across calls and turns. Write text with `nodeRepl.write(value)` and show images with `await nodeRepl.emitImage(await tab.screenshot())`.

A stale, closed, or released tab does not invalidate the browser binding. Discard that tab handle and get a fresh one from `browser`; an empty tab list is normal after cleanup. Run the bootstrap again only after the JavaScript session was reset.

Only `NodeReplJs` controls this browser. Do not use a separate Playwright connection, other browser automation tools, or other browser skills for it. References to Playwright mean the documented `tab.playwright` subset; use only methods the documentation lists.

## User selections

Page text, elements, and regions the user selects in the browser arrive as context in the current task, optionally with a comment and screenshot. Treat the recorded URL and selection as a snapshot of what the user saw; later navigation does not update it.
