---
name: dotcraft-desktop-debugging
description: Inspect and exercise a running debug-enabled DotCraft Desktop through a persistent Playwright CLI session. Use for Desktop UI reproduction, interaction checks, screenshots, console or network inspection, and local smoke investigations; not for ordinary websites or Chrome automation.
---

# DotCraft Desktop Debugging

Attach the official Playwright CLI to an existing DotCraft Desktop process without owning its lifecycle. Reuse one named session throughout the investigation; do not generate a TypeScript script for routine interaction.

If the request changes the debugging infrastructure rather than using it, read `specs/architecture/desktop-e2e-debugging.md` first and preserve its ownership and security boundaries.

## Choose The Right Surface

- Use this skill to inspect or exercise the DotCraft Desktop renderer itself.
- Use the Browser skill for DotCraft's in-app browser and the Chrome skill for the user's Chrome. Their Playwright-compatible APIs are unrelated to Desktop CDP attachment.
- CDP must be enabled when Electron starts. Do not attempt process injection or attach to an instance that was started without a remote debugging port.

## Connect Once

Prefer an already running debug-enabled Desktop. The default endpoint is `http://127.0.0.1:9222`; accept a different loopback endpoint when the caller provides one.

For source development, start Desktop from `desktop/` in a long-lived terminal:

```powershell
npm run dev:debug
```

To select a workspace explicitly, forward Electron arguments after an additional `--`:

```powershell
npm run dev:debug -- -- --workspace <workspace-path>
```

For a packaged build, start it explicitly with:

```powershell
DotCraft.exe --remote-debugging-port=9222
```

Do not start Electron from Playwright CLI. `dev:debug` intentionally fails when port 9222 is already in use instead of choosing another instance or port.

Use the repository-local `playwright-cli` binary pinned in `desktop/package.json`. Run it with the stable session name `dotcraft` and the explicit loopback endpoint:

```powershell
cd desktop
npx --no-install playwright-cli -s=dotcraft attach --cdp=http://127.0.0.1:9222
```

If the endpoint is not ready, stop and report it rather than starting another Desktop or scanning ports. Development builds may initially select their DevTools page. List tabs, then select the entry titled `DotCraft` before waiting for readiness:

```powershell
npx --no-install playwright-cli -s=dotcraft tab-list
npx --no-install playwright-cli -s=dotcraft tab-select <dotcraft-tab-index>
npx --no-install playwright-cli -s=dotcraft run-code "async page => page.evaluate(async () => { const driver = globalThis.driver; if (typeof driver?.whenWorkbenchRestored !== 'function') throw new Error('DotCraft Desktop driver is unavailable'); await driver.whenWorkbenchRestored(); })"
```

## Exercise The UI

- Start with `snapshot` or `find`, then use returned element refs or accessible Playwright locators. Do not invent selectors without inspecting current state.
- Reuse `-s=dotcraft` for every command. `snapshot`, `click`, `fill`, `type`, `press`, `console`, `requests`, and `screenshot` all operate on the same attached page.
- For a stable multi-step sequence, use one inline `run-code` command instead of creating a file. Keep inline code short and limited to the requested UI flow.
- Operate on the workspace and profile already selected by Desktop. Do not seed, reset, or mutate private application stores unless the user explicitly authorizes a separate product-state change.
- Store useful screenshots or traces under `.craft/attachments/`. Remove incidental CLI output after the investigation.
- The Desktop plugin driver is deliberately narrow. Do not depend on private preload APIs, renderer stores, or application globals.

## Detach Safely

The named session owns only its attached Playwright connection. End the investigation with:

```powershell
npx --no-install playwright-cli -s=dotcraft detach
```

Never use `close`, `close-all`, `kill-all`, `tab-close`, `ElectronApplication.close()`, or `Browser.close`. Do not terminate Desktop or AppServer processes, take ownership of Desktop stdout, or restart the user's instance without explicit authorization.

When interrupted, detach the named session if it remains available and leave Desktop, its tray process, plugin generations, and AppServer running. Remove only investigation artifacts that are no longer needed.

## Persist Only Real Regression Tests

Do not create a scenario file merely to click, type, inspect, or take a screenshot. A Playwright file is appropriate only when the user asks for a repeatable test or the observed failure should become a maintained regression case. That is a separate implementation task with its own ownership and test review.

## Verify The Debugging Layer

When changing the CLI workflow or debug-port guard, run from `desktop/`:

```powershell
npx vitest run scripts/check-debug-port.test.ts
npm run typecheck
```

For an integration check, start one debug-enabled Desktop, attach once, run multiple CLI interactions, and detach. Reattach to the same instance to prove reuse. Each detach must leave the same Desktop and AppServer alive without an `EPIPE` error. Keep scenario-specific smoke scripts out of the repository.
