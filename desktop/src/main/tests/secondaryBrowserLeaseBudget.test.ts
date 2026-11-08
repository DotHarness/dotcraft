import { describe, expect, it, vi } from 'vitest'
import { BrowserTurnSubscriptions } from '../browserTurnSubscriptions'
import type { DesktopWindowRuntime } from '../windows/windowRuntime'
import type { WorkspaceConnectionEntry } from '../windows/windowTypes'

vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../computerUse/runtime', () => ({ computerUseManager: {} }))
vi.mock('../defaultChatWorkspace', () => ({ isDefaultChatWorkspace: () => false }))
vi.mock('../DesktopAppServerClient', () => ({ DesktopAppServerClient: {} }))
vi.mock('../ipcBridge', () => ({ broadcastNotification: vi.fn() }))
vi.mock('../ripgrepRuntime', () => ({ resolveDotCraftRuntimeTools: vi.fn() }))
vi.mock('../voice/voiceIpc', () => ({ refreshVoiceChatGptAvailability: vi.fn() }))
vi.mock('../workspaceSetup', () => ({ getWorkspaceStatus: vi.fn() }))
vi.mock('../nodeReplManager', () => ({ nodeReplManager: {} }))
vi.mock('../threadPanelStateIpc', () => ({ forgetThreadPanelState: vi.fn() }))
import { enforceSecondaryWorkspaceBudget, refreshSecondaryWorkspaceConnections } from '../windows/windowSecondaryConnections'

function fixture(count: number) {
  const leases = Array.from({ length: count }, () => new BrowserTurnSubscriptions(async (_method, _params, onResponse) => {
    const result = { currentState: { turnId: 'turn', turnStatus: 'inProgress' } }
    onResponse?.(result)
    return result
  }))
  const entries = leases.map((lease, i) => ({
    key: `/project/${i}`, workspacePath: `/project/${i}`, role: 'secondary', kind: 'local', lastUsedAt: count - i,
    client: { get hasRetainedThreadAttachments() { return lease.retained } }
  })) as WorkspaceConnectionEntry[]
  const connections = new Map(entries.map(entry => [entry.key, entry]))
  const disposed: string[] = []
  const ctx = {
    runtimeIsStopping: () => false, resolveConnectionMode: () => 'local', settings: {},
    ensureDefaultChatConnection: async () => {},
    app: {
      localProjects: { list: () => entries.map(entry => ({ path: entry.workspacePath })) },
      createHubClient: () => ({ listAppServers: async () => entries.map(entry => ({
        workspacePath: entry.workspacePath, endpoints: { appServerWebSocket: `ws://${entry.key}` }
      })) })
    },
    isWorkspaceForeground: () => false, isRunningHubAppServer: () => true,
    normalizeWorkspaceConnectionKey: (path: string) => path,
    createSecondaryWorkspaceConnection: vi.fn(), workspaceConnections: connections,
    disposeWorkspaceConnection: (entry: WorkspaceConnectionEntry) => { disposed.push(entry.key); connections.delete(entry.key) },
    emitWorkspaceProjects: vi.fn()
  } as unknown as DesktopWindowRuntime
  return { ctx, leases, entries, connections, disposed }
}

function addDevice(connections: Map<string, WorkspaceConnectionEntry>, index: number, lastUsedAt: number) {
  const key = `remote:device:computer:${index}`
  const entry = {
    key, projectId: key, kind: 'remote', remote: { source: 'device' },
    workspacePath: '/remote/project', role: 'secondary', lastUsedAt,
    client: { hasRetainedThreadAttachments: false }
  } as WorkspaceConnectionEntry
  connections.set(key, entry)
  return entry
}

describe('secondary browser connection budget', () => {
  it('retains a browser connection beyond eight idle entries until its turn ends', async () => {
    const { ctx, leases, entries, disposed, connections } = fixture(12)
    await leases[10].retain('thread', 'turn')
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(connections.has(entries[10].key)).toBe(true)
    expect(ctx.createSecondaryWorkspaceConnection).toHaveBeenCalledTimes(8)
    expect(disposed).toEqual([entries[8].key, entries[9].key, entries[11].key])
    await leases[10].release('thread', 'turn')
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(connections.has(entries[10].key)).toBe(false)
    expect(disposed).toContain(entries[10].key)
  })

  it('shares the idle budget with recent device projects without rediscovering or dropping them', async () => {
    const { ctx, connections, entries, disposed } = fixture(8)
    const device = addDevice(connections, 1, 100)
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(connections.has(device.key)).toBe(true)
    expect(disposed).toEqual([entries[7].key])
    expect(ctx.createSecondaryWorkspaceConnection).toHaveBeenCalledTimes(7)
    expect(connections.size).toBe(8)
  })

  it('keeps device projects when there are no local projects and enforces their budget in remote mode', async () => {
    const { ctx, connections, disposed } = fixture(0)
    const device = addDevice(connections, 0, 10)
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(connections.has(device.key)).toBe(true)
    ctx.resolveConnectionMode = () => 'remote'
    const older = Array.from({ length: 8 }, (_, index) => addDevice(connections, index + 1, index))
    enforceSecondaryWorkspaceBudget(ctx)
    expect(disposed).toEqual([older[0].key])
    expect(connections.size).toBe(8)
    expect(connections.has(device.key)).toBe(true)
  })

  it('allows a protected working set to temporarily exceed the idle connection budget', async () => {
    const { ctx, leases, connections, disposed } = fixture(12)
    await Promise.all(leases.map((lease, i) => lease.retain(`thread-${i}`, 'turn')))
    await refreshSecondaryWorkspaceConnections(ctx)
    expect(connections.size).toBe(12)
    expect(disposed).toEqual([])
    expect(ctx.createSecondaryWorkspaceConnection).not.toHaveBeenCalled()
  })
})
