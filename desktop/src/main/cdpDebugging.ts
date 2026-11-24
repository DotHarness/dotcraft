import { spawnSync } from 'node:child_process'
import { app } from 'electron'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const SETTINGS_PORT = '9222'

// Chromium binds after startup and a failed bind is silent, so check the port first as dev:debug does.
const PORT_PROBE = `const s=require('node:net').createServer();s.once('error',()=>process.exit(1));s.listen({host:'127.0.0.1',port:${SETTINGS_PORT},exclusive:true},()=>s.close(()=>process.exit(0)))`

/** Reads settings.json directly because Settings loading needs app readiness, and the switch must be set before it. */
export function enableCdpDebuggingFromSettings(): void {
  if (app.commandLine.hasSwitch('remote-debugging-port') || !cdpDebuggingSetting()) return
  const probe = spawnSync(process.execPath, ['-e', PORT_PROBE], {
    env: { ...process.env, ELECTRON_RUN_AS_NODE: '1' },
    timeout: 5000,
    windowsHide: true
  })
  if (probe.status !== 0) {
    console.warn(`[desktop] CDP debugging stays off because port ${SETTINGS_PORT} is in use`)
    return
  }
  app.commandLine.appendSwitch('remote-debugging-port', SETTINGS_PORT)
}

export function cdpDebuggingEndpoint(): string | undefined {
  const port = app.commandLine.getSwitchValue('remote-debugging-port')
  return /^[1-9]\d*$/.test(port) ? `http://127.0.0.1:${port}` : undefined
}

function cdpDebuggingSetting(): boolean {
  try {
    const settings = JSON.parse(readFileSync(join(app.getPath('userData'), 'settings.json'), 'utf8')) as { cdpDebugging?: unknown }
    return settings.cdpDebugging === true
  } catch {
    return false
  }
}
