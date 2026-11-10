import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createDesktopPluginHost,
  disposeDesktopPluginCleanupScope,
  type DesktopPluginCleanupScope
} from '../plugins/desktopPluginHost'
import type { PluginEntry } from '../stores/pluginStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useConversationStore } from '../stores/conversationStore'
import { installDesktopApiMock } from './desktopApiMock'

const scopes: DesktopPluginCleanupScope[] = []

function plugin(id: string): PluginEntry {
  return {
    id,
    displayName: id,
    version: '1.0.0',
    enabled: true,
    installed: true,
    installable: false,
    removable: false,
    source: 'local',
    rootPath: `fixtures/${id}`,
    functions: [],
    skills: [],
    apps: [],
    desktop: { entry: './desktop/dist/index.mjs', revision: 'a'.repeat(64), styles: [] },
    mcpServers: [],
    lspServers: []
  }
}

function host(id = 'fixture.desktop'): DesktopPluginHost {
  const scope: DesktopPluginCleanupScope = { active: true, cleanups: new Set() }
  scopes.push(scope)
  return createDesktopPluginHost(plugin(id), id, 'a'.repeat(64), scope)
}

function threadNotFound(): Error {
  return Object.assign(new Error('Thread not found'), { code: 'threadNotFound', rpcCode: -32010 })
}

let connectionSequence = 0

function nextConnection(): void {
  useConnectionStore.setState((state) => ({ connectionId: `connection-${++connectionSequence}`, connectionEpoch: state.connectionEpoch + 1 }))
}

function appServer(
  onToolCall: (params: Record<string, unknown>) => unknown = () => ({ content: [] }),
  onThreadStart: () => void = () => {},
  onThreadResume: () => void = () => {}
) {
  let started = 0
  const sendRequestRaw = vi.fn(async (method: string, params?: unknown) => {
    if (method === 'thread/start') {
      onThreadStart()
      return { thread: { id: `hidden-${++started}` } }
    }
    if (method === 'thread/resume') {
      onThreadResume()
      return { thread: { id: (params as { threadId: string }).threadId } }
    }
    if (method === 'thread/delete') return {}
    if (method === 'mcpServer/tool/call') return onToolCall(params as Record<string, unknown>)
    throw new Error(`unexpected ${method}`)
  })
  const sendConnectionRequest = vi.fn((_key: string, method: string, params?: unknown) => sendRequestRaw(method, params))
  installDesktopApiMock({ appServer: { sendRequestRaw, sendConnectionRequest } })
  const calls = (method: string) => sendRequestRaw.mock.calls.filter(([name]) => name === method).map(([, params]) => params)
  return { sendRequestRaw, sendConnectionRequest, calls }
}

beforeEach(() => {
  useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: `workspace-${++connectionSequence}`, connectionId: 'initial' })
})

afterEach(() => {
  useWorkspaceProjectsStore.getState().reset()
  for (const scope of scopes.splice(0)) disposeDesktopPluginCleanupScope(scope)
})

describe('Desktop extension navigation.openExternal', () => {
  it('opens web URLs in the browser, handles DotCraft links in Desktop, and rejects other schemes', async () => {
    const openExternal = vi.fn().mockResolvedValue(undefined)
    const openDesktopLink = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({ shell: { openExternal, openDesktopLink } })
    const navigation = host().navigation

    await navigation.openExternal('https://example.com/docs')
    await navigation.openExternal('dotcraft://threads/new?path=%2Fp&prompt=hi')

    expect(openExternal).toHaveBeenCalledWith('https://example.com/docs')
    expect(openDesktopLink).toHaveBeenCalledWith('dotcraft://threads/new?path=%2Fp&prompt=hi')
    await expect(navigation.openExternal('mailto:team@example.com')).rejects.toThrow()
    await expect(navigation.openExternal('file:///C:/secret')).rejects.toThrow()
    await expect(navigation.openExternal('not a url')).rejects.toThrow()
    expect(openExternal).toHaveBeenCalledOnce()
    expect(openDesktopLink).toHaveBeenCalledOnce()
  })
})

