import { create } from 'zustand'
import { requestWorkspaceResource, workspaceResourceKey, type WorkspaceResourceScope } from './workspaceResourceScope'

export interface ProviderSummary {
  id: string
  displayName: string
  protocol: string
  authMethod: 'apiKey' | 'chatgptOAuth'
  backendId?: string
  endPoint?: string
  webSearchBackend?: string
}

export interface ProviderUsageWindow {
  usedPercent: number
  windowSeconds: number
  /** UTC ISO-8601 string when the window resets. */
  resetAt: string
}

export interface ProviderUsageCredits {
  hasCredits: boolean
  unlimited: boolean
  balance: string | null
}

export interface ProviderUsageBalance {
  currency: string
  total: string
  granted: string
  toppedUp: string
}

export interface ProviderUsageSnapshot {
  values?: Record<string, number>
  labels?: Record<string, string>
  available: boolean
  planType: string | null
  primary: ProviderUsageWindow | null
  secondary: ProviderUsageWindow | null
  credits: ProviderUsageCredits | null
  balances: ProviderUsageBalance[] | null
  limitReachedKind: string | null
  fetchedAt: string | null
  pluginId: string | null
}

interface ProvidersState {
  providers: ProviderSummary[]
  listResult: unknown | null
  status: 'idle' | 'loading' | 'ready' | 'error'
  error: string | null
  usage: Record<string, ProviderUsageSnapshot>
}

interface ProvidersStore extends ProvidersState {
  activate(scope: WorkspaceResourceScope | null): void
  prune(liveKeys: ReadonlySet<string>): void
  reload(): Promise<void>
  reset(): void
  applyUsage(providerId: string, snapshot: ProviderUsageSnapshot | null): void
  refreshUsage(providerId: string): Promise<void>
}

const initialState: ProvidersState = {
  providers: [],
  listResult: null,
  status: 'idle',
  error: null,
  usage: {}
}

interface ProviderListEntry {
  scope: WorkspaceResourceScope
  providers: ProviderSummary[]
  listResult: unknown | null
  status: ProvidersState['status']
  error: string | null
  inFlight: Promise<void> | null
}

const entries = new Map<string, ProviderListEntry>()
let activeEntry: ProviderListEntry | null = null
let usageGeneration = 0
const inFlightUsage = new Map<string, Promise<void>>()
let usageNotificationUnsubscribe: (() => void) | null = null

function detachUsage(): void {
  usageGeneration += 1
  inFlightUsage.clear()
  usageNotificationUnsubscribe?.()
  usageNotificationUnsubscribe = null
}

function isLive(entry: ProviderListEntry): boolean {
  return entries.get(workspaceResourceKey(entry.scope)) === entry
}

function usageKey(providerId: string): string {
  return providerId.trim().toLowerCase()
}

function parseUsage(raw: unknown): ProviderUsageSnapshot | null {
  if (!raw || typeof raw !== 'object') return null
  const obj = raw as Record<string, unknown>
  if (obj.available === false) {
    return { available: false, planType: null, primary: null, secondary: null, credits: null, balances: null, limitReachedKind: null, fetchedAt: null, pluginId: null }
  }
  return {
    available: obj.available === true,
    ...(obj.values && typeof obj.values === 'object' ? { values: obj.values as Record<string, number> } : {}),
    ...(obj.labels && typeof obj.labels === 'object' ? { labels: obj.labels as Record<string, string> } : {}),
    planType: typeof obj.planType === 'string' ? obj.planType : null,
    primary: parseWindow(obj.primary),
    secondary: parseWindow(obj.secondary),
    credits: parseCredits(obj.credits),
    balances: parseBalances(obj.balances),
    limitReachedKind: typeof obj.limitReachedKind === 'string' && obj.limitReachedKind.trim() !== ''
      ? obj.limitReachedKind
      : null,
    fetchedAt: typeof obj.fetchedAt === 'string' ? obj.fetchedAt : null,
    pluginId: typeof obj.pluginId === 'string' ? obj.pluginId : null
  }
}

function parseWindow(value: unknown): ProviderUsageWindow | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  if (typeof obj.usedPercent !== 'number' || typeof obj.windowSeconds !== 'number' || typeof obj.resetAt !== 'string') {
    return null
  }
  return {
    usedPercent: obj.usedPercent,
    windowSeconds: obj.windowSeconds,
    resetAt: obj.resetAt
  }
}

function parseCredits(value: unknown): ProviderUsageCredits | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  if (typeof obj.hasCredits !== 'boolean') return null
  return {
    hasCredits: obj.hasCredits,
    unlimited: typeof obj.unlimited === 'boolean' ? obj.unlimited : false,
    balance: typeof obj.balance === 'string' ? obj.balance : null
  }
}

function parseBalances(value: unknown): ProviderUsageBalance[] | null {
  if (!Array.isArray(value)) return null
  return value.flatMap((item): ProviderUsageBalance[] => {
    if (!item || typeof item !== 'object') return []
    const obj = item as Record<string, unknown>
    if (typeof obj.currency !== 'string' || typeof obj.total !== 'string') return []
    return [{
      currency: obj.currency,
      total: obj.total,
      granted: typeof obj.granted === 'string' ? obj.granted : '0',
      toppedUp: typeof obj.toppedUp === 'string' ? obj.toppedUp : '0'
    }]
  })
}

