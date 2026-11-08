import { afterAll, afterEach, describe, expect, it, vi } from 'vitest'
import { NodeReplManager, nodeReplManager } from '../nodeReplManager'
import { teardownRuntime } from '../windows/windowLifecycle'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'
import { disposeWorkspaceConnection } from '../windows/windowProjects'
import { disposeDeviceProjectConnection } from '../windows/windowDeviceConnections'
import { createReplWorkerFixture } from './helpers/replWorkerFixture'

vi.mock('electron', () => ({ app: { getAppPath: () => process.cwd() }, BrowserWindow: vi.fn() }))
vi.mock('../ipcBridge', () => ({
  getChannelModuleManager: vi.fn(), registerIpcHandlers: vi.fn(),
  restoreModuleTrackingByChannelName: vi.fn(), unregisterIpcHandlers: vi.fn()
}))
vi.mock('../desktopPet', () => ({ restoreDesktopPet: vi.fn() }))

const workerFixture = createReplWorkerFixture()
afterAll(() => workerFixture.dispose())

const owner = (id: number) => ({ id, isDestroyed: () => false, webContents: { id } }) as Electron.BrowserWindow
const runtime = (win: Electron.BrowserWindow) => ({
  mainWindow: win,
  getThreadTasks: () => ({ abandonOffersForOwner: vi.fn() }),
  clearActiveRemoteReconnectTimer: vi.fn(), resetThreadToolState: vi.fn(),
  deviceOpenGeneration: 0, workspaceConnections: new Map(),
  userInputAutoResolution: { clear: vi.fn() }, setActiveRemoteProject: vi.fn()
}) as unknown as DesktopWindowRuntime

const managers: NodeReplManager[] = []
afterEach(async () => {
  await Promise.all(managers.splice(0).map(manager => manager.disposeAll()))
  vi.restoreAllMocks()
})

function createManager(onDisplay: () => void = () => {}) {
  const browserManager = {
    prepareNodeRepl: vi.fn(async () => ({
      display: async () => { onDisplay() }, collect: () => ({ images: [], logs: [] })
    })),
    abortEvaluation: vi.fn(), reset: vi.fn(() => ({ ok: true }))
  }
  const manager = new NodeReplManager(browserManager as never, workerFixture.fork)
  managers.push(manager)
  vi.spyOn(nodeReplManager, 'disposeOwner').mockImplementation(id => manager.disposeOwner(id))
  return { manager, browserManager }
}

describe('window REPL teardown', () => {
  it('releases foreground and secondary device bridges and cancels their retries on window teardown', async () => {
    vi.useFakeTimers()
    try {
      const ctx = runtime(owner(33))
      const retry = vi.fn()
      const entries = ['foreground', 'secondary'].map((role, index) => ({
        key: `remote:device:computer:${index}`, role,
        client: { dispose: vi.fn() },
        device: { bridge: { close: vi.fn() }, retryTimer: setTimeout(retry, 1000), generation: 0 }
      })) as unknown as WorkspaceConnectionEntry[]
      const bridges = entries.map(entry => entry.device!.bridge!)
      ctx.workspaceConnections = new Map(entries.map(entry => [entry.key, entry]))
      ctx.wireClient = entries[0].client
      ctx.disposeDeviceProjectConnection = entry => disposeDeviceProjectConnection(ctx, entry)
      ctx.disposeWorkspaceConnection = entry => disposeWorkspaceConnection(ctx, entry)
      ctx.emitWorkspaceProjects = vi.fn()
      ctx.runtimeIsStopping = () => true

      await teardownRuntime(ctx, 'window close', { clearWorkspace: true })
      await vi.advanceTimersByTimeAsync(1000)

      expect(ctx.workspaceConnections.size).toBe(0)
      expect(ctx.wireClient).toBeNull()
      for (const bridge of bridges) expect(bridge.close).toHaveBeenCalledOnce()
      for (const entry of entries) expect(entry.client.dispose).toHaveBeenCalledOnce()
      expect(retry).not.toHaveBeenCalled()
    } finally {
      vi.clearAllTimers()
      vi.useRealTimers()
    }
  })

  it('cancels admission before a runtime exists while leaving the window alive', async () => {
    const { manager, browserManager } = createManager()
    const first = owner(11)
    const pending = manager.evaluate(first, { threadId: 'not-started', code: '42' })
    await teardownRuntime(runtime(first), 'reconnect')
    expect((await pending).error).toContain('cancelled before it started')
    expect(browserManager.prepareNodeRepl).not.toHaveBeenCalled()
    expect((await manager.evaluate(first, { threadId: 'not-started', code: '42' })).resultText).toBe('42')
  })

  it('cancels active and queued work on reconnect without invalidating another owner', async () => {
    let started!: () => void
    const ready = new Promise<void>(resolve => { started = resolve })
    const { manager, browserManager } = createManager(started)
    const first = owner(11)
    const second = owner(22)
    await manager.evaluate(second, { threadId: 'sibling', code: 'let retained = 2' })
    const active = manager.evaluate(first, {
      threadId: 'shared', evaluationId: 'active',
      code: 'await display({ mediaType: "image/png", dataBase64: "AQID" }); await new Promise(() => {})'
    })
    await ready
    const queued = manager.evaluate(first, { threadId: 'shared', evaluationId: 'queued', code: 'let stale = true' })
    const siblingQueued = manager.evaluate(second, { threadId: 'shared', code: 'typeof stale' })
    await teardownRuntime(runtime(first), 'workspace-switch')
    expect((await active).error).toContain('connection closed')
    expect((await queued).error).toContain('cancelled before it started')
    expect((await siblingQueued).resultText).toBe('undefined')
    expect(browserManager.prepareNodeRepl).not.toHaveBeenCalledWith(first, expect.objectContaining({ evaluationId: 'queued' }))
    expect((await manager.evaluate(second, { threadId: 'sibling', code: 'retained' })).resultText).toBe('2')
  })
})
