import { EventEmitter } from 'node:events'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { ElicitRequestSchema } from '@modelcontextprotocol/sdk/types.js'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

const runtime = vi.hoisted(() => ({
  evaluate: vi.fn(),
  cancel: vi.fn(),
  reset: vi.fn(),
  handleTool: vi.fn(),
  hasTools: vi.fn(),
  createBridge: vi.fn(),
  broadcast: vi.fn(),
  computerHandleTurnEnded: vi.fn(),
  computerClearApprovals: vi.fn()
}))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: runtime }))
vi.mock('../browserUseManager', () => ({ browserUseManager: { handleTurnNotification: vi.fn() } }))
vi.mock('../browserUseManagers', () => ({ browserUseManagers: { handleTurnNotification: vi.fn() } }))
vi.mock('electron', () => ({ BrowserWindow: vi.fn() }))
vi.mock('../chatFoldersIpc', () => ({ createChatFolderFromSettings: vi.fn() }))
vi.mock('../computerUse/runtime', () => ({
  computerUseManager: {
    hasActiveTurn: vi.fn(() => false),
    handleTurnEnded: runtime.computerHandleTurnEnded,
    clearThreadApprovals: runtime.computerClearApprovals
  },
  rememberAlwaysAllowedApp: vi.fn(async () => {})
}))
vi.mock('../desktopRuntimeThreadTools', () => ({ handleDesktopRuntimeThreadToolCall: runtime.handleTool, hasDesktopThreadTools: runtime.hasTools, resetDesktopThreadToolBindings: vi.fn() }))
vi.mock('../ipcBridge', () => ({ broadcastServerRequest: runtime.broadcast, createServerRequestBridge: runtime.createBridge }))
vi.mock('../viewerTerminal', () => ({ viewerTerminalManager: {} }))
vi.mock('../windowAttentionNotifier', () => ({ NEW_TASK_OFFER_ATTENTION: 'offer' }))

import {
  getClientAdditionalMcpRevision,
  getClientAdditionalMcpServers,
  prepareClientAdditionalMcpServers
} from '../repl/nodeReplMcpBindings'
import { nodeReplMcpServer } from '../repl/nodeReplMcpServer'
import {
  handleWorkspaceNodeReplNotification,
  registerWorkspaceNodeReplClient
} from '../windows/windowNodeReplMcp'
import {
  bridgeServerRequestToRenderer,
  handleServerRequestInMain,
  handleWorkspaceServerRequest,
  observeComputerUseApproval
} from '../windows/windowRequests'

const clients: EventEmitter[] = []
const mcpClients: Client[] = []

function entry(key: string, kind: 'local' | 'remote' | 'chat' = 'local', enabledPlugins: string[] = ['browser']): WorkspaceConnectionEntry {
  let plugins = [...enabledPlugins]
  const client = Object.assign(new EventEmitter(), {
    connectionId: `connection-${key}`,
    isDisposed: false,
    retainBrowserTurn: vi.fn(async () => true),
    releaseBrowserTurn: vi.fn(async () => {}),
    sendRequest: vi.fn(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: plugins.map(id => ({ id, installed: true, enabled: true })) }
      }
      return {}
    }),
    setEnabledPlugins: (next: string[]) => { plugins = [...next] }
  })
  clients.push(client)
  return {
    key, projectId: key, kind, workspacePath: `/identity/${key}`, localWorkspacePath: `/local/${key}`,
    wsUrl: `ws://${key}`, client: client as unknown as WorkspaceConnectionEntry['client'], role: 'foreground',
    connected: true, connecting: false, lastUsedAt: 0, threads: []
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
    handleWorkspaceServerRequest(ctx, method, params, source, source.role === 'foreground' && ctx.wireClient === source.client, true, 1, new AbortController().signal)
  const promoteB = () => { a.role = 'secondary'; b.role = 'foreground'; ctx.wireClient = b.client; ctx.currentWorkspacePath = '/local/b' }
  return { a, b, owner, ctx, request, promoteB }
}

async function connectMcp(source: WorkspaceConnectionEntry, capabilities: Record<string, unknown> = {}): Promise<Client> {
  await prepareClientAdditionalMcpServers(source.client)
  const config = getClientAdditionalMcpServers(source.client)?.find(s => s.name === 'node_repl')
  expect(config).toBeDefined()
  const mcpClient = new Client({ name: 'test', version: '1' }, { capabilities })
  await mcpClient.connect(new StreamableHTTPClientTransport(new URL(config!.url), {
    requestInit: { headers: config!.headers }
  }))
  mcpClients.push(mcpClient)
  return mcpClient
}

beforeEach(() => {
  runtime.evaluate.mockResolvedValue({ resultText: '42', images: [], logs: [] })
  runtime.cancel.mockReturnValue({ ok: true })
  runtime.hasTools.mockReturnValue(false)
  runtime.handleTool.mockResolvedValue({ success: true })
})

