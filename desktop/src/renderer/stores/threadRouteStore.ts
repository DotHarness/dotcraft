import { create } from 'zustand'
import type {
  RemoteToolHostInfo,
  RemoteToolHostRouteChangedNotification,
  RemoteToolRouteInfo
} from '@dotcraft/sdk/contracts'
import { readAppServerErrorFields } from '../../shared/appServerError'
import type { SatelliteThreadRoute } from '../../shared/satellites'
import { useConnectionStore } from './connectionStore'
import { useSatellitesStore } from './satellitesStore'
import { useThreadStore } from './threadStore'
import { showToast } from './toastStore'

export interface ThreadRouteError {
  code?: string
  messageKey?: string
  params?: Record<string, unknown>
  fallbackText: string
}

/** A machine chosen on the welcome composer, before the thread that will run on it exists. */
export interface PendingThreadRoute {
  hostId: string
  workspaceId: string
}

export interface PendingRouteFailure {
  hostName: string
  error: unknown
}

export interface ThreadRouteState {
  supported: boolean
  hosts: RemoteToolHostInfo[]
  routes: Record<string, RemoteToolRouteInfo>
  /** Client-only and never persisted: it lives until the first message claims it. */
  pendingRoute: PendingThreadRoute | null
  connecting: string | null
  /** `<generation>:<threadId>` entries whose silent re-apply has already been tried. */
  attempted: Set<string>
  /** Bumped on every AppServer connection so routes and attempts re-arm. */
  generation: number
}

export interface ThreadRouteActions {
  list(threadId?: string): Promise<void>
  connect(threadId: string, hostId: string, workspaceId: string): Promise<void>
  disconnect(threadId: string): Promise<void>
  setPendingRoute(route: PendingThreadRoute | null): void
  applyPendingRoute(threadId: string): Promise<PendingRouteFailure | null>
  handleRouteChanged(params: unknown): void
  resetForConnection(): void
  maybeReapply(threadId: string, options?: { turnRunning?: boolean }): void
}

export type ThreadRouteStore = ThreadRouteState & ThreadRouteActions

export const REAPPLY_DEBOUNCE_MS = 400

const reapplyTimers = new Map<string, ReturnType<typeof setTimeout>>()

function isCapable(): boolean {
  return useConnectionStore.getState().capabilities?.remoteToolHost === true
}

function activeWorkspacePath(): string {
  return useThreadStore.getState().activeThread?.workspacePath?.trim() ?? ''
}

export function threadRouteMemoryKey(workspacePath: string, threadId: string): string {
  return `${workspacePath}::${threadId}`
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value : undefined
}

export function showThreadRouteFailureToast(
  hostName: string,
  error: unknown,
  t: (key: string, vars?: Record<string, string | number>) => string
): void {
  const failure = readThreadRouteError(error)
  // `t` echoes back a key it does not know, and the server's own English sentence
  // reads better than the key.
  const localized = failure.messageKey ? t(failure.messageKey) : failure.messageKey
  showToast({
    type: 'error',
    message: t('error.remoteToolHost.title', { name: hostName }),
    description: localized && localized !== failure.messageKey ? localized : failure.fallbackText
  })
}

export function readThreadRouteError(error: unknown): ThreadRouteError {
  const message = error instanceof Error ? error.message : String(error)
  const { code, data } = readAppServerErrorFields(error)
  const domainCode = text(data?.code) ?? code
  const params = asRecord(data?.params)
  return {
    ...(domainCode ? { code: domainCode } : {}),
    ...(text(data?.messageKey) ? { messageKey: text(data?.messageKey) as string } : {}),
    ...(params ? { params } : {}),
    fallbackText: text(data?.fallbackText) ?? message
  }
}

function routeOf(value: unknown): RemoteToolRouteInfo | null {
  const record = asRecord(value)
  if (!record) return null
  return text(record.threadId) && text(record.hostId) && text(record.workspaceId)
    ? (record as unknown as RemoteToolRouteInfo)
    : null
}

/** The map is replaced wholesale, so a missing entry is how a thread is forgotten. */
async function rememberRoute(threadId: string, entry: SatelliteThreadRoute | null): Promise<void> {
  try {
    const current = await window.api.settings.get()
    const key = threadRouteMemoryKey(activeWorkspacePath(), threadId)
    const next = { ...(current.satelliteRouteByThread ?? {}) }
    if (entry) next[key] = entry
    else delete next[key]
    await window.api.settings.set({ satelliteRouteByThread: next })
  } catch {
    // Memory only shadows a route the server already published, so a failed write is quiet.
  }
}

async function recallRoute(threadId: string): Promise<SatelliteThreadRoute | null> {
  const current = await window.api.settings.get()
  const key = threadRouteMemoryKey(activeWorkspacePath(), threadId)
  return current.satelliteRouteByThread?.[key] ?? null
}

function withoutRoute(
  routes: Record<string, RemoteToolRouteInfo>,
  threadId: string
): Record<string, RemoteToolRouteInfo> {
  if (!(threadId in routes)) return routes
  const next = { ...routes }
  delete next[threadId]
  return next
}