export const useProvidersStore = create<ProvidersStore>((set, get) => {
  const publish = (entry: ProviderListEntry): void => {
    if (activeEntry !== entry) return
    set({ providers: entry.providers, listResult: entry.listResult, status: entry.status, error: entry.error })
  }
  return {
    ...initialState,
    activate(scope) {
      const key = scope ? workspaceResourceKey(scope) : null
      if (activeEntry && key === workspaceResourceKey(activeEntry.scope)) return
      if (!activeEntry && !scope) return
      detachUsage()
      if (!scope || !key) {
        activeEntry = null
        set(initialState)
        return
      }
      for (const [oldKey, old] of entries) {
        if (old.scope.connectionKey === scope.connectionKey && old.scope.connectionId !== scope.connectionId) entries.delete(oldKey)
      }
      let entry = entries.get(key)
      if (!entry) {
        entry = { scope, providers: [], listResult: null, status: 'idle', error: null, inFlight: null }
        entries.set(key, entry)
      }
      activeEntry = entry
      set({ providers: entry.providers, listResult: entry.listResult, status: entry.status, error: entry.error, usage: {} })
    },
    prune(liveKeys) {
      for (const [key, entry] of entries) {
        if (entry !== activeEntry && !liveKeys.has(key)) entries.delete(key)
      }
    },
    reset() {
      detachUsage()
      entries.clear()
      activeEntry = null
      set(initialState)
    },
    reload() {
      const entry = activeEntry
      if (!entry) return Promise.resolve()
      if (entry.inFlight) return entry.inFlight
      entry.error = null
      if (entry.status !== 'ready') entry.status = 'loading'
      publish(entry)
      const next = (async () => {
        try {
          const result = await requestWorkspaceResource(entry.scope, 'provider/list', {}, 15_000) as
            | { providers?: Array<Record<string, unknown>> }
            | null
          if (!isLive(entry)) return
          entry.listResult = result
          entry.providers = (result?.providers ?? [])
            .map((raw): ProviderSummary | null => {
              if (!raw || typeof raw !== 'object') return null
              const id = typeof raw.id === 'string' ? raw.id : ''
              if (!id) return null
              const authMethodRaw = typeof raw.authMethod === 'string' ? raw.authMethod.toLowerCase() : ''
              return {
                id,
                displayName: typeof raw.displayName === 'string' ? raw.displayName : id,
                protocol: typeof raw.protocol === 'string' ? raw.protocol : '',
                authMethod: authMethodRaw === 'chatgptoauth' ? 'chatgptOAuth' : 'apiKey',
                backendId: typeof raw.backendId === 'string' && raw.backendId ? raw.backendId : undefined,
                endPoint: typeof raw.endPoint === 'string' ? raw.endPoint : undefined,
                webSearchBackend: typeof raw.webSearchBackend === 'string' ? raw.webSearchBackend : undefined
              }
            })
            .filter((provider): provider is ProviderSummary => provider !== null)
          entry.status = 'ready'
        } catch (error) {
          if (!isLive(entry)) return
          entry.error = error instanceof Error ? error.message : String(error)
          if (entry.status !== 'ready') entry.status = 'error'
        } finally {
          if (isLive(entry)) publish(entry)
        }
      })()
      entry.inFlight = next
      void next.finally(() => { if (entry.inFlight === next) entry.inFlight = null })
      return next
    },
    applyUsage(providerId, snapshot) {
      if (!activeEntry) return
      const key = usageKey(providerId)
      const usage = { ...get().usage }
      if (snapshot) usage[key] = snapshot
      else delete usage[key]
      set({ usage })
    },
    async refreshUsage(providerId) {
      const entry = activeEntry
      const key = usageKey(providerId)
      if (!entry || !key) return
      const pending = inFlightUsage.get(key)
      if (pending) return pending
      ensureUsageSubscription(entry)
      const generation = usageGeneration
      const next = (async () => {
        try {
          const result = await requestWorkspaceResource(entry.scope, 'provider/auth/usage', { providerId: providerId.trim() }, 30_000)
          if (activeEntry === entry && generation === usageGeneration) get().applyUsage(providerId, parseUsage(result))
        } catch {
          return
        }
      })()
      inFlightUsage.set(key, next)
      try {
        await next
      } finally {
        if (inFlightUsage.get(key) === next) inFlightUsage.delete(key)
      }
    }
  }
})

function ensureUsageSubscription(entry: ProviderListEntry): void {
  if (usageNotificationUnsubscribe) return
  const generation = usageGeneration
  usageNotificationUnsubscribe = window.api.appServer.onNotification((payload) => {
    if (generation !== usageGeneration || activeEntry !== entry || payload.foreground === false) return
    if (payload.connectionKey && payload.connectionKey !== entry.scope.connectionKey) return
    if (payload.connectionId && payload.connectionId !== entry.scope.connectionId) return
    if (payload?.method === 'turn/completed') {
      refreshMeteredUsage()
      return
    }
    if (payload?.method !== 'provider/auth/usage/updated') return
    const params = payload.params as { providerId?: unknown } | undefined
    if (typeof params?.providerId !== 'string') return
    useProvidersStore.getState().applyUsage(params.providerId, parseUsage(params))
  })
}

function refreshMeteredUsage(): void {
  const store = useProvidersStore.getState()
  for (const [key, snapshot] of Object.entries(store.usage)) {
    if (snapshot.balances || snapshot.labels?.scope === 'apiKey') void store.refreshUsage(key)
  }
}

export function useProviderSummary(providerId: string | null | undefined): ProviderSummary | null {
  return useProvidersStore((state) => {
    if (!providerId) return null
    return state.providers.find((p) => p.id.toLowerCase() === providerId.toLowerCase()) ?? null
  })
}

export function useProviderUsage(providerId: string | null | undefined): ProviderUsageSnapshot | null {
  return useProvidersStore((state) => providerId ? state.usage[usageKey(providerId)] ?? null : null)
}
