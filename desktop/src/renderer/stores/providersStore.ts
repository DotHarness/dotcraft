import { create } from 'zustand'

export interface ProviderSummary {
  id: string
  displayName: string
  protocol: string
  authMethod: 'apiKey' | 'chatgptOAuth'
  backendId?: string
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
  available: boolean
  planType: string | null
  primary: ProviderUsageWindow | null
  secondary: ProviderUsageWindow | null
  credits: ProviderUsageCredits | null
  balances: ProviderUsageBalance[] | null
  limitReachedKind: string | null
  fetchedAt: string | null
}

interface ProvidersState {
  providers: ProviderSummary[]
  status: 'idle' | 'loading' | 'ready' | 'error'
  usage: Record<string, ProviderUsageSnapshot>
}

interface ProvidersStore extends ProvidersState {
  reload(): Promise<void>
  reset(): void
  upsert(provider: ProviderSummary): void
  applyUsage(providerId: string, snapshot: ProviderUsageSnapshot | null): void
  refreshUsage(providerId: string): Promise<void>
}

const initialState: ProvidersState = {
  providers: [],
  status: 'idle',
  usage: {}
}

let inFlight: Promise<void> | null = null
const inFlightUsage = new Map<string, Promise<void>>()
let usageNotificationUnsubscribe: (() => void) | null = null

function usageKey(providerId: string): string {
  return providerId.trim().toLowerCase()
}

function parseUsage(raw: unknown): ProviderUsageSnapshot | null {
  if (!raw || typeof raw !== 'object') return null
  const obj = raw as Record<string, unknown>
  if (obj.available === false) {
    return { available: false, planType: null, primary: null, secondary: null, credits: null, balances: null, limitReachedKind: null, fetchedAt: null }
  }
  return {
    available: obj.available === true,
    planType: typeof obj.planType === 'string' ? obj.planType : null,
    primary: parseWindow(obj.primary),
    secondary: parseWindow(obj.secondary),
    credits: parseCredits(obj.credits),
    balances: parseBalances(obj.balances),
    limitReachedKind: typeof obj.limitReachedKind === 'string' && obj.limitReachedKind.trim() !== ''
      ? obj.limitReachedKind
      : null,
    fetchedAt: typeof obj.fetchedAt === 'string' ? obj.fetchedAt : null
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

export const useProvidersStore = create<ProvidersStore>((set, get) => ({
  ...initialState,
  reset() {
    inFlight = null
    inFlightUsage.clear()
    usageNotificationUnsubscribe?.()
    usageNotificationUnsubscribe = null
    set(initialState)
  },
  async reload() {
    if (inFlight) {
      await inFlight
      return
    }
    if (typeof window === 'undefined' || !window.api?.appServer?.sendRequest) {
      set({ status: 'error' })
      return
    }
    const next = (async () => {
      set({ status: 'loading' })
      try {
        const result = await window.api.appServer.sendRequest('provider/list', {}, 15_000) as
          | { providers?: Array<Record<string, unknown>> }
          | null
        const list = (result?.providers ?? [])
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
              webSearchBackend: typeof raw.webSearchBackend === 'string' ? raw.webSearchBackend : undefined
            }
          })
          .filter((p): p is ProviderSummary => p !== null)
        set({ providers: list, status: 'ready' })
      } catch {
        set({ status: 'error' })
      }
    })()
    inFlight = next
    try {
      await next
    } finally {
      inFlight = null
    }
  },
  upsert(provider: ProviderSummary) {
    const current = get().providers
    const filtered = current.filter((p) => p.id.toLowerCase() !== provider.id.toLowerCase())
    set({ providers: [...filtered, provider] })
  },
  applyUsage(providerId: string, snapshot: ProviderUsageSnapshot | null) {
    const key = usageKey(providerId)
    const usage = { ...get().usage }
    if (snapshot) usage[key] = snapshot
    else delete usage[key]
    set({ usage })
  },
  async refreshUsage(providerId: string) {
    const key = usageKey(providerId)
    if (!key) return
    const pending = inFlightUsage.get(key)
    if (pending) {
      await pending
      return
    }
    if (typeof window === 'undefined' || !window.api?.appServer?.sendRequest) return
    ensureUsageSubscription()
    const next = (async () => {
      try {
        const result = await window.api.appServer.sendRequest('provider/auth/usage', { providerId }, 30_000)
        get().applyUsage(providerId, parseUsage(result))
      } catch {
        return
      }
    })()
    inFlightUsage.set(key, next)
    try {
      await next
    } finally {
      inFlightUsage.delete(key)
    }
  }
}))

function ensureUsageSubscription(): void {
  if (usageNotificationUnsubscribe) return
  if (typeof window === 'undefined' || !window.api?.appServer?.onNotification) return
  usageNotificationUnsubscribe = window.api.appServer.onNotification((payload) => {
    if (payload?.method === 'turn/completed') {
      refreshBalanceUsage()
      return
    }
    if (payload?.method !== 'provider/auth/usage/updated') return
    const params = payload.params as { providerId?: unknown } | undefined
    if (typeof params?.providerId !== 'string') return
    useProvidersStore.getState().applyUsage(params.providerId, parseUsage(params))
  })
}

function refreshBalanceUsage(): void {
  const store = useProvidersStore.getState()
  for (const [key, snapshot] of Object.entries(store.usage)) {
    if (snapshot.balances) void store.refreshUsage(key)
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
