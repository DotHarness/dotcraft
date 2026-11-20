import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises'
import { createServer, type Server } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { DevServerSnapshot, DevServerStatus } from '../../shared/devServers'
import { DevServerManager, MAX_SERVERS_PER_FOLDER } from '../devServers/devServerManager'
import { isListening } from '../devServers/ports'

const SERVER_SCRIPT = `
const http = require('node:http')
const port = Number(process.env.PORT)
console.log('booting on ' + port)
http.createServer((_req, res) => res.end('ok')).listen(port, '127.0.0.1', () => console.log('ready on ' + port))
`

let folder: string
let manager: DevServerManager
const held: Server[] = []

async function freePort(): Promise<number> {
  const server = createServer()
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
  const port = (server.address() as { port: number }).port
  await new Promise<void>((resolve) => server.close(() => resolve()))
  return port
}

async function holdPort(port: number): Promise<void> {
  const server = createServer()
  await new Promise<void>((resolve) => server.listen(port, '127.0.0.1', resolve))
  held.push(server)
}

async function writeLaunch(configurations: unknown[]): Promise<void> {
  await mkdir(join(folder, '.craft'), { recursive: true })
  await writeFile(join(folder, '.craft', 'launch.json'), JSON.stringify({ version: '0.0.1', configurations }))
}

function nodeServer(name: string, port: number, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return { name, runtimeExecutable: process.execPath, program: 'server.js', port, ...extra }
}

async function until(serverId: string, statuses: DevServerStatus[], timeoutMs = 10_000): Promise<DevServerSnapshot> {
  const deadline = Date.now() + timeoutMs
  for (;;) {
    const server = manager.get(serverId)
    if (server && statuses.includes(server.status)) return server
    if (Date.now() > deadline) throw new Error(`Server stayed ${server?.status}`)
    await new Promise((resolve) => setTimeout(resolve, 50))
  }
}

async function started(threadId: string, name: string): Promise<DevServerSnapshot> {
  const result = await manager.start({ folder, threadId }, name)
  if (!result.ok || !('server' in result)) throw new Error(`Start failed: ${JSON.stringify(result)}`)
  return result.server
}

beforeEach(async () => {
  folder = await mkdtemp(join(tmpdir(), 'dotcraft-dev-servers-'))
  await writeFile(join(folder, 'server.js'), SERVER_SCRIPT)
  manager = new DevServerManager({ readyPollMs: 50, readyTimeoutMs: 8_000 })
})

afterEach(async () => {
  await manager.disposeAll()
  await Promise.all(held.splice(0).map((server) => new Promise<void>((resolve) => server.close(() => resolve()))))
  await rm(folder, { recursive: true, force: true })
})

