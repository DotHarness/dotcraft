import { useMemo, type JSX } from 'react'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { LoadState, UsageHistoryWire, UsageRange } from '../../../stores/usageStore'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { Skeleton } from '../../ui/Skeleton'
import { SettingsGroup } from '../SettingsGroup'
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
import { usageKeyLabel, type TFn, type UsageDimension } from './usageLabels'
import styles from './usage.module.css'

interface ActivityCardProps {
  title: string
  state: LoadState<UsageHistoryWire>
  dimension: UsageDimension
  dayKeys: string[]
  formatDate: (date: string) => string
  pluginName: (pluginId: string) => string | null
  onRetry: () => void
  t: TFn
}

function ActivityCard({ title, state, dimension, dayKeys, formatDate, pluginName, onRetry, t }: ActivityCardProps): JSX.Element {
  const series = useMemo(() => buildChartSeries(state.data), [state.data])
  const points = useMemo(() => buildChartPoints(state.data, dayKeys, series), [state.data, dayKeys, series])
  const total = useMemo(() => points.reduce((sum, point) => sum + point.total, 0), [points])
  const labelFor = (key: string): string => usageKeyLabel(t, dimension, key, pluginName)

  let body: JSX.Element
  if (state.error && !state.data) {
    body = <ChartError message={t('settings.usage.loadError')} retryLabel={t('settings.usage.retry')} onRetry={onRetry} />
  } else if (!state.data) {
    body = <Skeleton height={CHART_HEIGHT} radius={8} />
  } else if (total <= 0) {
    body = <ChartEmpty message={t('settings.usage.activity.empty')} />
  } else {
    body = (
      <div className={styles.chart}>
        <ResponsiveContainer width="100%" height="100%">
          <LineChart data={points} margin={CHART_MARGIN}>
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
              cursor={{ stroke: 'var(--border-active)' }}
              content={
                <ChartTooltip series={series} labelFor={labelFor} totalLabel={t('settings.usage.chart.total')} formatDate={formatDate} />
              }
            />
            {series.map((entry) => (
              <Line
                key={entry.key}
                type="monotone"
                dataKey={(point: ChartPoint) => point.values[entry.key] ?? 0}
                name={entry.key}
                stroke={entry.color}
                strokeWidth={2}
                dot={false}
                activeDot={{ r: 4, strokeWidth: 0 }}
                isAnimationActive={false}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>
    )
  }

  return (
    <div className={`${styles.frame} ${styles.card}`}>
      <div className={styles.hero}>
        <span className={styles.heroLabel}>{title}</span>
        <span className={styles.heroValue}>{state.data ? formatCompactCount(total) : '—'}</span>
      </div>
      {body}
      {state.data && total > 0 && <ChartLegend series={series} labelFor={labelFor} />}
    </div>
  )
}

interface ToolActivityChartsProps {
  toolCalls: LoadState<UsageHistoryWire>
  skillUses: LoadState<UsageHistoryWire>
  range: UsageRange
  onRangeChange: (range: UsageRange) => void
  dayKeys: string[]
  formatDate: (date: string) => string
  pluginName: (pluginId: string) => string | null
  onRetry: () => void
  t: TFn
}

export function ToolActivityCharts({
  toolCalls,
  skillUses,
  range,
  onRangeChange,
  dayKeys,
  formatDate,
  pluginName,
  onRetry,
  t
}: ToolActivityChartsProps): JSX.Element {
  const shared = { dayKeys, formatDate, pluginName, onRetry, t }
  return (
    <SettingsGroup
      title={t('settings.usage.activity.title')}
      description={t('settings.usage.activity.hint')}
      headerAction={<RangeToggle value={range} onChange={onRangeChange} t={t} />}
      framed={false}
      style={SECTION_STYLE}
    >
      <div className={styles.stack}>
        <ActivityCard title={t('settings.usage.activity.toolCalls')} state={toolCalls} dimension="toolSource" {...shared} />
        <ActivityCard title={t('settings.usage.activity.skills')} state={skillUses} dimension="skill" {...shared} />
      </div>
    </SettingsGroup>
  )
}