describe('Desktop extension open targets', () => {
  const targets = [
    { id: 'vscode', label: 'VS Code', iconKey: 'editor', iconDataUrl: 'data:image/png;base64,AA' },
    { id: 'fileManager', label: 'File Explorer', labelKey: 'openTargets.fileManager', iconKey: 'fileManager' },
    { id: 'custom:review', label: 'Review', iconKey: 'editor' },
    { id: 'systemDefault', label: 'Default app', iconKey: 'fileManager', hidden: true }
  ]

  function installTargets(openTargetPreferences?: unknown) {
    const openInTarget = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({
      shell: { listOpenTargets: vi.fn().mockResolvedValue(targets), openInTarget },
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en', openTargetPreferences }) }
    })
    useConversationStore.setState({ workspacePath: 'C:\\repo', remoteWorkspaceActive: false })
    return openInTarget
  }

  it('lists visible targets and opens a path in the chosen or the effective target', async () => {
    const openInTarget = installTargets({ perProject: { 'C:\\repo': 'custom:review' } })
    const navigation = host().navigation

    await expect(navigation.listOpenTargets()).resolves.toEqual([
      { id: 'vscode', label: 'VS Code', icon: 'data:image/png;base64,AA' },
      { id: 'fileManager', label: 'File Explorer' },
      { id: 'custom:review', label: 'Review' }
    ])
    await navigation.openPath('C:\\repo\\src\\app.ts', { target: 'vscode', line: 7, column: 3 })
    await navigation.openPath('src/app.ts')
    await navigation.openPath('\\\\server\\share\\file.txt')

    expect(openInTarget).toHaveBeenNthCalledWith(1, { targetId: 'vscode', path: 'C:\\repo\\src\\app.ts', line: 7, column: 3 })
    expect(openInTarget).toHaveBeenNthCalledWith(2, { targetId: 'custom:review', path: 'C:/repo/src/app.ts' })
    expect(openInTarget).toHaveBeenNthCalledWith(3, { targetId: 'custom:review', path: '\\\\server\\share\\file.txt' })
  })

  it('rejects an unavailable target and a remote workspace', async () => {
    const openInTarget = installTargets()
    const navigation = host().navigation

    await expect(navigation.openPath('C:\\repo\\a.ts', { target: 'zed' })).rejects.toThrow()
    useConversationStore.setState({ remoteWorkspaceActive: true })
    await expect(navigation.listOpenTargets()).rejects.toThrow()
    await expect(navigation.openPath('C:\\repo\\a.ts')).rejects.toThrow()
    expect(openInTarget).not.toHaveBeenCalled()
  })
})

describe('Desktop extension ui.pickFolder', () => {
  it('passes the localized title to the folder dialog and returns its choice', async () => {
    const pickFolder = vi.fn()
      .mockResolvedValueOnce('C:\\Projects\\alpha')
      .mockResolvedValueOnce(null)
    installDesktopApiMock({ workspace: { pickFolder } })
    const ui = host().ui

    await expect(ui.pickFolder({ title: 'Choose a project' })).resolves.toBe('C:\\Projects\\alpha')
    await expect(ui.pickFolder({ title: 'Choose a project' })).resolves.toBeNull()
    expect(pickFolder).toHaveBeenCalledWith({ title: 'Choose a project' })
  })
})

