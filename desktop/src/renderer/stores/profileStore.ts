import { create } from 'zustand'
import { addLocalDays, localDayKey, localTzOffsetMinutes } from '../utils/localDay'
import { requestUsage } from './usageConnectionStore'

export interface UsageDayWire {
  date: string
  totalTokens: number
}

interface UsageHistoryWire {
  unit: string
  groupBy: string
  days: Array<{ date: string; total: number }>
}

/** The contribution grid spans 53 weeks, so the history request is bounded to that window. */
const HEATMAP_LOOKBACK_DAYS = 371

export interface RankedMetricWire {
  key: string
  count: number
  total: number
}

export interface SkillUsageWire {
  name: string
  count: number
  pluginId?: string | null
}

export interface ProfileInsightsWire {
  lifetimeTokens: number
  peakDailyTokens: number
  currentStreakDays: number
  longestStreakDays: number
  topModel: RankedMetricWire | null
  topReasoning: RankedMetricWire | null
  skillsExplored: number
  totalSkillsUsed: number
  totalThreads: number
  longestTaskMs: number
  skills: SkillUsageWire[]
}

export interface GitHubProfile {
  login: string
  name: string | null
  /** Avatar as a `data:` URL (resolved + cached in the main process), or null. */
  avatarUrl: string | null
}

interface ProfileStoreState {
  days: UsageDayWire[]
  loading: boolean
  /** True after at least one successful fetch; avoids skeleton flash on tab revisit. */
  loadedOnce: boolean
  error: string | null

  insights: ProfileInsightsWire | null
  insightsLoading: boolean
  insightsLoadedOnce: boolean
  insightsError: string | null

  githubUsername: string | null
  githubProfile: GitHubProfile | null
  identityLoaded: boolean

  load(): Promise<void>
  fetchHistory(options?: { silent?: boolean }): Promise<void>
  fetchInsights(options?: { silent?: boolean }): Promise<void>
  loadIdentity(): Promise<void>
  setGithubUsername(username: string | null): Promise<void>
  reset(): void
}

async function fetchGithubProfile(login: string): Promise<GitHubProfile | null> {
  try {
    const result = await window.api.profile.getGithubIdentity(login)
    if (!result) return null
    return { login: result.login, name: result.name, avatarUrl: result.avatarDataUrl }
  } catch {
    return null
  }
}

let historyGeneration = 0
let insightsGeneration = 0

export const useProfileStore = create<ProfileStoreState>((set, get) => ({
  days: [],
  loading: false,
  loadedOnce: false,
  error: null,

  insights: null,
  insightsLoading: false,
  insightsLoadedOnce: false,
  insightsError: null,

  githubUsername: null,
  githubProfile: null,
  identityLoaded: false,

  async load() {
    await Promise.all([get().fetchHistory({ silent: true }), get().fetchInsights({ silent: true })])
  },

  async fetchHistory(options?: { silent?: boolean }) {
    const generation = ++historyGeneration
    const silent = options?.silent === true
    if (!get().loadedOnce) set({ loading: true, error: null })
    else set({ error: null })
    try {
      const from = addLocalDays(new Date(), -HEATMAP_LOOKBACK_DAYS)
      const result = await requestUsage<UsageHistoryWire>('usage/history', {
        metric: 'tokens',
        from: localDayKey(from),
        tzOffsetMinutes: localTzOffsetMinutes()
      })
      if (generation !== historyGeneration) return
      const days = result.days.map((day) => ({ date: day.date, totalTokens: day.total }))
      set({ days, loading: false, loadedOnce: true })
    } catch (e: unknown) {
      if (generation !== historyGeneration) return
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent || !get().loadedOnce) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  async fetchInsights(options?: { silent?: boolean }) {
    const generation = ++insightsGeneration
    const silent = options?.silent === true
    if (!get().insightsLoadedOnce) set({ insightsLoading: true, insightsError: null })
    else set({ insightsError: null })
    try {
      const result = await requestUsage<ProfileInsightsWire>('profile/insights', {
        topSkills: 5,
        tzOffsetMinutes: localTzOffsetMinutes()
      })
      if (generation !== insightsGeneration) return
      set({
        insights: { ...result, topModel: result.topModel ?? null, topReasoning: result.topReasoning ?? null },
        insightsLoading: false,
        insightsLoadedOnce: true
      })
    } catch (e: unknown) {
      if (generation !== insightsGeneration) return
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent || !get().insightsLoadedOnce) set({ insightsError: msg, insightsLoading: false })
      else set({ insightsLoading: false })
    }
  },

  async loadIdentity() {
    try {
      const settings = await window.api.settings.get()
      const username = settings.profile?.githubUsername?.trim() || null
      set({ githubUsername: username, identityLoaded: true })
      if (username) {
        const profile = await fetchGithubProfile(username)
        if (get().githubUsername === username) set({ githubProfile: profile })
      } else {
        set({ githubProfile: null })
      }
    } catch {
      set({ identityLoaded: true })
    }
  },

  async setGithubUsername(username: string | null) {
    const normalized = username?.trim() || null
    set({ githubUsername: normalized, githubProfile: null })
    await window.api.settings.set({ profile: { githubUsername: normalized ?? '' } })
    if (normalized) {
      const profile = await fetchGithubProfile(normalized)
      if (get().githubUsername === normalized) set({ githubProfile: profile })
    }
  },

  reset() {
    historyGeneration += 1
    insightsGeneration += 1
    set({
      days: [],
      loading: false,
      loadedOnce: false,
      error: null,
      insights: null,
      insightsLoading: false,
      insightsLoadedOnce: false,
      insightsError: null
    })
  }
}))
