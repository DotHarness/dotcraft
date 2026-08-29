# Desktop E2E and developer debugging

**Status:** Accepted

This specification defines the local automation boundary for DotCraft Desktop. It covers explicit Chrome DevTools Protocol (CDP) startup, a hot-pluggable renderer driver, and non-owning Playwright CLI attachment. It does not define a CI suite.

## Goals

- Attach a persistent Playwright CLI session to an already-running DotCraft Desktop instance.
- Reuse the same attachment path for source development and packaged DotCraft builds.
- Keep the attached Desktop, tray, Hub, and AppServer alive when the CLI client detaches or fails.
- Replace renderer store exposure with a narrow driver owned by a Desktop Plugin generation.
- Let agents inspect and operate the UI through short CLI commands without generating scenario files.

## Non-goals

- Enabling CDP in an Electron process that started without a remote debugging endpoint.
- Launching, restarting, terminating, or supervising Desktop from Playwright CLI.
- Defining permanent smoke scenarios, CI fixtures, state reset, test databases, or a test workspace format.
- Adding MCP, a custom RPC transport, an authentication proxy, instance discovery, or port scanning to the default workflow.
- Exposing renderer stores, preload APIs, arbitrary AppServer requests, or host lifecycle operations through the driver.

## Architecture

The implementation has three existing layers:

1. Electron exposes CDP when the process is started with a remote debugging port.
2. The DotCraft Desktop Plugin installs a renderer driver while its current generation is active.
3. The official Playwright CLI keeps a named session attached to that endpoint and exposes snapshot, interaction, diagnostic, and inline `run-code` commands.

CDP is a startup capability. Electron must receive `--remote-debugging-port` on its command line, or main must append the switch before `app.ready`. A renderer plugin loaded after startup cannot create an external CDP endpoint. `webContents.debugger` is an in-process CDP client and is not an external transport.

The Desktop Plugin is a runtime semantic capability. Installing, disabling, updating, or uninstalling the plugin adds, replaces, or removes the driver through the normal Desktop Plugin generation lifecycle described in [desktop-plugins.md](desktop-plugins.md).

## Startup contract

Ordinary development remains unchanged:

```powershell
npm run dev
```

Explicit debugging uses the repository command and default port `9222`:

```powershell
npm run dev:debug
```

The repository command first checks whether `127.0.0.1:9222` is available, then passes that port to electron-vite's native `--remoteDebuggingPort` option. A collision fails and reports the requested endpoint. Callers that need another port invoke electron-vite with an explicit value; the implementation must not scan for or silently select another port.

A packaged DotCraft build uses Electron's command-line switch directly:

```powershell
DotCraft.exe --remote-debugging-port=9222
```

Starting or relaunching with this switch is required. Installing the Desktop Plugin into an already-running process does not enable CDP.

## Visible disclosure

When the Electron process has the `remote-debugging-port` switch, Desktop shows a persistent CDP debugging indicator at the bottom-right window edge. The indicator is present on welcome, setup, error, and workspace surfaces. Its tooltip names the active CDP debugging mode without adding operational detail.

The disclosure is owned by the Desktop shell, not by the DotCraft Desktop Plugin. Main determines the startup capability from Electron's effective command line, preload projects only that immutable boolean, and renderer owns the visual. Disabling, uninstalling, updating, or failing to load the plugin cannot hide an active CDP endpoint.

Tray and New Window child processes do not inherit the parent's remote debugging port. A second debuggable process must be started explicitly with its own port, avoiding port contention and false disclosure.

The indicator reports capability, not client activity. It remains visible when no Playwright client is attached and does not claim that a connection exists. Opening DevTools without a remote debugging port does not show it. Reduced-motion preferences keep the indicator static without removing the disclosure.

## Driver contract

The active DotCraft Desktop Plugin generation installs this object in the renderer realm:

```ts
interface DotCraftDesktopDriver {
  whenWorkbenchRestored(): Promise<void>
}

declare global {
  var driver: DotCraftDesktopDriver | undefined
}
```

`whenWorkbenchRestored()` resolves only after the DotCraft application surface is mounted and the plugin generation is active. It rejects if that generation is disposed before readiness.

The driver is generation-owned:

- Activation replaces no foreign `globalThis.driver`; a collision fails activation.
- Revision replacement installs a new object.
- Disposal removes the global only when it still references the disposing generation's object.
- A stale generation cannot remove a newer driver's object.

The first version has no state mutation or generic request method. Add methods only for demonstrated interactions that Playwright cannot perform reliably through accessible DOM and standard input APIs.

## Playwright CLI session

DotCraft uses the official `@playwright/cli` package pinned by `desktop/package.json`. Agents invoke the repository-local binary and use a stable named session, normally `dotcraft`, for the duration of an investigation.

The session lifecycle is:

1. Probe `<endpoint>/json/version` and fail clearly when the caller-provided loopback endpoint is unavailable.
2. Attach once with `playwright-cli -s=dotcraft attach --cdp=<endpoint>`.
3. Select the page carrying `globalThis.driver`, then wait for `driver.whenWorkbenchRestored()` before interacting.
4. Reuse the same session for `snapshot`, `find`, `click`, `fill`, `type`, `console`, `requests`, screenshots, and inline `run-code`.
5. End with `playwright-cli -s=dotcraft detach`, which leaves the external Electron process running.

Exploratory work uses snapshots and focused interaction commands. Once locators are known, a short `run-code` command may group a stable sequence into one CLI round trip. Scenario code is written to a file only when it is intentionally becoming a repeatable regression artifact.

`close`, `close-all`, `kill-all`, tab-closing commands, Playwright `ElectronApplication.close()`, the CDP `Browser.close` command, PID termination, process-name enumeration, and Desktop stdout/stderr ownership are outside this attach-only workflow.

## Security

CDP has no application-level authentication and grants high authority over the renderer. The supported boundary is deliberately small:

- Debugging is enabled only by an explicit startup command or argument.
- The endpoint uses Chromium's default loopback binding.
- The workflow accepts only caller-provided loopback endpoints.
- The caller supplies the endpoint; DotCraft does not advertise or discover instances.
- The driver comes from a fully trusted Desktop Plugin and does not widen CDP beyond its existing authority.

A token in front of a separate helper would not authenticate the raw Chromium endpoint and is therefore not part of this design.

## State and lifecycle

The attached session operates on the profile and workspace selected when Desktop started. The CLI workflow does not reset, seed, snapshot, or restore application state. Use explicit launch arguments and a disposable workspace/profile when isolation is required.

The CLI session owns only its CDP connection. Desktop owns its window, tray, main process, and plugin generations. Hub owns managed AppServer lifecycle. Detaching the session must not stop or restart any of them.

This ownership rule also prevents a short-lived automation client from becoming the stdio owner of the long-lived Desktop process tree, eliminating the old broken-pipe failure path.

## Repository policy

The repository keeps the debug startup contract, official CLI dependency, and driver contract, not scenario-specific smoke scripts. Routine investigations use the CLI directly and do not create source files.

Renderer store layouts and private preload APIs are not automation contracts.

## Acceptance criteria

- Source development and a packaged DotCraft build can expose CDP through an explicit startup option.
- Desktop visibly discloses the active CDP startup capability independently of plugin lifecycle or client attachment.
- A named Playwright CLI session attaches to the DotCraft page and waits for the active Desktop driver.
- Detaching or interrupting the CLI client leaves Desktop and its background services running.
- Plugin revision replacement and disposal do not leave a stale driver.
- The CLI workflow never launches Electron, owns Desktop stdio, sends `Browser.close`, or terminates a process.
- The repository contains no permanent smoke, terminal, screenshot, or `_electron.launch()` script.
- The driver exposes only readiness and no renderer stores or preload APIs.
