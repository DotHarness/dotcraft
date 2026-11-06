import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

const runtime = vi.hoisted(() => ({ evaluate: vi.fn(), cancel: vi.fn(), reset: vi.fn() }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: runtime }))
vi.mock('electron', () => ({ BrowserWindow: vi.fn() }))
vi.mock('../chatFoldersIpc', () => ({ createChatFolderFromSettings: vi.fn() }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: { handleTurnEnded: vi.fn() }, rememberAlwaysAllowedApp: vi.fn() }))
vi.mock('../desktopRuntimeThreadTools', () => ({ handleDesktopRuntimeThreadToolCall: vi.fn(), resetDesktopThreadToolBindings: vi.fn() }))
vi.mock('../ipcBridge', () => ({ broadcastServerRequest: vi.fn(), createServerRequestBridge: vi.fn() }))
vi.mock('../viewerTerminal', () => ({ viewerTerminalManager: {} }))
vi.mock('../windowAttentionNotifier', () => ({ NEW_TASK_OFFER_ATTENTION: 'offer' }))

import { handleWorkspaceServerRequest } from '../windows/windowRequests'
import { handleWorkspaceNodeReplNotification } from '../windows/windowNodeReplRequests'

const clients: EventEmitter[] = []
function entry(key: string): WorkspaceConnectionEntry {
  const client = Object.assign(new EventEmitter(), {
    retainBrowserTurn: vi.fn(async () => {}),
    releaseBrowserTurn: vi.fn(async () => {}),
    sendRequest: vi.fn(async () => ({ approved: true }))
  })
  clients.push(client)
  return {
    key, projectId: key, kind: 'local', workspacePath: `/identity/${key}`, localWorkspacePath: `/local/${key}`,
    wsUrl: `ws://${key}`, client: client as unknown as WorkspaceConnectionEntry['client'], role: 'foreground',
    connected: true, connecting: false, lastUsedAt: 0, threads: [], subscribedThreadIds: new Set()
  }
}
function fixture(windowId = 1) {
  const a = entry('a')
  const b = entry('b')
  const owner = { id: windowId, webContents: { id: 100 + windowId }, isDestroyed: () => false } as Electron.BrowserWindow
  const ctx = {
    runtimeIsStopping: () => false, mainWindow: owner, wireClient: a.client, activeRemoteProject: null,
    currentWorkspacePath: '/local/a', workspaceConnections: new Map([[a.key, a], [b.key, b]]),
    handleServerRequestInMain: vi.fn(async () => undefined),
    bridgeServerRequestToRenderer: vi.fn(async () => ({ bridged: true }))
  } as unknown as DesktopWindowRuntime
  const request = (source: WorkspaceConnectionEntry, method: string, params: unknown) =>
    handleWorkspaceServerRequest(ctx, method, params, source, source.role === 'foreground' && ctx.wireClient === source.client, true)
  const evaluate = (source = a, evaluationId = 'eval', threadId = 'thread') => request(source, 'ext/nodeRepl/evaluate', {
    threadId, turnId: 'turn', evaluationId, code: '42'
  })
  const promoteB = () => { a.role = 'secondary'; b.role = 'foreground'; ctx.wireClient = b.client; ctx.currentWorkspacePath = '/local/b' }
  return { a, b, owner, ctx, request, evaluate, promoteB }
}

beforeEach(() => {
  runtime.evaluate.mockResolvedValue({ resultText: '42', images: [], logs: [] })
  runtime.cancel.mockReturnValue({ ok: true })
})
afterEach(() => {
  for (const client of clients.splice(0)) client.emit('disposed')
  vi.clearAllMocks()
})

