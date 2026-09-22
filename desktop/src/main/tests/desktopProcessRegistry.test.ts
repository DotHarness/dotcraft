import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'fs'
import net from 'net'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  quitRegisteredDesktopProcesses,
  registerDesktopProcess,
  type DesktopProcessRegistrationHandle
} from '../desktopProcessRegistry'

const temporaryDirectories: string[] = []

function temporaryRegistry(name: string): string {
  const directory = join(
    tmpdir(),
    `dotcraft-desktop-processes-${name}-${process.pid}-${Date.now()}-${Math.random().toString(16).slice(2)}`
  )
  temporaryDirectories.push(directory)
  return directory
}

async function sendControlMessage(
  endpoint: DesktopProcessRegistrationHandle['endpoint'],
  payload: Record<string, unknown>
): Promise<{ ok?: boolean } | null> {
  return await new Promise((resolve) => {
    const socket = net.createConnection({ host: endpoint.host, port: endpoint.port })
    let buffer = ''
    let settled = false
    const finish = (value: { ok?: boolean } | null): void => {
      if (settled) return
      settled = true
      socket.destroy()
      resolve(value)
    }
    socket.setEncoding('utf8')
    socket.setTimeout(1_500, () => finish(null))
    socket.on('connect', () => socket.write(`${JSON.stringify(payload)}\n`, 'utf8'))
    socket.on('data', (chunk) => {
      buffer += chunk
      const newline = buffer.indexOf('\n')
      if (newline < 0) return
      try {
        finish(JSON.parse(buffer.slice(0, newline)) as { ok?: boolean })
      } catch {
        finish(null)
      }
    })
    socket.on('error', () => finish(null))
    socket.on('close', () => finish(null))
  })
}

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true })
  }
})

describe('Desktop process registry', () => {
  it('requests a graceful quit from every registered Desktop process', async () => {
    const registry = temporaryRegistry('multiple')
    const firstQuit = vi.fn()
    const secondQuit = vi.fn()
    const handles: DesktopProcessRegistrationHandle[] = []
    try {
      handles.push(await registerDesktopProcess(firstQuit, registry))
      handles.push(await registerDesktopProcess(secondQuit, registry))

      await expect(quitRegisteredDesktopProcesses(registry)).resolves.toEqual({
        requested: 2,
        acknowledged: 2,
        failed: 0
      })
      expect(firstQuit).toHaveBeenCalledOnce()
      expect(secondQuit).toHaveBeenCalledOnce()
    } finally {
      for (const handle of handles) handle.release()
    }
  })

  it('rejects unauthenticated quit requests', async () => {
    const registry = temporaryRegistry('authentication')
    const onQuit = vi.fn()
    const handle = await registerDesktopProcess(onQuit, registry)
    try {
      await expect(sendControlMessage(handle.endpoint, {
        type: 'quit',
        token: 'wrong-token',
        protocolVersion: handle.endpoint.protocolVersion
      })).resolves.toEqual({ ok: false })
      expect(onQuit).not.toHaveBeenCalled()
    } finally {
      handle.release()
    }
  })

  it('removes its registration during normal release', async () => {
    const registry = temporaryRegistry('release')
    const handle = await registerDesktopProcess(vi.fn(), registry)
    expect(existsSync(handle.path)).toBe(true)

    handle.release()

    expect(existsSync(handle.path)).toBe(false)
  })

  it('discards dead and malformed registrations without blocking live processes', async () => {
    const registry = temporaryRegistry('stale')
    const onQuit = vi.fn()
    const handle = await registerDesktopProcess(onQuit, registry)
    try {
      mkdirSync(registry, { recursive: true })
      const live = JSON.parse(readFileSync(handle.path, 'utf8')) as Record<string, unknown>
      writeFileSync(join(registry, 'dead.json'), JSON.stringify({
        ...live,
        pid: 2_147_483_647
      }), 'utf8')
      writeFileSync(join(registry, 'malformed.json'), '{', 'utf8')

      await expect(quitRegisteredDesktopProcesses(registry)).resolves.toEqual({
        requested: 1,
        acknowledged: 1,
        failed: 0
      })
      expect(onQuit).toHaveBeenCalledOnce()
      expect(readdirSync(registry).sort()).toEqual([handle.path.split(/[\\/]/).at(-1)])
    } finally {
      handle.release()
    }
  })
})
