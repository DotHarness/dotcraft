# Desktop E2E and developer debugging

| Field | Value |
| --- | --- |
| Version | 0.7.8 |
| Status | Living |
| Date | 2026-09-28 |

This specification defines explicit CDP startup, the renderer readiness driver, and
non-owning automation attachment. It does not define a scenario suite or state-reset service.

## Architecture

Electron owns the CDP endpoint from process startup. The active Desktop Plugin generation owns
its renderer driver under [Desktop Plugins](desktop-plugins.md#runtime-lifecycle). An automation
client owns only its CDP connection, never the Desktop, tray, Hub, or AppServer lifecycle.

## Startup contract

CDP is disabled unless startup explicitly enables a remote debugging port. Source development
uses `npm run dev:debug` (port `9222` by default); packaged builds accept
`--remote-debugging-port=<port>`. A requested-port collision fails rather than scanning for
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

## Security

CDP is unauthenticated and highly privileged. Only explicit startup and caller-provided loopback
endpoints are supported; the workflow does not advertise, discover, or scan for instances.
Desktop Plugin trust does not add a second CDP permission system.

## State and lifecycle

Attachment uses the existing profile and workspace. It does not seed, reset, snapshot, or restore
application state. Detach or automation-client failure leaves Desktop and its services running.
