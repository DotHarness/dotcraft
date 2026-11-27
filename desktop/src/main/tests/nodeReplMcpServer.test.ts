import type { BrowserWindow } from 'electron'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Client } from '@modelcontextprotocol/sdk/client/index.js'
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js'
import { ElicitRequestSchema } from '@modelcontextprotocol/sdk/types.js'
import { NodeReplMcpServer, type NodeReplMcpWorkspaceBinding } from '../repl/nodeReplMcpServer'

function fakeWindow(id = 1): BrowserWindow {
  return { id, webContents: { id: 100 + id }, isDestroyed: () => false } as unknown as BrowserWindow
}

describe('NodeReplMcpServer', () => {
  const servers: NodeReplMcpServer[] = []
  const clients: Client[] = []

  afterEach(async () => {
    for (const client of clients.splice(0)) {
      await client.close().catch(() => {})
    }
    for (const server of servers.splice(0)) {
      await server.dispose()
    }
  })

  function createFixture() {
    const replManager = {
      evaluate: vi.fn(),
      cancel: vi.fn(() => ({ ok: true })),
      reset: vi.fn()
    }
    const browserManagers = { handleTurnNotification: vi.fn() }
    const computerManager = { handleTurnEnded: vi.fn(), clearThreadApprovals: vi.fn() }
    const rememberAlwaysAllowed = vi.fn(async () => {})
    const server = new NodeReplMcpServer({
      replManager,
      browserManagers,
      computerManager,
      rememberAlwaysAllowed
    })
    servers.push(server)
    return { server, replManager, browserManagers, computerManager, rememberAlwaysAllowed }
  }

  function makeBinding(overrides: Partial<NodeReplMcpWorkspaceBinding> = {}): NodeReplMcpWorkspaceBinding {
    const win = fakeWindow()
    return {
      workspacePath: '/workspace/a',
      projectId: 'proj-a',
      getOwnerWindow: () => win,
      isActive: () => true,
      isComputerEnabled: () => true,
      defaultSurface: () => 'browser',
      retainBrowserTurn: vi.fn(async () => true),
      releaseBrowserTurn: vi.fn(async () => {}),
      ...overrides
    }
  }

  async function connectClient(url: string, token: string, capabilities: Record<string, unknown> = {}): Promise<Client> {
    const client = new Client({ name: 'test-client', version: '1.0.0' }, { capabilities })
    const transport = new StreamableHTTPClientTransport(new URL(url), {
      requestInit: {
        headers: { Authorization: `Bearer ${token}` }
      }
    })
    await client.connect(transport)
    clients.push(client)
    return client
  }

  it('enforces bearer authentication and route availability', async () => {
    const { server } = createFixture()
    const reg = await server.registerWorkspace(makeBinding())

    const unauth = await fetch(reg.url, { method: 'POST', body: '{}' })
    expect(unauth.status).toBe(401)

    const wrongToken = await fetch(reg.url, {
      method: 'POST',
      headers: { Authorization: 'Bearer wrong-token' },
      body: '{}'
    })
    expect(wrongToken.status).toBe(401)

    reg.dispose()
    const gone = await fetch(reg.url, {
      method: 'POST',
      headers: { Authorization: `Bearer ${reg.token}` },
      body: '{}'
    })
    expect(gone.status).toBe(404)
  })

  it('lists NodeReplJs with non-approval annotations and app-only turn_ended', async () => {
    const { server } = createFixture()
    const reg = await server.registerWorkspace(makeBinding())
    const client = await connectClient(reg.url, reg.token)

    const listed = await client.listTools()
    const replTool = listed.tools.find(t => t.name === 'NodeReplJs')
    const turnEndedTool = listed.tools.find(t => t.name === 'turn_ended')

    expect(replTool).toBeDefined()
    expect(replTool?.annotations).toEqual({
      readOnlyHint: false,
      destructiveHint: false,
      openWorldHint: false
    })
    expect(turnEndedTool).toBeDefined()
    expect((turnEndedTool as { _meta?: unknown })?._meta).toEqual({
      ui: { visibility: ['app'] }
    })
  })

  it('evaluates NodeReplJs with _meta callContext, images, surface, and cancellation', async () => {
    const { server, replManager } = createFixture()
    const binding = makeBinding({ isComputerEnabled: () => false })
    const reg = await server.registerWorkspace(binding)
    const client = await connectClient(reg.url, reg.token)

    replManager.evaluate.mockResolvedValueOnce({
      resultText: 'done',
      logs: ['step 1'],
      images: [{ mediaType: 'image/png', dataBase64: 'AQID' }],
      surface: 'chrome'
    })

    const result = await client.callTool({
      name: 'NodeReplJs',
      arguments: { code: 'await tab.title()', title: 'Read title' },
      _meta: {
        'dotcraft/callContext': {
          threadId: 'thread-1',
          turnId: 'turn-1',
          callId: 'call-1',
          workspacePath: '/workspace/override'
        }
      }
    })

    expect(result.isError).toBeUndefined()
    expect(result.content).toEqual([
      { type: 'text', text: 'step 1\ndone' },
      { type: 'image', data: 'AQID', mimeType: 'image/png' }
    ])
    expect(result.structuredContent).toEqual({ surface: 'chrome' })
    expect(binding.retainBrowserTurn).toHaveBeenCalledWith('thread-1', 'turn-1')
    expect(replManager.evaluate).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        threadId: 'thread-1',
        turnId: 'turn-1',
        evaluationId: 'call-1',
        workspacePath: '/workspace/override',
        projectKey: 'proj-a',
        allowComputerUse: false
      })
    )

    let releaseEval!: () => void
    let started!: () => void
    const evalStarted = new Promise<void>(resolve => { started = resolve })
    replManager.evaluate.mockImplementationOnce(() => {
      started()
      return new Promise(resolve => {
        releaseEval = () => resolve({ logs: [], images: [], error: 'cancelled' })
      })
    })

    const controller = new AbortController()
    const pending = client.callTool(
      {
        name: 'NodeReplJs',
        arguments: { code: 'await sleep(60000)' },
        _meta: {
          'dotcraft/callContext': { threadId: 'thread-1', turnId: 'turn-1', callId: 'call-cancel' }
        }
      },
      undefined,
      { signal: controller.signal }
    )
    await evalStarted
    controller.abort()
    await expect(pending).rejects.toThrow()
    await vi.waitFor(() => {
      expect(replManager.cancel).toHaveBeenCalledWith('thread-1', 'call-cancel')
    })
    releaseEval()
  })

  it('bridges Computer Use approvals through MCP elicitation and persists acceptAlways', async () => {
    const { server, replManager, rememberAlwaysAllowed } = createFixture()
    const reg = await server.registerWorkspace(makeBinding())
    const client = await connectClient(reg.url, reg.token, { elicitation: { form: {} } })

    const seenElicitations: Array<Record<string, unknown>> = []
    client.setRequestHandler(ElicitRequestSchema, async (req) => {
      seenElicitations.push(req.params as Record<string, unknown>)
      const target = ((req.params as { _meta?: { target?: string } })._meta)?.target
      if (target === 'notepad.exe') {
        return { action: 'accept', content: { decision: 'acceptAlways' } }
      }
      if (target === 'calc.exe') {
        return { action: 'accept', content: { decision: 'acceptForSession' } }
      }
      return { action: 'decline' }
    })

    replManager.evaluate.mockImplementationOnce(async (_win, params) => {
      const a = await params.requestApproval({
        evaluationId: params.evaluationId,
        approvalType: 'computerUse',
        operation: 'focus_app',
        target: 'notepad.exe',
        targetLabel: 'Notepad'
      })
      const b = await params.requestApproval({
        evaluationId: params.evaluationId,
        approvalType: 'computerUse',
        operation: 'focus_app',
        target: 'calc.exe',
        targetLabel: 'Calculator'
      })
      const c = await params.requestApproval({
        evaluationId: params.evaluationId,
        approvalType: 'computerUse',
        operation: 'focus_app',
        target: 'secret.exe',
        targetLabel: 'Secret'
      })
      return { resultText: `${a},${b},${c}`, logs: [], images: [], surface: 'computer' }
    })

    const result = await client.callTool({
      name: 'NodeReplJs',
      arguments: { code: 'await dotcraft.computer.screenshot()' },
      _meta: { 'dotcraft/callContext': { threadId: 'thread-cu', turnId: 'turn-cu', callId: 'call-cu' } }
    })

    expect(result.content).toEqual([{ type: 'text', text: 'true,true,false' }])
    expect(seenElicitations).toHaveLength(3)
    expect(seenElicitations[0]._meta).toEqual({
      approvalKind: 'computerUse',
      operation: 'focus_app',
      target: 'notepad.exe',
      targetLabel: 'Notepad'
    })
    expect(rememberAlwaysAllowed).toHaveBeenCalledExactlyOnceWith({ id: 'notepad.exe', displayName: 'Notepad' })
  })

  it('handles turn_ended idempotently for main threads and disposes subagents', async () => {
    const { server, replManager, browserManagers, computerManager } = createFixture()
    const binding = makeBinding()
    const reg = await server.registerWorkspace(binding)
    const client = await connectClient(reg.url, reg.token)

    replManager.evaluate.mockResolvedValue({ resultText: 'ok', logs: [], images: [] })

    await client.callTool({
      name: 'NodeReplJs',
      arguments: { code: '1' },
      _meta: { 'dotcraft/callContext': { threadId: 'main-thread', turnId: 'turn-1', callId: 'c1' } }
    })

    for (let i = 0; i < 2; i++) {
      await client.callTool({
        name: 'turn_ended',
        arguments: {},
        _meta: {
          'dotcraft/hookEvent': 'Stop',
          'dotcraft/callContext': { threadId: 'main-thread', turnId: 'turn-1', isSubagent: false }
        }
      })
    }

    expect(computerManager.handleTurnEnded).toHaveBeenCalledExactlyOnceWith('main-thread', 'turn-1')
    expect(browserManagers.handleTurnNotification).toHaveBeenCalledExactlyOnceWith(
      'turn/completed',
      { threadId: 'main-thread', turnId: 'turn-1' },
      '/workspace/a'
    )
    expect(binding.releaseBrowserTurn).toHaveBeenCalledExactlyOnceWith('main-thread', 'turn-1')
    expect(replManager.reset).not.toHaveBeenCalled()

    await client.callTool({
      name: 'NodeReplJs',
      arguments: { code: '2' },
      _meta: { 'dotcraft/callContext': { threadId: 'sub-thread', turnId: 'sub-turn', callId: 'c2', isSubagent: true } }
    })

    await client.callTool({
      name: 'turn_ended',
      arguments: {},
      _meta: {
        'dotcraft/hookEvent': 'SubagentStop',
        'dotcraft/callContext': { threadId: 'sub-thread', turnId: 'sub-turn', isSubagent: true }
      }
    })

    expect(computerManager.handleTurnEnded).toHaveBeenCalledWith('sub-thread', 'sub-turn')
    expect(computerManager.clearThreadApprovals).toHaveBeenCalledWith('sub-thread')
    expect(replManager.reset).toHaveBeenCalledWith('sub-thread', { preserveBrowserTabs: false })
  })

  it('rejects reusing an active thread id from a different workspace route', async () => {
    const { server, replManager } = createFixture()
    replManager.evaluate.mockResolvedValue({ resultText: 'ok', logs: [], images: [] })

    const regA = await server.registerWorkspace(makeBinding({ workspacePath: '/workspace/a', projectId: 'a' }))
    const regB = await server.registerWorkspace(makeBinding({ workspacePath: '/workspace/b', projectId: 'b' }))
    const clientA = await connectClient(regA.url, regA.token)
    const clientB = await connectClient(regB.url, regB.token)

    const ok = await clientA.callTool({
      name: 'NodeReplJs',
      arguments: { code: '1' },
      _meta: { 'dotcraft/callContext': { threadId: 'shared-thread', turnId: 't1', callId: 'c1' } }
    })
    expect(ok.isError).toBeUndefined()

    const rejected = await clientB.callTool({
      name: 'NodeReplJs',
      arguments: { code: '2' },
      _meta: { 'dotcraft/callContext': { threadId: 'shared-thread', turnId: 't2', callId: 'c2' } }
    })
    expect(rejected.isError).toBe(true)
    expect(JSON.stringify(rejected.content)).toContain('another workspace or window')
  })
})
