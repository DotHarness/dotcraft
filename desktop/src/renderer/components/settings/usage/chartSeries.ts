import type { UsageHistoryWire } from '../../../stores/usageStore'

export const OTHER_KEY = 'other'
export const UNKNOWN_KEY = 'unknown'
export const CHART_SERIES_LIMIT = 3
export const TOKEN_TYPE_ORDER = ['uncached', 'cached', 'cacheWrite', 'output'] as const

export interface ChartSeries {
  key: string
  total: number
  color: string
}

export interface ChartPoint {
  date: string
  total: number
  values: Record<string, number>
}

function seriesColor(index: number): string {
  return `var(--chart-series-${Math.min(index + 1, 4)})`
}

/** Token types keep their fixed slots; every other dimension keeps the top series and folds the rest. */
export function buildChartSeries(history: UsageHistoryWire | null, limit = CHART_SERIES_LIMIT): ChartSeries[] {
  if (!history) return []
  if (history.groupBy === 'tokenType') {
    const totals = new Map(history.series.map((series) => [series.key, series.total]))
    return TOKEN_TYPE_ORDER
      .map((key, index) => ({ key, total: totals.get(key) ?? 0, color: seriesColor(index) }))
      .filter((series) => series.total > 0)
  }

  const ranked = history.series.filter((series) => series.key !== OTHER_KEY)
  const kept = ranked.slice(0, limit).map((series, index) => ({
    key: series.key,
    total: series.total,
    color: seriesColor(index)
  }))
  const folded =
    ranked.slice(limit).reduce((sum, series) => sum + series.total, 0) +
    (history.series.find((series) => series.key === OTHER_KEY)?.total ?? 0)
  if (folded > 0) kept.push({ key: OTHER_KEY, total: folded, color: 'var(--chart-series-other)' })
  return kept
}

/** One point per requested day so gaps render as zero instead of vanishing from the axis. */
export function buildChartPoints(
  history: UsageHistoryWire | null,
  dayKeys: string[],
  series: ChartSeries[]
): ChartPoint[] {
  const kept = new Set(series.map((entry) => entry.key))
  const byDate = new Map((history?.days ?? []).map((day) => [day.date, day]))
  return dayKeys.map((date) => {
    const day = byDate.get(date)
    const values: Record<string, number> = {}
    for (const entry of series) values[entry.key] = 0
    for (const value of day?.values ?? []) {
      const bucket = kept.has(value.key) ? value.key : OTHER_KEY
      if (bucket in values) values[bucket] += value.value
    }
    return { date, total: day?.total ?? 0, values }
  })
}
