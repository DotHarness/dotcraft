import { describe, expect, it } from 'vitest'
import { buildChartPoints, buildChartSeries } from '../components/settings/usage/chartSeries'
import { usageWindow, type UsageHistoryWire } from '../stores/usageStore'

const byModel: UsageHistoryWire = {
  unit: 'tokens',
  groupBy: 'model',
  days: [
    { date: '2026-09-15', total: 100, values: [{ key: 'a', value: 60 }, { key: 'b', value: 25 }, { key: 'c', value: 10 }, { key: 'd', value: 5 }] },
    { date: '2026-09-17', total: 30, values: [{ key: 'd', value: 20 }, { key: 'a', value: 10 }] }
  ],
  series: [
    { key: 'a', total: 70 },
    { key: 'b', total: 25 },
    { key: 'd', total: 25 },
    { key: 'c', total: 10 }
  ]
}

describe('usage chart series', () => {
  it('keeps the leading series and folds the rest into other with fixed colors', () => {
    const series = buildChartSeries(byModel, 2)
    expect(series.map((entry) => [entry.key, entry.total])).toEqual([
      ['a', 70],
      ['b', 25],
      ['other', 35]
    ])
    expect(series.map((entry) => entry.color)).toEqual([
      'var(--chart-series-1)',
      'var(--chart-series-2)',
      'var(--chart-series-other)'
    ])
  })

  it('zero-fills missing days and routes folded keys into other', () => {
    const series = buildChartSeries(byModel, 2)
    const points = buildChartPoints(byModel, ['2026-09-15', '2026-09-16', '2026-09-17'], series)
    expect(points.map((point) => point.total)).toEqual([100, 0, 30])
    expect(points[0].values).toEqual({ a: 60, b: 25, other: 15 })
    expect(points[1].values).toEqual({ a: 0, b: 0, other: 0 })
    expect(points[2].values).toEqual({ a: 10, b: 0, other: 20 })
  })

  it('never folds token types and keeps their slot order', () => {
    const series = buildChartSeries({
      unit: 'tokens',
      groupBy: 'tokenType',
      days: [],
      series: [
        { key: 'output', total: 5 },
        { key: 'cached', total: 50 },
        { key: 'uncached', total: 20 }
      ]
    }, 1)
    expect(series.map((entry) => entry.key)).toEqual(['uncached', 'cached', 'output'])
    expect(series[2].color).toBe('var(--chart-series-4)')
  })

  it('spans the requested number of local days ending today', () => {
    const now = new Date(2026, 8, 17, 12)
    expect(usageWindow('7d', now)).toMatchObject({ from: '2026-09-11', to: '2026-09-17' })
    expect(usageWindow('30d', now)).toMatchObject({ from: '2026-08-19', to: '2026-09-17' })
  })
})
