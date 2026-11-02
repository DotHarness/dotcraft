import { createServer } from 'http'
import { describe, expect, it, vi } from 'vitest'
import { prepareAppUpdateProcesses, type AppUpdateProcessDependencies } from '../appUpdateProcesses'

const desktop = 'C:\\App\\Desktop.exe'
const runtime = 'C:\\App\\resources\\bin\\runtime.exe'

function fixture() {
  let clock = 0
  const alive = new Set([10, 20, 30, 40])
  const events: string[] = []
  const endpoint = { host: '127.0.0.1', port: 1234, token: 'fixture-control-token', protocolVersion: 1 }
  const registration = (pid: number, executable = desktop) => ({
    version: 1, pid, executable, startedAt: '2026-10-01T00:00:00Z', endpoint
  })
  const controls = {
    desktops: [registration(10), registration(20)],
    tray: { version: 2, pid: 40, executable: desktop, endpoint },
    hub: { pid: 30, binaryPath: runtime, apiBaseUrl: 'http://127.0.0.1:1', token: 'fixture-hub-token' }
  }
  const release = vi.fn(async () => {})
  const dependencies: AppUpdateProcessDependencies = {
    readControls: vi.fn(() => controls),
    identifyDesktop: vi.fn(async (entry) => ({ pid: entry.pid, executable: entry.executable })),
    identifyTray: vi.fn(async () => ({ pid: 40, executable: desktop })),
    quitDesktop: vi.fn(async ({ pid }) => { events.push(`quit:${pid}`); alive.delete(pid); return true }),
    quitTray: vi.fn(async () => { events.push('quit-tray'); alive.delete(40); return true }),
    stopHub: vi.fn(async () => { events.push('stop-hub'); alive.delete(30) }),
    acquireLease: vi.fn(async () => ({ release })),
    isAlive: (pid) => alive.has(pid),
    now: () => clock,
    delay: vi.fn(async (ms) => { clock += ms })
  }
  const options = {
    desktopExecutable: desktop, runtimeExecutable: runtime, currentPid: 10,
    timeoutMs: 1000, quiesce: vi.fn(async (_signal: AbortSignal) => { events.push('quiesce') }), dependencies
  }
  return { options, dependencies, controls, alive, events, release, registration, advance: (ms: number) => { clock += ms } }
}

