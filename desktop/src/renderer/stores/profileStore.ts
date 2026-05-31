import { create } from 'zustand'

/** One day of token usage. Matches AppServer UsageTimeseriesDay wire DTO (spec §27A.3). */
export interface UsageDayWire {
  date: string
  inputTokens: number
  outputTokens: number
  totalTokens: number
  sessionCount: number
}

/** Matches AppServer UsageTimeseriesResult wire DTO (spec §27A.3). */
interface UsageTimeseriesWire {
  tzOffsetMinutes: number
  longestTaskMs: number
  days: UsageDayWire[]
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
  /** Longest single task (turn) duration across the workspace, in milliseconds. */
  longestTaskMs: number
  loading: boolean
  /** True after at least one successful fetch; avoids skeleton flash on tab revisit. */
  loadedOnce: boolean
  error: string | null

  githubUsername: string | null
  githubProfile: GitHubProfile | null
  identityLoaded: boolean

  fetchTimeseries(options?: { silent?: boolean }): Promise<void>
  loadIdentity(): Promise<void>
  setGithubUsername(username: string | null): Promise<void>
  reset(): void
}

/** Minutes to add to UTC to obtain local time, i.e. -getTimezoneOffset(). */
function localTzOffsetMinutes(): number {
  return -new Date().getTimezoneOffset()
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
  longestTaskMs: 0,
  loading: false,
  loadedOnce: false,
  error: null,

  githubUsername: null,
  githubProfile: null,
  identityLoaded: false,

  async fetchTimeseries(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent && !get().loadedOnce) set({ loading: true, error: null })
    else set({ error: null })
    try {
      const result = (await window.api.appServer.sendRequest('usage/timeseries', {
        tzOffsetMinutes: localTzOffsetMinutes()
      })) as UsageTimeseriesWire
      const days = Array.isArray(result?.days) ? result.days : []
      const longestTaskMs = typeof result?.longestTaskMs === 'number' ? result.longestTaskMs : 0
      set({ days, longestTaskMs, loading: false, loadedOnce: true })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
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
    set({ days: [], longestTaskMs: 0, loading: false, loadedOnce: false, error: null })
  }
}))