afterEach(async () => {
  for (const mcpClient of mcpClients.splice(0)) await mcpClient.close().catch(() => {})
  for (const client of clients.splice(0)) client.emit('disposed')
  await nodeReplMcpServer.dispose()
  vi.clearAllMocks()
})

describe('workspace NodeRepl MCP mounting and source-bound routing', () => {
  it('mounts node_repl only for local workspaces with enabled REPL plugins and updates revision on plugin toggles', async () => {
    const { a, ctx } = fixture()
    const remote = entry('r', 'remote', ['browser'])
    ctx.workspaceConnections.set(remote.key, remote)

    registerWorkspaceNodeReplClient(ctx, a)
    registerWorkspaceNodeReplClient(ctx, remote)

    await prepareClientAdditionalMcpServers(a.client)
    await prepareClientAdditionalMcpServers(remote.client)

    const localServers = getClientAdditionalMcpServers(a.client)
    expect(localServers).toHaveLength(1)
    expect(localServers?.[0]).toMatchObject({
      name: 'node_repl',
      enabled: true,
      transport: 'streamableHttp'
    })
    expect(getClientAdditionalMcpServers(remote.client)).toBeUndefined()

    const revBefore = getClientAdditionalMcpRevision(a.client)
    ;(a.client as unknown as { setEnabledPlugins(next: string[]): void }).setEnabledPlugins([])
    handleWorkspaceNodeReplNotification(ctx, a, 'plugin/snapshot/updated', {})
    await prepareClientAdditionalMcpServers(a.client)

    expect(getClientAdditionalMcpServers(a.client)).toEqual([])
    expect(getClientAdditionalMcpRevision(a.client)).not.toBe(revBefore)
  })

  it('restores the node_repl MCP provider after a transient close and reconnect on the same client', async () => {
    const { a, ctx } = fixture()
    registerWorkspaceNodeReplClient(ctx, a)

    a.client.emit('ready')
    await prepareClientAdditionalMcpServers(a.client)
    const firstUrl = getClientAdditionalMcpServers(a.client)?.[0]?.url
    expect(firstUrl).toBeDefined()

    a.client.emit('close')
    expect(getClientAdditionalMcpServers(a.client)).toBeUndefined()

    a.client.emit('reconnected')
    await prepareClientAdditionalMcpServers(a.client)
    const secondUrl = getClientAdditionalMcpServers(a.client)?.[0]?.url
    expect(secondUrl).toBeDefined()
    expect(secondUrl).not.toBe(firstUrl)
  })

  it('marks pending Computer Use elicitations as trusted on Default Chat (chat) connections', async () => {
    const { ctx, owner } = fixture()
    const chat = entry('chat', 'chat', ['computer'])
    ctx.workspaceConnections.set(chat.key, chat)
    registerWorkspaceNodeReplClient(ctx, chat)
    const mcpChat = await connectMcp(chat, { elicitation: { form: {} } })
    let resolveElicit!: (value: { action: 'decline' }) => void
    let notifyElicitStarted!: () => void
    const elicitStarted = new Promise<void>(resolve => { notifyElicitStarted = resolve })
    mcpChat.setRequestHandler(ElicitRequestSchema, () => {
      notifyElicitStarted()
      return new Promise(resolve => { resolveElicit = resolve })
    })
    Object.assign(ctx, {
      userInputAutoResolution: { remove: vi.fn() },
      settings: {},
      app: { notifyWindowAttention: vi.fn(async () => {}) },
      observeComputerUseApproval: vi.fn()
    })
    runtime.createBridge.mockReturnValue({ bridgeId: 'cu-chat', promise: Promise.resolve({ action: 'decline' }) })

    runtime.evaluate.mockImplementationOnce(async (_win, params) => {
      await params.requestApproval({
        evaluationId: params.evaluationId,
        approvalType: 'computerUse',
        operation: 'focus_app',
        target: 'notepad.exe',
        targetLabel: 'Notepad'
      })
      return { resultText: 'ok', images: [], logs: [] }
    })

    const evalCall = mcpChat.callTool({
      name: 'NodeReplJs',
      arguments: { code: '1' },
      _meta: { 'dotcraft/callContext': { threadId: 'chat-thread', turnId: 'turn-1', callId: 'c1' } }
    })
    await elicitStarted

    await bridgeServerRequestToRenderer(
      ctx,
      'mcpServer/elicitation/request',
      {
        threadId: 'chat-thread',
        turnId: 'turn-1',
        serverName: 'node_repl',
        mode: 'form',
        _meta: { approvalKind: 'computerUse', target: 'notepad.exe', targetLabel: 'Notepad' }
      },
      chat,
      1,
      new AbortController().signal
    )
    expect(runtime.broadcast).toHaveBeenLastCalledWith(owner, expect.objectContaining({
      bridgeId: 'cu-chat',
      trustedComputerUse: true
    }))

    resolveElicit({ action: 'decline' })
    await evalCall
  })

  it('keeps background interactions source-bound and promotes their project only on notification click', async () => {
    const { a, ctx, promoteB, owner } = fixture()
    promoteB()
    let onOpen!: (threadId: string) => void
    let opened!: () => void
    const didOpen = new Promise<void>(resolve => { opened = resolve })
    Object.assign(ctx, {
      userInputAutoResolution: { remove: vi.fn() }, settings: {},
      app: { notifyWindowAttention: vi.fn(async (_method: string, _params: unknown, options: { openThread(threadId: string): void }) => { onOpen = options.openThread }) },
      showWindowSafely: vi.fn(), sendOpenThread: vi.fn(() => opened()),
      observeComputerUseApproval: vi.fn(),
      promoteWorkspaceConnection: vi.fn(async () => { a.role = 'foreground'; ctx.wireClient = a.client; return true })
    })
    runtime.createBridge.mockReturnValue({ bridgeId: 'approval', promise: Promise.resolve({ action: 'accept', content: { decision: 'acceptForSession' } }) })
    await bridgeServerRequestToRenderer(ctx, 'mcpServer/elicitation/request', { threadId: 'thread', turnId: 'turn' }, a, 1, new AbortController().signal)
    expect(runtime.broadcast).toHaveBeenCalledWith(owner, expect.objectContaining({
      connectionKey: a.key, connectionId: 'connection-a', bridgeId: 'approval'
    }))
    expect(ctx.observeComputerUseApproval).toHaveBeenCalledWith(owner, expect.any(Object), expect.any(Promise), false)
    observeComputerUseApproval(ctx, owner, { threadId: 'thread', turnId: 'turn' }, Promise.resolve({}), false)
    expect(ctx.showWindowSafely).not.toHaveBeenCalled()
    onOpen('thread')
    await didOpen
    expect(ctx.promoteWorkspaceConnection).toHaveBeenCalledWith(a)
    expect(ctx.sendOpenThread).toHaveBeenCalledWith(owner, 'thread')
  })

  it('routes registered Desktop tools through their background source instead of the foreground', async () => {
    const { a, b, ctx, promoteB, request } = fixture()
    promoteB()
    a.initializeResult = { capabilities: { dynamicToolRebind: true } } as WorkspaceConnectionEntry['initializeResult']
    ctx.desktopThreadToolOptions = vi.fn(() => ({ supportsDynamicToolRebind: true }))
    ctx.handleServerRequestInMain = (method, params, source) => handleServerRequestInMain(ctx, method, params, source)
    runtime.hasTools.mockReturnValue(true)
    const params = { threadId: 'thread', namespace: 'desktop', tool: 'ListThreads', arguments: {} }
    await expect(request(a, 'item/tool/call', params)).resolves.toEqual({ success: true })
    expect(runtime.handleTool).toHaveBeenCalledExactlyOnceWith(a.client, params, a.workspacePath, { supportsDynamicToolRebind: true })
    expect(ctx.desktopThreadToolOptions).toHaveBeenCalledWith(a)
    expect(b.client.sendRequest).not.toHaveBeenCalled()
  })

  it('does not broaden other background tools', async () => {
    const { a, promoteB, request, ctx } = fixture()
    promoteB()
    await expect(request(a, 'item/tool/call', {})).rejects.toThrow('foreground workspace')
    expect(ctx.handleServerRequestInMain).not.toHaveBeenCalled()
    await expect(request(a, 'item/tool/requestUserInput', {})).resolves.toEqual({ bridged: true })
  })

  it('cleans only the source binding on deletion/disposal and disposes subagent tabs on client disposal', async () => {
    const { a, b, ctx } = fixture()
    registerWorkspaceNodeReplClient(ctx, a)
    registerWorkspaceNodeReplClient(ctx, b)
    const mcpA = await connectMcp(a)
    const mcpB = await connectMcp(b)

    await mcpA.callTool({
      name: 'NodeReplJs',
      arguments: { code: '42' },
      _meta: { 'dotcraft/callContext': { threadId: 'first', turnId: 'turn', callId: '1' } }
    })
    await mcpA.callTool({
      name: 'NodeReplJs',
      arguments: { code: '42' },
      _meta: { 'dotcraft/callContext': { threadId: 'sub-1', turnId: 'turn-sub', callId: '2', isSubagent: true } }
    })
    await mcpB.callTool({
      name: 'NodeReplJs',
      arguments: { code: '42' },
      _meta: { 'dotcraft/callContext': { threadId: 'second', turnId: 'turn', callId: '3' } }
    })

    handleWorkspaceNodeReplNotification(ctx, b, 'thread/deleted', { threadId: 'first' })
    expect(runtime.reset).not.toHaveBeenCalled()

    a.client.emit('disposed')
    expect(runtime.reset).toHaveBeenCalledWith('first', { preserveBrowserTabs: true })
    expect(runtime.reset).toHaveBeenCalledWith('sub-1', { preserveBrowserTabs: false })

    handleWorkspaceNodeReplNotification(ctx, b, 'thread/deleted', { threadId: 'second' })
    expect(runtime.reset).toHaveBeenLastCalledWith('second')
    expect(b.client.releaseBrowserTurn).toHaveBeenCalledWith('second', 'turn')
  })
})
