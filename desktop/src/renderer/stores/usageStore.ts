import { create } from 'zustand'
import { addLocalDays, localDayKey, localTzOffsetMinutes } from '../utils/localDay'
import { requestUsage } from './usageConnectionStore'

export type UsageRange = '7d' | '30d'
export type UsageHistoryGroup = 'surface' | 'model' | 'tokenType'
export type UsageHistoryMetric = 'tokens' | 'cost'
export type UsageSection = 'history' | 'activity' | 'threads'

export const USAGE_RANGE_DAYS: Record<UsageRange, number> = { '7d': 7, '30d': 30 }

export interface UsageHistoryValueWire {
  key: string
  value: number
}

export interface UsageHistoryDayWire {
  date: string
  total: number
  values: UsageHistoryValueWire[]
}

export interface UsageHistorySeriesWire {
  key: string
  total: number
}

export interface UsageHistoryWire {
  unit: 'tokens' | 'usd' | 'count'
  groupBy: string
  days: UsageHistoryDayWire[]
  series: UsageHistorySeriesWire[]
}

export interface UsageThreadWire {
  workspacePath: string
  threadId: string
  title?: string | null
  originChannel: string
  lastActiveAt: string
  turns: number
  totalTokens: number
  inputTokens: number
  outputTokens: number
  cachedInputTokens: number
  cacheHitRate: number
  /** Omitted when none of the thread's turns could be priced. */
  costUsd?: number | null
  archived: boolean
}

export interface UsageCostSummaryWire {
  totalCostUsd: number
  unpricedTurns: number
}

export interface LoadState<T> {
  data: T | null
  loading: boolean
  error: string | null
}

export interface UsageWindow {
  from: string
  to: string
  tzOffsetMinutes: number
}

const TOP_THREADS_LIMIT = 20

function idle<T>(): LoadState<T> {
  return { data: null, loading: false, error: null }
}

export function usageWindow(range: UsageRange, now = new Date()): UsageWindow {
  return {
    from: localDayKey(addLocalDays(now, -(USAGE_RANGE_DAYS[range] - 1))),
    to: localDayKey(now),
    tzOffsetMinutes: localTzOffsetMinutes()
  }
}

interface UsageStoreState {
  ranges: Record<UsageSection, UsageRange>
  historyGroup: UsageHistoryGroup
  historyMetric: UsageHistoryMetric
  history: LoadState<UsageHistoryWire>
  costSummary: LoadState<UsageCostSummaryWire>
  toolCalls: LoadState<UsageHistoryWire>
  skillUses: LoadState<UsageHistoryWire>
  threads: LoadState<UsageThreadWire[]>

  setRange(section: UsageSection, range: UsageRange): void
  setHistoryGroup(group: UsageHistoryGroup): void
  setHistoryMetric(metric: UsageHistoryMetric): void
  load(section?: UsageSection): Promise<void>
  reset(): void
}

type SectionKey = 'history' | 'costSummary' | 'toolCalls' | 'skillUses' | 'threads'

function initialState(): Pick<UsageStoreState, 'ranges' | 'historyGroup' | 'historyMetric' | SectionKey> {
  return {
    ranges: { history: '7d', activity: '7d', threads: '7d' },
    historyGroup: 'surface',
    historyMetric: 'tokens',
    history: idle(),
    costSummary: idle(),
    toolCalls: idle(),
    skillUses: idle(),
    threads: idle()
  }
}

export const useUsageStore = create<UsageStoreState>((set, get) => {
  const generation: Record<UsageSection, number> = { history: 0, activity: 0, threads: 0 }

  function track<T>(section: UsageSection, key: SectionKey, run: () => Promise<T>): Promise<void> {
    const token = generation[section]
    set((state) => ({ [key]: { ...state[key], loading: true, error: null } }))
    return run().then(
      (data) => {
        if (token === generation[section]) set({ [key]: { data, loading: false, error: null } })
      },
      (e: unknown) => {
        if (token !== generation[section]) return
        const error = e instanceof Error ? e.message : String(e)
        set((state) => ({ [key]: { data: state[key].data, loading: false, error } }))
      }
    )
  }

  function loadSection(section: UsageSection): Promise<void> {
    generation[section] += 1
    const { ranges, historyGroup, historyMetric } = get()
    const window = usageWindow(ranges[section])
    switch (section) {
      case 'history':
        return Promise.all([
          track('history', 'history', () =>
            requestUsage<UsageHistoryWire>('usage/history', { metric: historyMetric, groupBy: historyGroup, ...window })
          ),
          track('history', 'costSummary', async () => {
            const { totalCostUsd, unpricedTurns } = await requestUsage<UsageCostSummaryWire>('usage/summary', window)
            return { totalCostUsd, unpricedTurns }
          })
        ]).then(() => undefined)
      case 'activity':
        return Promise.all([
          track('activity', 'toolCalls', () =>
            requestUsage<UsageHistoryWire>('usage/history', { metric: 'toolCalls', groupBy: 'toolSource', ...window })
          ),
          track('activity', 'skillUses', () =>
            requestUsage<UsageHistoryWire>('usage/history', { metric: 'skillUses', groupBy: 'skill', ...window })
          )
        ]).then(() => undefined)
      case 'threads':
        return track('threads', 'threads', async () => {
          const result = await requestUsage<{ threads: UsageThreadWire[] }>('usage/threads', { ...window, limit: TOP_THREADS_LIMIT })
          return result.threads
        })
    }
  }

  return {
    ...initialState(),

    setRange(section, range) {
      if (get().ranges[section] === range) return
      set((state) => ({ ranges: { ...state.ranges, [section]: range } }))
      void loadSection(section)
    },

    setHistoryGroup(group) {
      if (get().historyGroup === group) return
      set({ historyGroup: group })
      void loadSection('history')
    },

    setHistoryMetric(metric) {
      const state = get()
      if (state.historyMetric === metric) return
      const historyGroup = metric === 'cost' && state.historyGroup === 'tokenType' ? 'model' : state.historyGroup
      set({ historyMetric: metric, historyGroup })
      void loadSection('history')
    },

    async load(section) {
      if (section) return loadSection(section)
      await Promise.all([loadSection('history'), loadSection('activity'), loadSection('threads')])
    },

    reset() {
      for (const section of Object.keys(generation) as UsageSection[]) generation[section] += 1
      set(initialState())
    }
  }
})