describe('DevServerManager', { timeout: 30_000 }, () => {
  it('starts a declared server, reuses it, and stops it', async () => {
    const port = await freePort()
    await writeLaunch([nodeServer('web', port)])

    const first = await started('thread-a', 'web')
    expect(first.status).toBe('starting')
    expect((await until(first.serverId, ['running'])).url).toBe(`http://localhost:${port}`)
    expect(await isListening(port)).toBe(true)

    const again = await manager.start({ folder, threadId: 'thread-a' }, 'WEB')
    expect(again).toMatchObject({ ok: true, reused: true, server: { serverId: first.serverId } })
    expect(manager.logs(first.serverId).join('\n')).toContain(`ready on ${port}`)

    expect(await manager.stop(first.serverId)).toMatchObject({ status: 'stopped' })
    expect(await isListening(port)).toBe(false)
  })

  it('keeps servers of different threads apart', async () => {
    const port = await freePort()
    await writeLaunch([nodeServer('web', port)])
    const own = await started('thread-a', 'web')
    await until(own.serverId, ['running'])

    const other = await started('thread-b', 'web')
    expect(other.serverId).not.toBe(own.serverId)
    expect(other.port).not.toBe(port)
    expect(other.configuredPort).toBe(port)
    expect(manager.servers({ folder, threadId: 'thread-a' }).map((server) => server.serverId)).toEqual([own.serverId])
  })

  it('moves a server off a taken port unless autoPort is false', async () => {
    const port = await freePort()
    await holdPort(port)
    await writeLaunch([nodeServer('moving', port), nodeServer('pinned', port, { autoPort: false })])

    const moved = await started('thread-a', 'moving')
    expect(moved.port).not.toBe(port)
    expect(moved.configuredPort).toBe(port)
    await until(moved.serverId, ['running'])

    const pinned = await manager.start({ folder, threadId: 'thread-a' }, 'pinned')
    expect(pinned).toMatchObject({ ok: false, failure: { kind: 'portInUse', port, holder: 'unknown' } })
    expect(manager.servers({ folder, threadId: 'thread-a' }).find((server) => server.name === 'pinned')?.status).toBe('error')
  })

  it('names the thread that holds a pinned port', async () => {
    const port = await freePort()
    await writeLaunch([nodeServer('web', port, { autoPort: false })])
    await until((await started('thread-a', 'web')).serverId, ['running'])

    const result = await manager.start({ folder, threadId: 'thread-b' }, 'web')
    expect(result).toMatchObject({ ok: false, failure: { kind: 'portInUse', holder: 'otherThread' } })
  })

  it('fails at once when the command does not exist', async () => {
    await writeLaunch([{ name: 'missing', runtimeExecutable: 'dotcraft-command-that-does-not-exist', port: await freePort() }])
    const server = await until((await started('thread-a', 'missing')).serverId, ['error'])
    expect(server.failure?.kind).toBe('spawn')
    expect(manager.logs(server.serverId).join('\n')).not.toContain('Retrying')
  })

  it('retries a server that exits before it is ready', async () => {
    await writeFile(join(folder, 'crash.js'), 'console.log("crashing"); process.exit(3)')
    await writeLaunch([{ name: 'crash', runtimeExecutable: process.execPath, program: 'crash.js', port: await freePort() }])
    const server = await until((await started('thread-a', 'crash')).serverId, ['error'])
    expect(server.failure).toMatchObject({ kind: 'exited', code: 3 })
    expect(manager.logs(server.serverId).join('\n')).toContain('attempt 3 of 3')
  })

  it('caps the servers running in one folder', async () => {
    const ports = await Promise.all(Array.from({ length: MAX_SERVERS_PER_FOLDER + 1 }, freePort))
    await writeLaunch(ports.map((port, index) => nodeServer(`s${index}`, port)))
    for (let index = 0; index < MAX_SERVERS_PER_FOLDER; index += 1) {
      await started(index % 2 === 0 ? 'thread-a' : 'thread-b', `s${index}`)
    }
    const result = await manager.start({ folder, threadId: 'thread-a' }, `s${MAX_SERVERS_PER_FOLDER}`)
    expect(result).toMatchObject({ ok: false, failure: { kind: 'folderLimit', otherThreads: 2 } })
  })

  it('forgets an archived thread and stops its servers', async () => {
    const port = await freePort()
    await writeLaunch([nodeServer('web', port)])
    await until((await started('thread-a', 'web')).serverId, ['running'])

    await manager.forgetThread('thread-a')
    expect(manager.servers({ folder, threadId: 'thread-a' })).toEqual([])
    expect(await isListening(port)).toBe(false)
  })

  it('hands Welcome draft servers to the new thread, even in another folder', async () => {
    await writeLaunch([nodeServer('web', await freePort())])
    const server = await started('welcome-draft', 'web')
    manager.rebindThread('welcome-draft', 'thread-a')
    const worktree = join(folder, 'worktree')
    expect(manager.servers({ folder: worktree, threadId: 'thread-a' }).map((entry) => entry.serverId)).toEqual([server.serverId])
  })

  it('fails a launch that cannot be spawned instead of leaving it starting', async () => {
    await writeLaunch([nodeServer('web', await freePort(), { args: ['bad arg'] })])
    const server = await until((await started('thread-a', 'web')).serverId, ['error'])
    expect(server.failure?.kind).toBe('spawn')
  })

  it('applies the folder limit and port choice across concurrent starts', async () => {
    const ports = await Promise.all(Array.from({ length: MAX_SERVERS_PER_FOLDER }, freePort))
    await writeLaunch([...ports.map((port, index) => nodeServer(`s${index}`, port)), nodeServer('extra', ports[0])])
    const results = await Promise.all([
      ...ports.map((_, index) => manager.start({ folder, threadId: `thread-${index}` }, `s${index}`)),
      manager.start({ folder, threadId: 'thread-extra' }, 'extra')
    ])
    expect(results.filter((result) => !result.ok && result.failure.kind === 'folderLimit')).toHaveLength(1)
    const started = results.flatMap((result) => result.ok && 'server' in result ? [result.server.port] : [])
    expect(new Set(started).size).toBe(started.length)
  })

  it('stops a start that was still in progress when its thread was archived', async () => {
    const port = await freePort()
    await writeLaunch([nodeServer('web', port)])
    const pending = manager.start({ folder, threadId: 'thread-a' }, 'web')
    await manager.forgetThread('thread-a')
    await pending
    expect(manager.servers({ folder, threadId: 'thread-a' })).toEqual([])
    expect(await isListening(port)).toBe(false)
  })

  it('opens attach configurations without starting a process', async () => {
    await writeLaunch([{ name: 'docs', url: 'http://localhost:4000' }])
    expect(await manager.start({ folder, threadId: 'thread-a' }, 'docs')).toEqual({ ok: true, attachUrl: 'http://localhost:4000' })
    expect(manager.servers({ folder, threadId: 'thread-a' })).toEqual([])
  })

  it('explains a missing launch file and an unknown configuration', async () => {
    expect(await manager.start({ folder, threadId: 'thread-a' }, 'web')).toMatchObject({ ok: false, failure: { kind: 'launchFile' } })
    await writeLaunch([nodeServer('web', await freePort())])
    expect(await manager.start({ folder, threadId: 'thread-a' }, 'api')).toMatchObject({ ok: false, failure: { kind: 'launchFile' } })
  })
})