describe('Desktop extension mcp.callTool', () => {
  it('shares one hidden deny-approval thread across plugins and concurrent first calls', async () => {
    const server = appServer((params) => ({ content: [{ type: 'text', text: String(params.tool) }] }))

    const [first, second] = await Promise.all([
      host('one.desktop').mcp.callTool({ server: 'board', tool: 'list', arguments: { limit: 1 } }),
      host('two.desktop').mcp.callTool({ server: 'board', tool: 'count' })
    ])

    expect(first).toEqual({ content: [{ type: 'text', text: 'list' }] })
    expect(second).toEqual({ content: [{ type: 'text', text: 'count' }] })
    expect(server.calls('thread/start')).toEqual([{ identity: { channelName: 'dotcraft-desktop', userId: 'local' }, ephemeral: true, config: { approvalPolicy: 'deny' } }])
    expect(server.calls('mcpServer/tool/call')).toEqual([
      { server: 'board', tool: 'list', arguments: { limit: 1 }, threadId: 'hidden-1' },
      { server: 'board', tool: 'count', arguments: {}, threadId: 'hidden-1' }
    ])
  })

  it('runs on the given thread without starting a hidden one', async () => {
    const server = appServer()

    await host().mcp.callTool({ server: 'board', tool: 'list', threadId: 'thread-visible' })

    expect(server.calls('thread/start')).toEqual([])
    expect(server.calls('mcpServer/tool/call')).toEqual([
      { server: 'board', tool: 'list', arguments: {}, threadId: 'thread-visible' }
    ])
  })

  it('keeps its helper through status updates and resumes it after reconnecting', async () => {
    const server = appServer()
    const mcp = host().mcp

    await mcp.callTool({ server: 'board', tool: 'list' })
    useConnectionStore.setState((state) => ({ connectionEpoch: state.connectionEpoch + 1 }))
    await mcp.callTool({ server: 'board', tool: 'list' })
    expect(server.calls('thread/resume')).toEqual([])

    nextConnection()
    await mcp.callTool({ server: 'board', tool: 'list' })
    expect(server.calls('thread/start')).toHaveLength(1)
    expect(server.calls('thread/resume')).toEqual([{ threadId: 'hidden-1' }])
    expect(server.calls('mcpServer/tool/call').map((params) => (params as { threadId: string }).threadId))
      .toEqual(['hidden-1', 'hidden-1', 'hidden-1'])
  })

  it('recreates a helper lost across a server restart once for concurrent calls', async () => {
    const server = appServer(undefined, undefined, () => { throw threadNotFound() })
    const mcp = host().mcp
    await mcp.callTool({ server: 'board', tool: 'list' })
    nextConnection()
    await Promise.all([
      mcp.callTool({ server: 'board', tool: 'list' }),
      mcp.callTool({ server: 'board', tool: 'count' })
    ])
    expect(server.calls('thread/start')).toHaveLength(2)
    expect(server.calls('thread/resume')).toHaveLength(1)
    expect(server.calls('mcpServer/tool/call').slice(1).map(params => (params as { threadId: string }).threadId))
      .toEqual(['hidden-2', 'hidden-2'])
  })

  it('releases a discarded helper through its still-live original connection', async () => {
    const server = appServer()
    const mcp = host().mcp
    const original = useConnectionStore.getState()
    await mcp.callTool({ server: 'board', tool: 'list' })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'other',
      secondaryLimit: 8,
      projects: [{
        path: 'original', name: 'original', state: 'secondary', loaded: true, running: false,
        threadCount: 0, threads: [], pinned: false,
        connectionKey: original.connectionKey!, connectionId: original.connectionId!
      }]
    })
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: 'other', connectionId: 'other-1' })
    await mcp.callTool({ server: 'board', tool: 'list' })
    expect(server.sendConnectionRequest).toHaveBeenCalledWith(original.connectionKey, 'thread/delete',
      { threadId: 'hidden-1' }, undefined, { connectionId: original.connectionId })
  })

  it('recreates a missing hidden thread once and retries once', async () => {
    let failures = 1
    const server = appServer(() => {
      if (failures-- > 0) throw threadNotFound()
      return { content: [], isError: false }
    })

    await expect(host().mcp.callTool({ server: 'board', tool: 'list' })).resolves.toEqual({ content: [], isError: false })
    expect(server.calls('mcpServer/tool/call').map((params) => (params as { threadId: string }).threadId))
      .toEqual(['hidden-1', 'hidden-2'])

    failures = 2
    await expect(host().mcp.callTool({ server: 'board', tool: 'list' })).rejects.toThrow('Thread not found')
    expect(server.calls('thread/start')).toHaveLength(3)
  })

  it('rejects a call when the connection changes while the hidden thread is starting', async () => {
    const server = appServer(undefined, nextConnection)

    await expect(host().mcp.callTool({ server: 'board', tool: 'list' })).rejects.toThrow('connection changed')
    expect(server.calls('mcpServer/tool/call')).toEqual([])
  })

  it('does not retry a failed call after the connection changes', async () => {
    const server = appServer(() => {
      nextConnection()
      throw threadNotFound()
    })

    await expect(host().mcp.callTool({ server: 'board', tool: 'list' })).rejects.toThrow('Thread not found')
    expect(server.calls('thread/start')).toHaveLength(1)
    expect(server.calls('mcpServer/tool/call')).toHaveLength(1)
  })

  it('does not retry an ordinary tool failure', async () => {
    const server = appServer(() => { throw new Error('MCP server board is not running') })

    await expect(host().mcp.callTool({ server: 'board', tool: 'list' })).rejects.toThrow('not running')
    expect(server.calls('thread/start')).toHaveLength(1)
    expect(server.calls('mcpServer/tool/call')).toHaveLength(1)
  })
})
