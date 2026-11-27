import {
  nodeReplMcpServer,
  type NodeReplMcpRouteRegistration,
  type NodeReplMcpSurface
} from '../repl/nodeReplMcpServer'
import {
  registerClientAdditionalMcpProvider,
  unregisterClientAdditionalMcpProvider,
  type AdditionalMcpServerConfig
} from '../repl/nodeReplMcpBindings'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

interface WorkspaceNodeReplState {
  enabledPlugins: Set<NodeReplMcpSurface>
  registration: NodeReplMcpRouteRegistration | null
  revision: number
  syncPromise: Promise<void> | null
  hasSynced: boolean
  disposed: boolean
}

const NODE_REPL_PLUGINS: ReadonlySet<NodeReplMcpSurface> = new Set(['browser', 'chrome', 'computer'])

const states = new WeakMap<WorkspaceConnectionEntry['client'], WorkspaceNodeReplState>()

export function isCurrentWorkspaceRequestSource(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): boolean {
  return !ctx.runtimeIsStopping() && entry.connected && !entry.client.isDisposed
    && ctx.workspaceConnections.get(entry.key) === entry
    && ctx.mainWindow != null && !ctx.mainWindow.isDestroyed()
}

export function ownsWorkspaceNodeReplThread(entry: WorkspaceConnectionEntry, threadId: string): boolean {
  const routeId = states.get(entry.client)?.registration?.routeId
  return routeId != null && nodeReplMcpServer.ownsThread(routeId, threadId)
}

export function registerWorkspaceNodeReplClient(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry
): void {
  if (entry.kind === 'remote') return
  if (states.has(entry.client)) return

  const state: WorkspaceNodeReplState = {
    enabledPlugins: new Set(),
    registration: null,
    revision: 0,
    syncPromise: null,
    hasSynced: false,
    disposed: false
  }
  states.set(entry.client, state)

  const sync = (): Promise<void> => syncWorkspaceNodeReplPlugins(ctx, entry, state)

  registerClientAdditionalMcpProvider(entry.client, {
    prepare: () => {
      if (state.disposed) return undefined
      if (!state.hasSynced) return sync()
      return state.syncPromise ?? undefined
    },
    getServers: (): AdditionalMcpServerConfig[] | undefined => {
      if (state.disposed || !state.hasSynced) return undefined
      if (state.enabledPlugins.size === 0 || !state.registration) return []
      return [{
        name: 'node_repl',
        enabled: true,
        transport: 'streamableHttp',
        url: state.registration.url,
        headers: { Authorization: `Bearer ${state.registration.token}` }
      }]
    },
    getRevision: () => {
      if (state.disposed || !state.hasSynced) return ''
      const route = state.registration?.routeId ?? ''
      const plugins = [...state.enabledPlugins].sort().join(',')
      return `${state.revision}:${route}:${plugins}`
    }
  })

  entry.client.on('ready', () => {
    void sync()
  })
  entry.client.on('reconnected', () => {
    void sync()
  })
  entry.client.on('retained-turn-ended', (threadId: string, turnId: string) => {
    const routeId = state.registration?.routeId
    if (routeId) nodeReplMcpServer.handleRetainedTurnEnded(routeId, threadId, turnId)
  })
  entry.client.on('close', () => releaseWorkspaceNodeReplBindings(entry))
  entry.client.once('disposed', () => releaseWorkspaceNodeReplBindings(entry))
}

export function releaseWorkspaceNodeReplBindings(entry: WorkspaceConnectionEntry): void {
  const state = states.get(entry.client)
  if (!state) return
  state.disposed = true
  state.registration?.dispose()
  state.registration = null
  state.enabledPlugins.clear()
  unregisterClientAdditionalMcpProvider(entry.client)
  states.delete(entry.client)
}

export function handleWorkspaceNodeReplNotification(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): void {
  const state = states.get(entry.client)
  if (!state || state.disposed) return

  if (method === 'thread/deleted' && params && typeof params === 'object') {
    const threadId = (params as { threadId?: unknown }).threadId
    const routeId = state.registration?.routeId
    if (typeof threadId === 'string' && routeId) {
      nodeReplMcpServer.handleThreadDeleted(routeId, threadId)
    }
    return
  }

  if (method === 'plugin/snapshot/updated') {
    void syncWorkspaceNodeReplPlugins(ctx, entry, state)
  }
}

async function syncWorkspaceNodeReplPlugins(
  ctx: DesktopWindowRuntime,
  entry: WorkspaceConnectionEntry,
  state: WorkspaceNodeReplState
): Promise<void> {
  if (state.disposed || entry.kind === 'remote') return
  const task = (async () => {
    try {
      const response = await entry.client.sendRequest<{
        plugins?: Array<{ id?: string; enabled?: boolean; installed?: boolean }>
      }>('plugin/list', { includeDisabled: true })
      if (state.disposed) return

      const nextEnabled = new Set<NodeReplMcpSurface>()
      for (const plugin of response?.plugins ?? []) {
        const id = typeof plugin.id === 'string' ? plugin.id.trim().toLowerCase() : ''
        if (!NODE_REPL_PLUGINS.has(id as NodeReplMcpSurface)) continue
        if (id === 'computer' && process.platform !== 'win32') continue
        if (plugin.installed !== false && plugin.enabled === true) {
          nextEnabled.add(id as NodeReplMcpSurface)
        }
      }

      const changed = !sameStringSet(state.enabledPlugins, nextEnabled)
      state.enabledPlugins = nextEnabled

      if (nextEnabled.size > 0 && !state.registration) {
        const registration = await nodeReplMcpServer.registerWorkspace({
          workspacePath: entry.localWorkspacePath ?? entry.workspacePath,
          projectId: entry.projectId,
          getOwnerWindow: () => ctx.mainWindow,
          isActive: () => !state.disposed && isCurrentWorkspaceRequestSource(ctx, entry),
          isComputerEnabled: () => process.platform === 'win32' && state.enabledPlugins.has('computer'),
          defaultSurface: () => pickDefaultSurface(state.enabledPlugins),
          retainBrowserTurn: (threadId, turnId) => entry.client.retainBrowserTurn(threadId, turnId),
          releaseBrowserTurn: (threadId, turnId) => entry.client.releaseBrowserTurn(threadId, turnId)
        })
        if (state.disposed) {
          registration.dispose()
          return
        }
        state.registration = registration
        state.revision += 1
      } else if (changed) {
        state.revision += 1
      }
      state.hasSynced = true
    } catch (error) {
      if (!state.disposed) {
        console.warn('[desktop] failed to sync NodeRepl MCP plugins', error)
      }
    }
  })()
  state.syncPromise = task
  try {
    await task
  } finally {
    if (state.syncPromise === task) state.syncPromise = null
  }
}

function pickDefaultSurface(enabled: ReadonlySet<NodeReplMcpSurface>): NodeReplMcpSurface | undefined {
  if (enabled.size === 1) return enabled.values().next().value
  if (enabled.has('browser')) return 'browser'
  if (enabled.has('chrome')) return 'chrome'
  if (enabled.has('computer')) return 'computer'
  return undefined
}

function sameStringSet(left: ReadonlySet<string>, right: ReadonlySet<string>): boolean {
  if (left.size !== right.size) return false
  for (const item of left) {
    if (!right.has(item)) return false
  }
  return true
}