export const useThreadRouteStore = create<ThreadRouteStore>((set, get) => ({
  supported: false,
  hosts: [],
  routes: {},
  pendingRoute: null,
  connecting: null,
  attempted: new Set<string>(),
  generation: 0,

  async list(threadId) {
    if (!isCapable()) {
      set({ supported: false, hosts: [] })
      return
    }
    const generation = get().generation
    const result = await window.api.appServer.sendRequest(
      'remoteToolHost/list',
      threadId ? { threadId } : {}
    )
    if (get().generation !== generation) return
    const route = threadId ? routeOf(result.route) : null
    const hosts = Array.isArray(result.hosts) ? result.hosts : []
    // The same listing carries the lease state the Satellites page overlays on Hub presence.
    useSatellitesStore.getState().applyBusy(hosts)
    set((state) => ({
      supported: true,
      hosts,
      routes: threadId
        ? route
          ? { ...state.routes, [threadId]: route }
          : withoutRoute(state.routes, threadId)
        : state.routes
    }))
  },

  async connect(threadId, hostId, workspaceId) {
    const generation = get().generation
    set({ connecting: threadId })
    try {
      const result = await window.api.appServer.sendRequest('remoteToolHost/connect', {
        threadId,
        hostId,
        workspaceId
      })
      const route = routeOf(result.route)
      if (get().generation === generation && route) {
        set((state) => ({
          routes: { ...state.routes, [threadId]: route },
          attempted: new Set(state.attempted).add(`${generation}:${threadId}`)
        }))
      }
      await rememberRoute(threadId, { hostId, workspaceId, at: new Date().toISOString() })
    } finally {
      set((state) => (state.connecting === threadId ? { connecting: null } : state))
    }
  },

  async disconnect(threadId) {
    const generation = get().generation
    set({ connecting: threadId })
    try {
      await window.api.appServer.sendRequest('remoteToolHost/disconnect', { threadId })
      if (get().generation === generation) {
        set((state) => ({
          routes: withoutRoute(state.routes, threadId),
          attempted: new Set(state.attempted).add(`${generation}:${threadId}`)
        }))
      }
      await rememberRoute(threadId, null)
    } finally {
      set((state) => (state.connecting === threadId ? { connecting: null } : state))
    }
  },

  setPendingRoute(route) {
    set({ pendingRoute: route })
  },

  async applyPendingRoute(threadId) {
    const pending = get().pendingRoute
    if (!pending) return null
    // Cleared before the attempt, so a refusal cannot leak onto the next thread.
    set({ pendingRoute: null })
    try {
      await get().connect(threadId, pending.hostId, pending.workspaceId)
      return null
    } catch (error) {
      const host = get().hosts.find((candidate) => candidate.hostId === pending.hostId)
      return { hostName: host?.displayName ?? pending.hostId, error }
    }
  },

  handleRouteChanged(params) {
    const payload = asRecord(params) as RemoteToolHostRouteChangedNotification | null
    const threadId = text(payload?.threadId)
    if (!threadId) return
    const route = routeOf(payload?.route)
    set((state) => ({
      routes: route ? { ...state.routes, [threadId]: route } : withoutRoute(state.routes, threadId)
    }))
  },

  resetForConnection() {
    for (const timer of reapplyTimers.values()) clearTimeout(timer)
    reapplyTimers.clear()
    set((state) => ({
      supported: false,
      hosts: [],
      routes: {},
      pendingRoute: null,
      connecting: null,
      attempted: new Set<string>(),
      generation: state.generation + 1
    }))
  },

  maybeReapply(threadId, options) {
    if (!threadId || options?.turnRunning === true) return
    const state = get()
    if (!isCapable() || state.routes[threadId] || state.connecting != null) return
    if (state.attempted.has(`${state.generation}:${threadId}`)) return

    const existing = reapplyTimers.get(threadId)
    if (existing) clearTimeout(existing)
    reapplyTimers.set(
      threadId,
      setTimeout(() => {
        reapplyTimers.delete(threadId)
        void reapply(threadId, get, set)
      }, REAPPLY_DEBOUNCE_MS)
    )
  }
}))

/** One silent attempt to restore the thread's remembered machine. */
async function reapply(
  threadId: string,
  get: () => ThreadRouteStore,
  set: (partial: Partial<ThreadRouteState>) => void
): Promise<void> {
  const before = get()
  const generation = before.generation
  const marker = `${generation}:${threadId}`
  if (!isCapable() || before.routes[threadId] || before.attempted.has(marker)) return

  let remembered: SatelliteThreadRoute | null = null
  try {
    remembered = await recallRoute(threadId)
  } catch {
    return
  }
  if (!remembered) return

  const current = get()
  if (current.generation !== generation || current.routes[threadId]) return

  try {
    await current.list(threadId)
  } catch {
    return
  }

  const listed = get()
  if (listed.generation !== generation || listed.routes[threadId]) return
  const host = listed.hosts.find((candidate) => candidate.hostId === remembered.hostId)
  const workspace = host?.workspaces.find((candidate) => candidate.workspaceId === remembered.workspaceId)
  if (!host?.online || !workspace?.available) return

  set({ attempted: new Set(listed.attempted).add(marker) })
  try {
    await listed.connect(threadId, remembered.hostId, remembered.workspaceId)
  } catch {
    // A route the server refused was never real; the chip stays on This PC.
  }
}
