# Desktop E2E and developer debugging

| Field | Value |
| --- | --- |
| Version | 0.14.2 |
| Status | Living |
| Date | 2026-11-24 |

This specification defines explicit CDP startup, the renderer readiness driver, and
non-owning automation attachment. It does not define a scenario suite or state-reset service.

## Architecture

Electron owns the CDP endpoint from process startup. The active Desktop extension generation owns
its renderer driver. An automation
client owns only its CDP connection, never the Desktop, tray, Hub, or AppServer lifecycle.

## Startup contract

CDP is disabled unless startup explicitly enables a remote debugging port. Source development
uses `npm run dev:debug` (port `9222` by default); packaged builds accept
`--remote-debugging-port=<port>`. The CDP debugging switch in Settings › General is a persisted
startup choice: while it is on, each later start of the Desktop process enables port `9222`
unless its command line names a port. Changing the switch never affects the running process, so
the switch says that it applies after a restart. A requested-port collision fails rather than scanning for
another endpoint. Installing a renderer plugin cannot enable CDP after startup.

Tray and New Window child processes do not inherit a debugging port; each debuggable process
requires an explicit, independent startup choice.

## Visible disclosure

An active CDP endpoint has a persistent indicator across all Desktop surfaces. The indicator is
owned by the application shell, so plugin failure, disposal, or an absent automation client cannot
hide it. It identifies a startup capability, not current client activity. Opening local DevTools
alone does not show the indicator; reduced motion preserves a static disclosure.

## Driver contract

The active generation installs `globalThis.driver` with one method:

`whenWorkbenchRestored(): Promise<void>`

Readiness requires the application surface to be mounted and the generation to be active.
Disposal before readiness rejects the wait. Activation must not replace a foreign driver, and
disposal may remove only the object owned by that generation. Private stores, preload APIs,
generic AppServer requests, and host process controls are not automation contracts.

## Playwright CLI session

The supported attachment flow uses the repository's official Playwright CLI dependency:

1. Validate the caller-supplied loopback CDP endpoint.
2. Attach a named session, select the page carrying the driver, and await readiness.
3. Reuse that session for accessible-DOM interaction and diagnostics.
4. Detach without closing the browser or terminating Desktop processes.

## Agent access

On local connections, a Desktop started with CDP declares `additionalContext["desktop.cdp"]` on
its threads ([Desktop client §5.14](../clients/desktop-client.md#514-desktop-runtime-thread-tools)).
It gives the loopback endpoint and tells the agent that the endpoint is the window the user is
working in, so it attaches with the session flow above and never closes, reloads, or restarts it.
Remote threads do not receive it, because their commands run on another machine.

## Security

CDP is unauthenticated and highly privileged. Only explicit startup and caller-provided loopback
endpoints are supported; the workflow does not discover or scan for instances, and Desktop
advertises its own endpoint only to its local threads.
Desktop extension trust does not add a second CDP permission system.

## State and lifecycle

Attachment uses the existing profile and workspace. It does not seed, reset, snapshot, or restore
application state. Detach or automation-client failure leaves Desktop and its services running.
