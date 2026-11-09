import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ComputerUseManager, type ComputerUseCallContext } from '../computerUse/ComputerUseManager'
import type { CuaDriverClient, DriverToolResult } from '../computerUse/cuaDriverClient'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

const host = vi.hoisted(() => ({ manager: null as unknown, client: null as unknown }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: { evaluate: async () => ({ images: [], logs: [] }), reset: () => {} } }))
vi.mock('../computerUse/runtime', () => ({ get computerUseManager() { return host.manager } }))
vi.mock('../browserUseManager', () => ({ browserUseManager: { handleTurnNotification: vi.fn() } }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: { fromWebSocket: () => host.client } }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../localProjectRecovery', () => ({ bindLocalProjectRecovery: vi.fn() }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))
import { connectViaWebSocket } from '../windows/windowConnections'
import { handleWorkspaceNodeReplRequest } from '../windows/windowNodeReplRequests'

class FakeDriver {
  running = false
  closed = 0
  async start(): Promise<void> { this.running = true }
  async callTool(): Promise<DriverToolResult> { return { content: [], structuredContent: { apps: [] } } }
  async close(): Promise<void> { this.closed++; this.running = false }
  kill(): void { this.running = false }
}
class SourceClient extends EventEmitter {
  isDisposed = false
  private notifyHandler!: (method: string, params: unknown) => void
  onNotification(handler: (method: string, params: unknown) => void): void { this.notifyHandler = handler }
  onServerRequest(): void {}
  async retainBrowserTurn(): Promise<void> {}
  async releaseBrowserTurn(): Promise<void> {}
  notify(method: string, params: unknown): void { this.notifyHandler(method, params) }
  dispose(): void { this.isDisposed = true; this.emit('disposed') }
}

const initialized = { serverInfo: { name: 'fixture', version: '1' }, capabilities: {} }
const clients: SourceClient[] = []
let manager: ComputerUseManager
let driver: FakeDriver
let visible: boolean
let ctx: DesktopWindowRuntime

beforeEach(() => {
  driver = new FakeDriver()
  visible = false
  manager = new ComputerUseManager({
    platform: 'win32', createDriver: () => driver as unknown as CuaDriverClient,
    resolveWindowApps: async () => [], settings: { getAlwaysAllowedApps: () => [] },
    indicator: {
      show: () => { visible = true }, hide: () => { visible = false },
      suspendEscape: run => run()
    },
    userActivity: { cursor: () => ({ x: 0, y: 0 }), idleSeconds: () => 0, now: () => 0 }
  })
  host.manager = manager
  ctx = {
    mainWindow: { id: 1, webContents: { id: 101 }, isDestroyed: () => false },
    settings: { locale: 'en' }, workspaceConnections: new Map(),
    runtimeIsStopping: () => false, resetThreadToolState: () => {}, emitConnectionStatus: () => {},
    reregisterIpcForWorkspace: () => {}, localProjectId: (path: string) => path,
    clearPendingDeviceOpenRetry: () => {}, flushPendingWorkspaceOpenThread: () => {},
    emitWorkspaceProjects: () => {}, resolveConnectionMode: () => 'local',
    restoreEnabledModuleTracking: async () => {}, scheduleSecondaryWorkspaceRefresh: () => {},
    schedulePendingDeviceOpenRetry: () => {}, getThreadTasks: () => ({ handleNotification: () => {} }),
    applyWorkspaceThreadNotification: () => {}
  } as unknown as DesktopWindowRuntime
})
afterEach(async () => {
  for (const client of clients.splice(0)) client.dispose()
  await manager.dispose()
})

async function source(key: string): Promise<{ client: SourceClient; entry: WorkspaceConnectionEntry }> {
  const client = new SourceClient()
  clients.push(client)
  host.client = client
  await connectViaWebSocket(ctx, `/workspace/${key}`, `ws://example.invalid/${key}`)
  client.emit('ready', initialized)
  return { client, entry: ctx.workspaceConnections.get(`/workspace/${key}`)! }
}
async function use(entry: WorkspaceConnectionEntry, request: Record<string, unknown>, effectiveTurnId: string): Promise<void> {
  await handleWorkspaceNodeReplRequest(ctx, entry, 'ext/nodeRepl/evaluate', { code: '42', ...request })
  const context: ComputerUseCallContext = {
    threadId: request.threadId as string, turnId: effectiveTurnId, evaluationId: request.evaluationId as string,
    signal: new AbortController().signal, requestApproval: async () => true,
    pauseTimeout: () => () => {}, emitImage: async () => {}
  }
  await manager.handleHostCall('list_apps', {}, context)
  expect(driver.running).toBe(true)
  expect(visible).toBe(true)
}
function expectClosed(threadId: string, turnId: string): void {
  expect(manager.hasActiveTurn(threadId, turnId)).toBe(false)
  expect(driver.running).toBe(false)
  expect(driver.closed).toBe(1)
  expect(visible).toBe(false)
}

describe('source-owned Computer Use cleanup', () => {
  it.each([
    { event: 'close', turnId: 'explicit', sessionTurnId: 'other', effectiveTurnId: 'explicit' },
    { event: 'disposed', turnId: undefined, sessionTurnId: ' session-turn ', effectiveTurnId: 'session-turn' },
    { event: 'disposed', turnId: undefined, sessionTurnId: '   ', effectiveTurnId: 'evaluation' }
  ])('ends $effectiveTurnId on $event', async ({ event, turnId, sessionTurnId, effectiveTurnId }) => {
    const a = await source('a')
    await use(a.entry, {
      threadId: 'thread', evaluationId: 'evaluation', turnId,
      browserSession: { turnId: sessionTurnId }
    }, effectiveTurnId)
    if (event === 'disposed') a.client.dispose()
    else a.client.emit(event)
    expectClosed('thread', effectiveTurnId)
  })

  it('stays closed when reconnect delivers a terminal notification without another evaluation', async () => {
    const a = await source('a')
    await use(a.entry, { threadId: 'thread', turnId: 'turn', evaluationId: 'evaluation' }, 'turn')
    a.client.emit('close')
    expectClosed('thread', 'turn')
    a.client.emit('reconnected', initialized)
    a.client.notify('turn/completed', { threadId: 'thread', turn: { id: 'turn' } })
    expectClosed('thread', 'turn')
  })

  it('does not close another source current turn on deletion or connection disposal', async () => {
    const a = await source('a')
    await handleWorkspaceNodeReplRequest(ctx, a.entry, 'ext/nodeRepl/evaluate', {
      code: '42', threadId: 'a-thread', turnId: 'a-turn', evaluationId: 'a-evaluation'
    })
    a.entry.role = 'secondary'
    const b = await source('b')
    await use(b.entry, { threadId: 'b-thread', turnId: 'b-turn', evaluationId: 'b-evaluation' }, 'b-turn')
    a.client.notify('thread/deleted', { threadId: 'b-thread' })
    a.client.dispose()
    expect(manager.hasActiveTurn('b-thread', 'b-turn')).toBe(true)
    expect(driver.running).toBe(true)
    expect(driver.closed).toBe(0)
    expect(visible).toBe(true)
    b.client.dispose()
    expectClosed('b-thread', 'b-turn')
  })

  it('ends the evaluation-owned Computer Use turn when metadata is absent and its thread is deleted', async () => {
    const a = await source('a')
    await use(a.entry, { threadId: 'thread', evaluationId: 'evaluation' }, 'evaluation')
    a.client.notify('thread/deleted', { threadId: 'thread' })
    expectClosed('thread', 'evaluation')
  })
})
