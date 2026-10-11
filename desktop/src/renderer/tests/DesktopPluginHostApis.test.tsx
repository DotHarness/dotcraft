import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  createDesktopPluginHost,
  disposeDesktopPluginCleanupScope,
  type DesktopPluginCleanupScope
} from '../plugins/desktopPluginHost'
import type { PluginEntry } from '../stores/pluginStore'
import { useConnectionStore } from '../stores/connectionStore'
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
    rootPath: `X:\\fixtures\\${id}`,
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

function nextConnection(): void {
  useConnectionStore.setState((state) => ({ connectionEpoch: state.connectionEpoch + 1 }))
}

function appServer(
  onToolCall: (params: Record<string, unknown>) => unknown = () => ({ content: [] }),
  onThreadStart: () => void = () => {}
) {
  let started = 0
  const sendRequestRaw = vi.fn(async (method: string, params?: unknown) => {
    if (method === 'thread/start') {
      onThreadStart()
      return { thread: { id: `hidden-${++started}` } }
    }
    if (method === 'mcpServer/tool/call') return onToolCall(params as Record<string, unknown>)
    throw new Error(`unexpected ${method}`)
  })
  installDesktopApiMock({ appServer: { sendRequestRaw } })
  const calls = (method: string) => sendRequestRaw.mock.calls.filter(([name]) => name === method).map(([, params]) => params)
  return { sendRequestRaw, calls }
}

beforeEach(() => {
  nextConnection()
})

afterEach(() => {
  for (const scope of scopes.splice(0)) disposeDesktopPluginCleanupScope(scope)
})

describe('Desktop Plugin navigation.openExternal', () => {
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

describe('Desktop Plugin open targets', () => {
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

describe('Desktop Plugin ui.pickFolder', () => {
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

describe('Desktop Plugin mcp.callTool', () => {
  it('shares one hidden deny-approval thread across plugins and concurrent first calls', async () => {
    const server = appServer((params) => ({ content: [{ type: 'text', text: String(params.tool) }] }))

    const [first, second] = await Promise.all([
      host('one.desktop').mcp.callTool({ server: 'board', tool: 'list', arguments: { limit: 1 } }),
      host('two.desktop').mcp.callTool({ server: 'board', tool: 'count' })
    ])

    expect(first).toEqual({ content: [{ type: 'text', text: 'list' }] })
    expect(second).toEqual({ content: [{ type: 'text', text: 'count' }] })
    expect(server.calls('thread/start')).toEqual([{ ephemeral: true, config: { approvalPolicy: 'deny' } }])
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

  it('starts a new hidden thread after the connection changes', async () => {
    const server = appServer()
    const mcp = host().mcp

    await mcp.callTool({ server: 'board', tool: 'list' })
    nextConnection()
    await mcp.callTool({ server: 'board', tool: 'list' })

    expect(server.calls('thread/start')).toHaveLength(2)
    expect(server.calls('mcpServer/tool/call').map((params) => (params as { threadId: string }).threadId))
      .toEqual(['hidden-1', 'hidden-2'])
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
