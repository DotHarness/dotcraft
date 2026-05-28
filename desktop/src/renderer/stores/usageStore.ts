import { create } from 'zustand'

/** Matches AppServer UsageSummaryResult wire DTO (spec §27A.2). */
export interface UsageSummaryWire {
  sessionCount: number
  totalRequests: number
  totalResponses: number
  totalToolCalls: number
  totalErrors: number
  totalContextCompactions: number
  totalInputTokens: number
  totalOutputTokens: number
  totalCachedInputTokens: number
  totalCacheWriteInputTokens: number
  totalFreshInputTokens: number
  totalNonCachedInputTokens: number
  totalReasoningOutputTokens: number
  totalToolDurationMs: number
  avgToolDurationMs: number
  maxToolDurationMs: number
  cacheHitRate: number
  totalTokens: number
}

interface UsageStoreState {
  summary: UsageSummaryWire | null
  loading: boolean
  /** True after at least one successful fetch; used to avoid skeleton flash on tab revisit. */
  loadedOnce: boolean
  error: string | null

  fetchSummary(options?: { silent?: boolean }): Promise<void>
  reset(): void
}

export const useUsageStore = create<UsageStoreState>((set, get) => ({
  summary: null,
  loading: false,
  loadedOnce: false,
  error: null,

  async fetchSummary(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent && !get().loadedOnce) set({ loading: true, error: null })
    else set({ error: null })
    try {
      const result = (await window.api.appServer.sendRequest('usage/summary', {})) as UsageSummaryWire
      set({ summary: result, loading: false, loadedOnce: true })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  reset() {
    set({ summary: null, loading: false, loadedOnce: false, error: null })
  }
}))