describe('source-bound Node REPL routing', () => {
  it('continues A after promotion of B and sends workspace, leases and approvals to A', async () => {
    const { a, b, owner, evaluate, promoteB } = fixture()
    await evaluate(a, 'first')
    let retained!: () => void
    vi.mocked(a.client.retainBrowserTurn).mockReturnValueOnce(new Promise<void>(resolve => { retained = resolve }))
    const pending = evaluate(a, 'second')
    promoteB()
    retained()
    await pending
    expect(runtime.evaluate).toHaveBeenLastCalledWith(owner, expect.objectContaining({
      threadId: 'thread', evaluationId: 'second', workspacePath: '/local/a', projectKey: a.projectId
    }))
    const params = runtime.evaluate.mock.calls[1][1]
    await expect(params.requestApproval({ evaluationId: 'second', approvalType: 'computerUse', target: 'app' })).resolves.toBe(true)
    expect(a.client.sendRequest).toHaveBeenCalledWith('ext/nodeRepl/requestApproval', expect.objectContaining({ threadId: 'thread' }), null)
    expect(a.client.retainBrowserTurn).toHaveBeenCalledWith('thread', 'turn')
    expect(b.client.sendRequest).not.toHaveBeenCalled()
    expect(b.client.retainBrowserTurn).not.toHaveBeenCalled()
  })

  it('rejects a replaced connection before evaluation admission, including a late subscription', async () => {
    const { a, ctx, evaluate } = fixture()
    let retained!: () => void
    vi.mocked(a.client.retainBrowserTurn).mockReturnValue(new Promise<void>(resolve => { retained = resolve }))
    const pending = evaluate()
    ctx.workspaceConnections.set(a.key, entry('a'))
    retained()
    await expect(pending).rejects.toThrow('source changed')
    await expect(evaluate()).rejects.toThrow('source is no longer active')
    expect(runtime.evaluate).not.toHaveBeenCalled()
  })

  it('rejects colliding workspace ids and prevents foreign or completed evaluation cancellation', async () => {
    const { a, b, evaluate, request, promoteB } = fixture()
    let finish!: (value: unknown) => void
    let start!: () => void
    const started = new Promise<void>(resolve => { start = resolve })
    runtime.evaluate.mockImplementationOnce(() => {
      start()
      return new Promise(resolve => { finish = resolve })
    })
    const pending = evaluate(a, 'one')
    await started
    promoteB()
    await expect(evaluate(b, 'two')).rejects.toThrow('another workspace or window')
    await expect(request(b, 'ext/nodeRepl/cancel', { threadId: 'thread', evaluationId: 'one' })).resolves.toEqual({ ok: false })
    expect(runtime.cancel).not.toHaveBeenCalled()
    await expect(request(a, 'ext/nodeRepl/cancel', { threadId: 'thread', evaluationId: 'one' })).resolves.toEqual({ ok: true })
    expect(runtime.cancel).toHaveBeenCalledExactlyOnceWith('thread', 'one')
    finish({ resultText: '42' })
    await pending
    await expect(request(a, 'ext/nodeRepl/cancel', { threadId: 'thread', evaluationId: 'one' })).resolves.toEqual({ ok: false })
    expect(runtime.cancel).toHaveBeenCalledOnce()
  })

  it('allows cancelling source-owned work after remote promotion without admitting another evaluation', async () => {
    const { a, b, ctx, evaluate, request, promoteB } = fixture()
    let finish!: (value: unknown) => void
    let start!: () => void
    const started = new Promise<void>(resolve => { start = resolve })
    runtime.evaluate.mockImplementationOnce(() => {
      start()
      return new Promise(resolve => { finish = resolve })
    })
    const pending = evaluate(a, 'running')
    await started
    promoteB()
    b.kind = 'remote'
    ctx.activeRemoteProject = { projectId: b.projectId } as NonNullable<DesktopWindowRuntime['activeRemoteProject']>
    await expect(evaluate(a, 'new')).rejects.toThrow('active local workspace')
    await expect(request(b, 'ext/nodeRepl/cancel', { threadId: 'thread', evaluationId: 'running' })).resolves.toEqual({ ok: false })
    await expect(request(a, 'ext/nodeRepl/cancel', { threadId: 'thread', evaluationId: 'running' })).resolves.toEqual({ ok: true })
    expect(runtime.cancel).toHaveBeenCalledExactlyOnceWith('thread', 'running')
    expect(runtime.evaluate).toHaveBeenCalledOnce()
    finish({ resultText: '42' })
    await pending
  })

  it('does not route a duplicate browser session or mismatched metadata into another runtime', async () => {
    const { a, request } = fixture()
    await request(a, 'ext/nodeRepl/evaluate', { threadId: 'one', evaluationId: '1', code: '42', browserSession: { sessionId: 'shared', threadId: 'one' } })
    await expect(request(a, 'ext/nodeRepl/evaluate', { threadId: 'two', evaluationId: '2', code: '42', browserSession: { sessionId: 'shared' } })).rejects.toThrow('another thread')
    await expect(request(a, 'ext/nodeRepl/evaluate', { threadId: 'two', code: '42', browserSession: { threadId: 'one' } })).resolves.toMatchObject({ error: 'Invalid Node REPL evaluate request.' })
    expect(runtime.evaluate).toHaveBeenCalledOnce()
  })

  it('does not broaden other background tools or allow background remote evaluations', async () => {
    const { a, promoteB, request, evaluate, ctx } = fixture()
    promoteB()
    await expect(request(a, 'item/tool/call', {})).rejects.toThrow('foreground workspace')
    expect(ctx.handleServerRequestInMain).not.toHaveBeenCalled()
    await expect(request(a, 'item/tool/requestUserInput', {})).resolves.toEqual({ bridged: true })
    a.kind = 'remote'
    await expect(evaluate()).rejects.toThrow('active local workspace')
    expect(runtime.evaluate).not.toHaveBeenCalled()
  })

  it('cleans only the source binding on deletion/disposal and does not reset a foreign thread', async () => {
    const { a, b, ctx, evaluate } = fixture()
    await evaluate(a, '1', 'first')
    await evaluate(b, '2', 'second')
    handleWorkspaceNodeReplNotification(b, 'thread/deleted', { threadId: 'first' })
    expect(runtime.reset).not.toHaveBeenCalled()
    a.client.emit('disposed')
    expect(runtime.reset).toHaveBeenCalledExactlyOnceWith('first', { preserveBrowserTabs: true })
    handleWorkspaceNodeReplNotification(b, 'thread/deleted', { threadId: 'second' })
    expect(runtime.reset).toHaveBeenLastCalledWith('second')
    expect(b.client.releaseBrowserTurn).toHaveBeenCalledWith('second', 'turn')
    const replacement = entry('c')
    ctx.workspaceConnections.set(replacement.key, replacement)
    await expect(evaluate(replacement, '3', 'second')).resolves.toMatchObject({ resultText: '42' })
  })

  it('rejects a disposed client even before its wire-close status arrives', async () => {
    const { a, evaluate } = fixture()
    Object.defineProperty(a.client, 'isDisposed', { value: true })
    await expect(evaluate()).rejects.toThrow('source is no longer active')
    expect(runtime.evaluate).not.toHaveBeenCalled()
  })

  it('rejects cross-window reuse even if the source project and thread ids match', async () => {
    const first = fixture()
    const second = fixture(2)
    await first.evaluate()
    await expect(second.evaluate()).rejects.toThrow('another workspace or window')
    expect(runtime.evaluate).toHaveBeenCalledOnce()
  })
})
