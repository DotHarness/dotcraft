import { app } from 'electron'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const SETTINGS_PORT = '9222'

/** Reads settings.json directly because Settings loading needs app readiness, and the switch must be set before it. */
export function enableCdpDebuggingFromSettings(): void {
  if (app.commandLine.hasSwitch('remote-debugging-port')) return
  try {
    const settings = JSON.parse(readFileSync(join(app.getPath('userData'), 'settings.json'), 'utf8')) as { cdpDebugging?: unknown }
    if (settings.cdpDebugging === true) app.commandLine.appendSwitch('remote-debugging-port', SETTINGS_PORT)
  } catch {
    // Missing or unreadable settings leave CDP off.
  }
}

export function cdpDebuggingEndpoint(): string | undefined {
  const port = app.commandLine.getSwitchValue('remote-debugging-port')
  return /^[1-9]\d*$/.test(port) ? `http://127.0.0.1:${port}` : undefined
}
