import { spawn } from 'node:child_process'
import { join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'

import { CuaDriverClient, type SpawnDriver } from '../cuaDriverClient'

const fixture = join(__dirname, 'fixtures', 'fakeCuaDriver.mjs')

function fakeDriver(version = '0.28.2'): SpawnDriver {
  return () => spawn(process.execPath, [fixture], {
    stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, FAKE_DRIVER_VERSION: version }
  })
}

describe('CuaDriverClient', () => {
  const clients: CuaDriverClient[] = []
  afterEach(() => {
    for (const client of clients.splice(0)) client.kill()
  })

  function create(version?: string): CuaDriverClient {
    const client = new CuaDriverClient(fakeDriver(version), '0.28.2')
    clients.push(client)
    return client
  }

  it('initializes and calls tools over line-delimited JSON-RPC', async () => {
    const client = create()
    await client.start()

    const result = await client.callTool('list_windows', { on_screen_only: true }, 5000)

    expect(result.isError).toBe(false)
    expect(result.content[0]).toEqual({ type: 'text', text: 'called list_windows' })
    expect(result.structuredContent).toEqual({ arguments: { on_screen_only: true } })
    await client.close()
    expect(client.running).toBe(false)
  })

  it('refuses a driver whose version does not match the pin', async () => {
    const client = create('0.1.0')

    await expect(client.start()).rejects.toThrow(/^driver_unavailable: expected cua-driver 0\.28\.2/)
    expect(client.running).toBe(false)
  })

  it('kills the driver and reports an unknown effect when a call times out', async () => {
    const client = create()
    await client.start()

    await expect(client.callTool('hang', {}, 200)).rejects.toThrow(/^timeout: hang/)
    expect(client.running).toBe(false)
  })

  it('fails pending calls when the driver exits', async () => {
    const client = create()
    await client.start()

    await expect(client.callTool('crash', {}, 5000)).rejects.toThrow(/^driver_unavailable: the driver exited/)
  })
})
