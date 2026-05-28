import { create } from 'zustand'

export interface ProviderSummary {
  id: string
  displayName: string
  protocol: string
  authMethod: 'apiKey' | 'chatgptOAuth'
  chatGptAccountId: string | null
  chatGptPlanType: string | null
}

export interface ChatGptUsageWindow {
  usedPercent: number
  windowSeconds: number
  /** UTC ISO-8601 string when the window resets. */
  resetAt: string
}

export interface ChatGptUsageCredits {
  hasCredits: boolean
  unlimited: boolean
  balance: string | null
}

export interface ChatGptUsageSnapshot {
  available: boolean
  planType: string | null
  primary: ChatGptUsageWindow | null
  secondary: ChatGptUsageWindow | null
  credits: ChatGptUsageCredits | null
  limitReachedKind: string | null
  fetchedAt: string | null
}

interface ProvidersState {
  providers: ProviderSummary[]
  status: 'idle' | 'loading' | 'ready' | 'error'
  /** Usage snapshot from auth/openai/usage; null until first push arrives. */
  chatGptUsage: ChatGptUsageSnapshot | null
}

interface ProvidersStore extends ProvidersState {
  reload(): Promise<void>
  reset(): void
  upsert(provider: ProviderSummary): void
  applyUsage(snapshot: ChatGptUsageSnapshot | null): void
  refreshUsage(): Promise<void>
}

const initialState: ProvidersState = {
  providers: [],
  status: 'idle',
  chatGptUsage: null
}

let inFlight: Promise<void> | null = null
let inFlightUsage: Promise<void> | null = null
let usageNotificationUnsubscribe: (() => void) | null = null

function parseUsage(raw: unknown): ChatGptUsageSnapshot | null {
  if (!raw || typeof raw !== 'object') return null
  const obj = raw as Record<string, unknown>
  if (obj.available === false) {
    return { available: false, planType: null, primary: null, secondary: null, credits: null, limitReachedKind: null, fetchedAt: null }
  }
  return {
    available: obj.available === true,
    planType: typeof obj.planType === 'string' ? obj.planType : null,
    primary: parseWindow(obj.primary),
    secondary: parseWindow(obj.secondary),
    credits: parseCredits(obj.credits),
    limitReachedKind: typeof obj.limitReachedKind === 'string' && obj.limitReachedKind.trim() !== ''
      ? obj.limitReachedKind
      : null,
    fetchedAt: typeof obj.fetchedAt === 'string' ? obj.fetchedAt : null
  }
}

function parseWindow(value: unknown): ChatGptUsageWindow | null {
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

function parseCredits(value: unknown): ChatGptUsageCredits | null {
  if (!value || typeof value !== 'object') return null
  const obj = value as Record<string, unknown>
  if (typeof obj.hasCredits !== 'boolean') return null
  return {
    hasCredits: obj.hasCredits,
    unlimited: typeof obj.unlimited === 'boolean' ? obj.unlimited : false,
    balance: typeof obj.balance === 'string' ? obj.balance : null
  }
}

export const useProvidersStore = create<ProvidersStore>((set, get) => ({
  ...initialState,
  reset() {
    inFlight = null
    inFlightUsage = null
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
              chatGptAccountId: typeof raw.chatGptAccountId === 'string' && raw.chatGptAccountId.trim() !== ''
                ? raw.chatGptAccountId
                : null,
              chatGptPlanType: typeof raw.chatGptPlanType === 'string' && raw.chatGptPlanType.trim() !== ''
                ? raw.chatGptPlanType
                : null
            }
          })
          .filter((p): p is ProviderSummary => p !== null)
        set({ providers: list, status: 'ready' })

        // Subscribe to usage notifications once we know the AppServer is available.
        ensureUsageSubscription()
        // Best-effort initial usage fetch — non-blocking.
        void get().refreshUsage()
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
  applyUsage(snapshot: ChatGptUsageSnapshot | null) {
    set({ chatGptUsage: snapshot })
  },
  async refreshUsage() {
    if (inFlightUsage) {
      await inFlightUsage
      return
    }
    if (typeof window === 'undefined' || !window.api?.appServer?.sendRequest) return
    const next = (async () => {
      try {
        const result = await window.api.appServer.sendRequest('auth/openai/usage', {}, 30_000)
        set({ chatGptUsage: parseUsage(result) })
      } catch {
        // Ignore — capability may be disabled, or no account signed in yet.
      }
    })()
    inFlightUsage = next
    try {
      await next
    } finally {
      inFlightUsage = null
    }
  }
}))

function ensureUsageSubscription(): void {
  if (usageNotificationUnsubscribe) return
  if (typeof window === 'undefined' || !window.api?.appServer?.onNotification) return
  usageNotificationUnsubscribe = window.api.appServer.onNotification((payload) => {
    if (payload?.method !== 'auth/openai/usageChanged') return
    useProvidersStore.getState().applyUsage(parseUsage(payload.params))
  })
}

/**
 * Selector: returns the auth info for the active provider when it is a ChatGPT OAuth provider.
 */
export function useChatGptOAuthSummary(providerId: string | null | undefined): ProviderSummary | null {
  return useProvidersStore((state) => {
    if (!providerId) return null
    const match = state.providers.find((p) => p.id.toLowerCase() === providerId.toLowerCase())
    return match && match.authMethod === 'chatgptOAuth' ? match : null
  })
}

/**
 * Selector: returns the latest ChatGPT usage snapshot, or null when unavailable.
 */
export function useChatGptUsage(): ChatGptUsageSnapshot | null {
  return useProvidersStore((state) => state.chatGptUsage)
}
