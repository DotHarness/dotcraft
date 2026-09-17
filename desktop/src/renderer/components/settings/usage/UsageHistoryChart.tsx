import { useMemo, type JSX } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { LoadState, UsageHistoryGroup, UsageHistoryWire, UsageRange } from '../../../stores/usageStore'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { Skeleton } from '../../ui/Skeleton'
import { SettingsGroup } from '../SettingsGroup'
import { SegmentedControl, type SegmentedOption } from '../ui/SegmentedControl'
import { buildChartPoints, buildChartSeries, type ChartPoint } from './chartSeries'
import {
  AXIS_TICK,
  AxisTick,
  CHART_HEIGHT,
  CHART_MARGIN,
  ChartEmpty,
  ChartError,
  ChartLegend,
  ChartTooltip,
  RangeToggle,
  SECTION_STYLE,
  axisTicks
} from './UsageChartParts'
import { usageKeyLabel, type TFn } from './usageLabels'
import styles from './usage.module.css'

interface UsageHistoryChartProps {
  state: LoadState<UsageHistoryWire>
  group: UsageHistoryGroup
  onGroupChange: (group: UsageHistoryGroup) => void
  range: UsageRange
  onRangeChange: (range: UsageRange) => void
  dayKeys: string[]
  formatDate: (date: string) => string
  pluginName: (pluginId: string) => string | null
  onRetry: () => void
  t: TFn
}

export function UsageHistoryChart({
  state,
  group,
  onGroupChange,
  range,
  onRangeChange,
  dayKeys,
  formatDate,
  pluginName,
  onRetry,
  t
}: UsageHistoryChartProps): JSX.Element {
  const series = useMemo(() => buildChartSeries(state.data), [state.data])
  const points = useMemo(() => buildChartPoints(state.data, dayKeys, series), [state.data, dayKeys, series])
  const total = useMemo(() => points.reduce((sum, point) => sum + point.total, 0), [points])
  const labelFor = (key: string): string => usageKeyLabel(t, group, key, pluginName)

  const options: SegmentedOption<UsageHistoryGroup>[] = [
    { value: 'surface', label: t('settings.usage.history.group.surface') },
    { value: 'model', label: t('settings.usage.history.group.model') },
    { value: 'tokenType', label: t('settings.usage.history.group.tokenType') }
  ]

  let body: JSX.Element
  if (state.error && !state.data) {
    body = <ChartError message={t('settings.usage.loadError')} retryLabel={t('settings.usage.retry')} onRetry={onRetry} />
  } else if (!state.data) {
    body = <Skeleton height={CHART_HEIGHT} radius={8} />
  } else if (total <= 0) {
    body = <ChartEmpty message={t('settings.usage.history.empty')} />
  } else {
    body = (
      <div className={styles.chart}>
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={points} margin={CHART_MARGIN} barCategoryGap="28%">
            <CartesianGrid vertical={false} stroke="var(--border-subtle)" />
            <XAxis
              dataKey="date"
              ticks={axisTicks(dayKeys)}
              interval={0}
              tickFormatter={formatDate}
              tick={AxisTick}
              axisLine={false}
              tickLine={false}
            />
            <YAxis
              tickCount={4}
              tickFormatter={formatCompactCount}
              tick={AXIS_TICK}
              axisLine={false}
              tickLine={false}
              width={44}
              allowDecimals={false}
            />
            <Tooltip
              cursor={{ fill: 'var(--bg-hover)' }}
              content={
                <ChartTooltip series={series} labelFor={labelFor} totalLabel={t('settings.usage.chart.total')} formatDate={formatDate} />
              }
            />
            {series.map((entry, index) => (
              <Bar
                key={entry.key}
                dataKey={(point: ChartPoint) => point.values[entry.key] ?? 0}
                name={entry.key}
                stackId="tokens"
                fill={entry.color}
                radius={index === series.length - 1 ? [4, 4, 0, 0] : 0}
                isAnimationActive={false}
              />
            ))}
          </BarChart>
        </ResponsiveContainer>
      </div>
    )
  }

  return (
    <SettingsGroup
      title={t('settings.usage.history.title')}
      description={t('settings.usage.history.hint')}
      headerAction={<RangeToggle value={range} onChange={onRangeChange} t={t} />}
      framed={false}
      style={SECTION_STYLE}
    >
      <div className={`${styles.frame} ${styles.card}`}>
        <div className={styles.cardHeader}>
          <span className={styles.cardTitle}>{t('settings.usage.stat.totalTokens')}</span>
          <SegmentedControl value={group} options={options} onChange={onGroupChange} ariaLabel={t('settings.usage.history.groupLabel')} />
        </div>
        {body}
        {state.data && total > 0 && <ChartLegend series={series} labelFor={labelFor} />}
      </div>
    </SettingsGroup>
  )
}