describe('update preparation for registered processes', () => {
  it('quiesces, verifies and stops other clients before Hub, retaining the lease for installer handoff', async () => {
    const f = fixture()
    const prepared = await prepareAppUpdateProcesses(f.options)
    expect(f.events).toEqual(['quiesce', 'quit:20', 'quit-tray', 'stop-hub'])
    expect(f.dependencies.identifyDesktop).toHaveBeenCalledOnce()
    expect(f.dependencies.quitDesktop).toHaveBeenCalledWith(f.controls.desktops[1])
    expect(f.alive.has(10)).toBe(true)
    expect(f.release).not.toHaveBeenCalled()
    await prepared.release()
    expect(f.release).toHaveBeenCalledOnce()
  })

  it('keeps tray identity and shutdown pinned to the captured endpoint when discovery is replaced', async () => {
    const f = fixture()
    const original = f.controls.tray
    f.dependencies.identifyTray = vi.fn(async (endpoint) => {
      expect(endpoint).toBe(original.endpoint)
      f.controls.tray = { ...original, pid: 41, executable: 'D:\\Other\\Desktop.exe',
        endpoint: { ...original.endpoint, port: 5678, token: 'replacement-control-token' } }
      f.alive.add(41)
      return { pid: original.pid, executable: original.executable }
    })
    f.dependencies.quitTray = vi.fn(async (endpoint) => {
      expect(endpoint).toBe(original.endpoint)
      f.alive.delete(original.pid)
      return true
    })
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.identifyTray).toHaveBeenCalledOnce()
    expect(f.dependencies.quitTray).toHaveBeenCalledOnce()
    expect(f.alive.has(41)).toBe(true)
  })

  it('waits for actual client exits after ACKs before requesting Hub shutdown', async () => {
    const f = fixture()
    f.dependencies.quitDesktop = vi.fn(async () => true)
    f.dependencies.quitTray = vi.fn(async () => true)
    f.dependencies.delay = vi.fn(async (ms) => {
      expect(f.dependencies.stopHub).not.toHaveBeenCalled()
      f.advance(ms)
      f.alive.delete(20)
      f.alive.delete(40)
    })
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.delay).toHaveBeenCalled()
    expect(f.dependencies.stopHub).toHaveBeenCalledOnce()
  })

  it('aborts installation on an acknowledged but unexited client without stopping Hub or terminating anything', async () => {
    const f = fixture()
    f.dependencies.quitDesktop = vi.fn(async () => true)
    await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'clients-exit', blockers: [{ pid: 20 }] })
    expect(f.alive.has(20)).toBe(true)
    expect(f.dependencies.stopHub).not.toHaveBeenCalled()
    expect(f.release).toHaveBeenCalledOnce()
  })

  it('discovers the final Hub owner after other clients finish their in-flight work and exit', async () => {
    const f = fixture()
    f.alive.delete(30)
    let replacement = false
    f.dependencies.readControls = vi.fn(() => ({ ...f.controls, hub: replacement ? { ...f.controls.hub, pid: 31 } : null }))
    f.dependencies.quitDesktop = vi.fn(async ({ pid }) => {
      f.alive.delete(pid)
      f.alive.add(31)
      replacement = true
      return true
    })
    f.dependencies.stopHub = vi.fn(async (hub) => { f.alive.delete(hub.pid) })
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.stopHub).toHaveBeenCalledWith(expect.objectContaining({ pid: 31 }), expect.any(AbortSignal))
  })

  it('ignores other installations and custom runtimes, regardless of their PIDs being alive', async () => {
    const f = fixture()
    f.controls.desktops[1] = f.registration(20, 'D:\\Other\\Desktop.exe')
    f.controls.tray.executable = 'D:\\Other\\Desktop.exe'
    f.controls.hub.binaryPath = 'D:\\Custom\\runtime.exe'
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.identifyDesktop).not.toHaveBeenCalled()
    expect(f.dependencies.identifyTray).not.toHaveBeenCalled()
    expect(f.dependencies.stopHub).not.toHaveBeenCalled()
    expect([...f.alive]).toEqual([10, 20, 30, 40])
  })

  it.each([null, { pid: 99, executable: desktop }, { pid: 20, executable: 'D:\\Other\\Desktop.exe' }])(
    'does not authorize shutdown using an unavailable or changed live identity: %s', async (identity) => {
      const f = fixture()
      f.dependencies.identifyDesktop = vi.fn(async () => identity)
      await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'client-identity', blockers: [{ pid: 20 }] })
      expect(f.dependencies.quitDesktop).not.toHaveBeenCalled()
      expect(f.dependencies.stopHub).not.toHaveBeenCalled()
      expect(f.release).toHaveBeenCalledOnce()
    }
  )

  it('accepts a client that exits naturally while its identity request is in flight', async () => {
    const f = fixture()
    f.dependencies.identifyDesktop = vi.fn(async () => { f.alive.delete(20); return null })
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.quitDesktop).not.toHaveBeenCalled()
    expect(f.dependencies.stopHub).toHaveBeenCalledOnce()
  })

  it('keeps waiting for an unreachable Hub owner instead of treating HTTP failure as exit', async () => {
    const f = fixture()
    f.dependencies.stopHub = vi.fn(async () => { throw new Error('HTTP unavailable') })
    await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'hub-exit', blockers: [{ pid: 30 }] })
    expect(f.alive.has(30)).toBe(true)
    expect(f.release).toHaveBeenCalledOnce()
  })

  it('allows Hub to finish shutdown after the shutdown request loses HTTP', async () => {
    const f = fixture()
    f.dependencies.stopHub = vi.fn(async () => { throw new Error('HTTP closed') })
    f.dependencies.delay = vi.fn(async (ms) => { f.advance(ms); f.alive.delete(30) })
    await prepareAppUpdateProcesses(f.options)
    expect(f.dependencies.delay).toHaveBeenCalled()
    expect(f.release).not.toHaveBeenCalled()
  })

  it.each(['matching', 'pid-mismatch', 'binary-mismatch'])('checks Hub identity before sending credentials: %s', async (identity) => {
    const matching = identity === 'matching'
    const f = fixture()
    const probes: Array<string | undefined> = []
    const shutdowns: Array<string | undefined> = []
    const server = createServer((request, response) => {
      if (request.url === '/v1/status') {
        probes.push(request.headers.authorization)
        response.setHeader('Content-Type', 'application/json')
        response.end(JSON.stringify({ pid: identity === 'pid-mismatch' ? 99 : 30,
          binaryPath: identity === 'binary-mismatch' ? 'D:\\Other\\runtime.exe' : runtime }))
      } else if (request.url === '/v1/shutdown') {
        shutdowns.push(request.headers.authorization)
        f.alive.delete(30)
        response.end('{}')
      } else response.writeHead(404).end()
    })
    await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve))
    try {
      const address = server.address() as { port: number }
      f.controls.hub.apiBaseUrl = `http://127.0.0.1:${address.port}`
      const dependencies: Partial<AppUpdateProcessDependencies> = { ...f.dependencies }
      delete dependencies.stopHub
      const preparing = prepareAppUpdateProcesses({ ...f.options, dependencies })
      if (matching) await expect(preparing).resolves.toBeDefined()
      else await expect(preparing).rejects.toMatchObject({ stage: 'hub-identity' })
      expect(probes).toEqual([undefined])
      expect(shutdowns).toEqual(matching ? ['Bearer fixture-hub-token'] : [])
    } finally {
      await new Promise<void>((resolve) => server.close(() => resolve()))
    }
  })

  it('bounds hung preparation, propagates cancellation, and does not start shutdown after the deadline', async () => {
    const f = fixture()
    f.options.timeoutMs = 15
    let signal: AbortSignal | undefined
    f.options.quiesce = vi.fn(async (value: AbortSignal) => { signal = value; await new Promise<void>(() => {}) })
    await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'quiesce' })
    expect(signal?.aborted).toBe(true)
    expect(f.dependencies.readControls).not.toHaveBeenCalled()
    expect(f.release).toHaveBeenCalledOnce()
  })

  it('does not begin discovery when quiesce has consumed the preparation budget', async () => {
    const f = fixture()
    f.options.quiesce = vi.fn(async () => { f.advance(2_000) })
    await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'quiesce' })
    expect(f.dependencies.readControls).not.toHaveBeenCalled()
    expect(f.release).toHaveBeenCalledOnce()
  })

  it('releases a lease acquired after the attempt has timed out', async () => {
    const f = fixture()
    f.options.timeoutMs = 15
    let acquire!: (lease: { release(): Promise<void> }) => void
    f.dependencies.acquireLease = vi.fn(() => new Promise((resolve) => { acquire = resolve }))
    await expect(prepareAppUpdateProcesses(f.options)).rejects.toMatchObject({ stage: 'lease' })
    acquire({ release: f.release })
    await vi.waitFor(() => expect(f.release).toHaveBeenCalledOnce())
    expect(f.options.quiesce).not.toHaveBeenCalled()
  })
})
