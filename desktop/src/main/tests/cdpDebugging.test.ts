import { createServer, type Server } from 'node:net'
import { afterEach, describe, expect, it, vi } from 'vitest'

const electron = vi.hoisted(() => ({
  appendSwitch: vi.fn(),
  userData: ''
}))

vi.mock('electron', () => ({
  app: {
    getPath: () => electron.userData,
    commandLine: { hasSwitch: () => false, appendSwitch: electron.appendSwitch }
  }
}))

import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { enableCdpDebuggingFromSettings } from '../cdpDebugging'

let holder: Server | undefined

afterEach(async () => {
  await new Promise<void>((resolve) => holder ? holder.close(() => resolve()) : resolve())
  holder = undefined
  if (electron.userData) rmSync(electron.userData, { recursive: true, force: true })
  electron.appendSwitch.mockClear()
})

describe('enableCdpDebuggingFromSettings', () => {
  it('leaves CDP off when port 9222 is already held', async () => {
    electron.userData = mkdtempSync(join(tmpdir(), 'dotcraft-cdp-'))
    writeFileSync(join(electron.userData, 'settings.json'), JSON.stringify({ cdpDebugging: true }))
    // The port is unavailable whether this server or another process holds it.
    holder = await new Promise<Server | undefined>((resolve) => {
      const server = createServer()
      server.once('error', () => resolve(undefined))
      server.listen({ host: '127.0.0.1', port: 9222, exclusive: true }, () => resolve(server))
    })

    enableCdpDebuggingFromSettings()

    expect(electron.appendSwitch).not.toHaveBeenCalled()
  })
})
