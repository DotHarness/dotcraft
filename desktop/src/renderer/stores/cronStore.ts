import { create } from 'zustand'

/** Matches AppServer CronJobInfo wire DTO (spec §16.2). */
export interface CronJobWire {
  id: string
  name: string
  schedule: {
    kind: string
    everyMs?: number | null
    atMs?: number | null
    initialDelayMs?: number | null
    dailyHour?: number | null
    dailyMinute?: number | null
    tz?: string | null
  }
  enabled: boolean
  createdAtMs: number
  deleteAfterRun: boolean
  state: {
    nextRunAtMs?: number | null
    lastRunAtMs?: number | null
    lastStatus?: string | null
    lastError?: string | null
    lastThreadId?: string | null
    lastResult?: string | null
  }
}

const POLL_MS = 15_000
let pollTimer: ReturnType<typeof setInterval> | null = null
let isPollingActive = false

// Register cleanup on page unload to prevent timer leaks
if (typeof window !== 'undefined') {
  window.addEventListener('beforeunload', () => {
    if (pollTimer != null) {
      clearInterval(pollTimer)
      pollTimer = null
      isPollingActive = false
    }
  })
  
  // Also pause polling when page is hidden (tab switch, minimize, etc.)
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden' && pollTimer != null) {
      clearInterval(pollTimer)
      pollTimer = null
    } else if (document.visibilityState === 'visible' && isPollingActive) {
      // Resume polling when page becomes visible again
      if (pollTimer == null) {
        pollTimer = setInterval(() => {
          void useCronStore.getState().fetchJobs({ silent: true })
        }, POLL_MS)
      }
    }
  })
}

interface CronStoreState {
  jobs: CronJobWire[]
  loading: boolean
  /** True after at least one successful `fetchJobs`; used to avoid skeleton flash on tab revisit. */
  listLoadedOnce: boolean
  error: string | null
  selectedCronJobId: string | null

  fetchJobs(options?: { silent?: boolean }): Promise<void>
  startPolling(): void
  stopPolling(): void
  removeJob(jobId: string): Promise<void>
  enableJob(jobId: string, enabled: boolean): Promise<void>
  runJobNow(jobId: string): Promise<void>
  selectCronJob(jobId: string | null): void
  upsertJob(job: CronJobWire): void
  removeJobLocal(jobId: string): void
  reset(): void
}

export const useCronStore = create<CronStoreState>((set, get) => ({
  jobs: [],
  loading: false,
  listLoadedOnce: false,
  error: null,
  selectedCronJobId: null,

  async fetchJobs(options?: { silent?: boolean }) {
    const silent = options?.silent === true
    if (!silent) {
      if (!get().listLoadedOnce) set({ loading: true, error: null })
      else set({ error: null })
    }
    try {
      const result = (await window.api.appServer.sendRequest('cron/list', {
        includeDisabled: true
      })) as { jobs?: CronJobWire[] }
      set({ jobs: result.jobs ?? [], loading: false, listLoadedOnce: true })
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : String(e)
      if (!silent) set({ error: msg, loading: false })
      else set({ loading: false })
    }
  },

  startPolling() {
    if (pollTimer != null) return
    isPollingActive = true
    pollTimer = setInterval(() => {
      void useCronStore.getState().fetchJobs({ silent: true })
    }, POLL_MS)
  },

  stopPolling() {
    if (pollTimer != null) {
      clearInterval(pollTimer)
      pollTimer = null
    }
    isPollingActive = false
  },

  async removeJob(jobId: string) {
    await window.api.appServer.sendRequest('cron/remove', { jobId })
    get().removeJobLocal(jobId)
    if (get().selectedCronJobId === jobId) {
      set({ selectedCronJobId: null })
    }
  },

  async enableJob(jobId: string, enabled: boolean) {
    await window.api.appServer.sendRequest('cron/enable', { jobId, enabled })
    await get().fetchJobs({ silent: true })
  },

  async runJobNow(jobId: string) {
    await window.api.appServer.sendRequest('cron/run', { jobId })
    await get().fetchJobs({ silent: true })
  },

  selectCronJob(jobId: string | null) {
    set({ selectedCronJobId: jobId })
  },

  upsertJob(job: CronJobWire) {
    set((state) => {
      const idx = state.jobs.findIndex((j) => j.id === job.id)
      if (idx >= 0) {
        const next = [...state.jobs]
        next[idx] = { ...next[idx], ...job }
        return { jobs: next }
      }
      return { jobs: [job, ...state.jobs] }
    })
  },

  removeJobLocal(jobId: string) {
    set((state) => ({
      jobs: state.jobs.filter((j) => j.id !== jobId)
    }))
  },

  reset() {
    get().stopPolling()
    set({
      jobs: [],
      loading: false,
      listLoadedOnce: false,
      error: null,
      selectedCronJobId: null
    })
  }
}))
