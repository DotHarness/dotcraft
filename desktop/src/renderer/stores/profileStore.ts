import { create } from 'zustand'
import { addLocalDays, localDayKey, localTzOffsetMinutes } from '../utils/localDay'

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

/** A leading value with its count out of a total, for share% display. Matches RankedMetric. */
export interface RankedMetricWire {
  key: string
  count: number
  total: number
}

/** One referenced skill with run count and optional plugin attribution. Matches SkillUsageWire. */
export interface SkillUsageWire {
  name: string
  count: number
  pluginId?: string | null
}

export interface ProfileInsightsWire {
  topModel: RankedMetricWire | null
  topReasoning: RankedMetricWire | null
  skillsExplored: number
  totalSkillsUsed: number
  totalThreads: number
  /** Longest single task (turn) duration across the workspace, in milliseconds. */
  longestTaskMs: number
  skills: SkillUsageWire[]
}

/** Public GitHub profile fields used by the Profile header. */
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

  fetchHistory(options?: { silent?: boolean }): Promise<void>
  fetchInsights(options?: { silent?: boolean }): Promise<void>
  loadIdentity(): Promise<void>
  setGithubUsername(username: string | null): Promise<void>
  reset(): void
}

/**
 * Resolves a public GitHub profile via the main process, which fetches and caches
 * it locally. The main process is not subject to the renderer CSP, so this works in
 * packaged builds (where the renderer cannot reach github.com directly). Returns null
 * when the username is invalid or unavailable.
 */
async function fetchGithubProfile(login: string): Promise<GitHubProfile | null> {
  try {
    const result = await window.api.profile.getGithubIdentity(login)
    if (!result) return null
    return { login: result.login, name: result.name, avatarUrl: result.avatarDataUrl }
  } catch {
    return null
  }
}

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

  async fetchHistory(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent && !get().loadedOnce) set({ loading: true, error: null })
    else set({ error: null })
    try {
      const from = addLocalDays(new Date(), -HEATMAP_LOOKBACK_DAYS)
      const result = (await window.api.appServer.sendRequest('usage/history', {
        metric: 'tokens',
        from: localDayKey(from),
        tzOffsetMinutes: localTzOffsetMinutes()
      })) as UsageHistoryWire
      const days: UsageDayWire[] = Array.isArray(result?.days)
        ? result.days.map((day) => ({ date: day.date, totalTokens: day.total }))
        : []
      set({ days, loading: false, loadedOnce: true })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  async fetchInsights(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent && !get().insightsLoadedOnce) set({ insightsLoading: true, insightsError: null })
    else set({ insightsError: null })
    try {
      const result = (await window.api.appServer.sendRequest('profile/insights', {
        topSkills: 5
      })) as ProfileInsightsWire
      set({
        insights: {
          topModel: result?.topModel ?? null,
          topReasoning: result?.topReasoning ?? null,
          skillsExplored: typeof result?.skillsExplored === 'number' ? result.skillsExplored : 0,
          totalSkillsUsed: typeof result?.totalSkillsUsed === 'number' ? result.totalSkillsUsed : 0,
          totalThreads: typeof result?.totalThreads === 'number' ? result.totalThreads : 0,
          longestTaskMs: typeof result?.longestTaskMs === 'number' ? result.longestTaskMs : 0,
          skills: Array.isArray(result?.skills) ? result.skills : []
        },
        insightsLoading: false,
        insightsLoadedOnce: true
      })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ insightsError: msg, insightsLoading: false })
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
        // Guard against a username change racing this fetch.
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
