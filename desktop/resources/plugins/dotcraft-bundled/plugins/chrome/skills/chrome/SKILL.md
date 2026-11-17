---
name: chrome
description: "Use the user's existing Chrome browser through the DotCraft Chrome extension. Use for authenticated or profile-dependent pages, existing Chrome tabs, cookie-backed sessions, and installed extensions. For local previews and pages that need no user profile, use the in-app browser instead."
tools: NodeReplJs
---

# Chrome

Chrome is the user's real browser profile. Treat its pages and tabs as sensitive.

## User-facing updates

Setup is internal. Do not mention `NodeReplJs`, the REPL, JavaScript bindings, or reading documentation in progress updates unless the user asks. Describe setup or recovery as connecting or reconnecting to Chrome. If the user or Chrome stops browser control, say so plainly instead of quoting the runtime error.

## Bootstrap

Run browser code with `NodeReplJs`. Initialize once per thread, select Chrome, and read its complete documentation before acting:

Give every `NodeReplJs` call a `title` of a few words, under 80 characters, that tells the user what the run does, such as "Check the order status". It replaces the code in the conversation, so describe the action, not the REPL or JavaScript.

```js
const { setupBrowserRuntime } = await import(dotcraft.browserClientPath);
const agent = await setupBrowserRuntime();
const browser = await agent.browsers.get("extension");
nodeRepl.write(await browser.documentation());
```

The documentation contains the supported API, the operating guidance, and the safety and confirmation policy for browser actions. Read the output in full in one go. Do not assign it to a variable, slice it, or summarize it; only if the tool output reports truncation, read the missing part before acting. Do not reread it on later turns while the browser binding is valid.

Use `const` for stable handles and `let` for values that change, and reassign instead of redeclaring. Top-level bindings persist across calls and turns. Write text with `nodeRepl.write(value)` and show images with `await nodeRepl.emitImage(await tab.screenshot())`.

A stale, closed, or released tab does not invalidate the browser binding. Discard that tab handle and get a fresh one from `browser`. Run the bootstrap again only after the JavaScript session was reset.

Only `NodeReplJs` controls Chrome. Do not use a separate Playwright connection, other browser automation tools, shell automation, or Chrome profile files.

## User tabs

- `browser.user.openTabs()` lists the user's open tabs by title and URL. Claim one only by passing a returned tab to `browser.user.claimTab(tab)`, and only when the task needs that tab.
- Tabs you open join a DotCraft tab group. Unmarked agent tabs close and claimed tabs are released when the turn ends; mark a tab with `tab.markDeliverable()` or `tab.markHandoff()` when it must stay under the rules in the documentation.

## Recovery

If `agent.browsers.get("extension")` or a later command fails with `BridgeDisconnected`, wait about two seconds and retry once. If it still fails, run `nodeRepl.write(await dotcraft.chrome.checkSetup())` and act on the first failing check:

- Chrome not installed: tell the user DotCraft needs Google Chrome.
- Extension missing or disabled: ask the user to install or enable the DotCraft Chrome extension.
- Native host missing or invalid: ask the user to repair the Chrome setup from DotCraft settings.
- Chrome not running: ask before opening Chrome. With consent, run `open-chrome-window.js` from `dotcraft.chromeScriptsPath`.
- All checks pass but the backend is disconnected: ask the user to confirm Chrome is open and the DotCraft extension is enabled, then retry.

Other errors:

- `CommandCancelled` after the user clicked Cancel on Chrome's debugging banner: tell the user Chrome control was stopped and ask before resuming. Do not retry on your own.
- `DebuggerUnavailable`: ask the user to close DevTools or another tool debugging the tab, or use a normal web page. Chrome internal pages and the Chrome Web Store cannot be controlled.

Do not inspect cookies, passwords, local storage, history, or other Chrome profile data, and do not repair Chrome or native host setup without the user's consent.
