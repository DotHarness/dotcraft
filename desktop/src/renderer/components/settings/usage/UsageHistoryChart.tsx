import { useMemo, type JSX } from 'react'
import { Bar, BarChart, CartesianGrid, Tooltip, XAxis, YAxis } from 'recharts'
import { useLocale } from '../../../contexts/LocaleContext'
import type {
  LoadState,
  UsageCostSummaryWire,
  UsageHistoryGroup,
  UsageHistoryMetric,
  UsageHistoryWire,
  UsageRange
} from '../../../stores/usageStore'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { formatCompactUsd, formatUsd } from '../../../utils/formatUsd'
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
  ChartFrame,
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
  costSummary: LoadState<UsageCostSummaryWire>
  metric: UsageHistoryMetric
  onMetricChange: (metric: UsageHistoryMetric) => void
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
  costSummary,
  metric,
  onMetricChange,
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
  const locale = useLocale()
  const cost = metric === 'cost'
  const series = useMemo(() => buildChartSeries(state.data), [state.data])
  const points = useMemo(() => buildChartPoints(state.data, dayKeys, series), [state.data, dayKeys, series])
  const total = useMemo(() => points.reduce((sum, point) => sum + point.total, 0), [points])
  const labelFor = (key: string): string => usageKeyLabel(t, group, key, pluginName)
  const formatValue = cost ? (value: number) => formatUsd(value, locale) : (value: number) => value.toLocaleString(locale)
  const formatAxis = cost ? (value: number) => formatCompactUsd(value, locale) : formatCompactCount
  const unpricedTurns = costSummary.data?.unpricedTurns ?? 0
  // A range where nothing could be priced has no cost, which is not the same as costing nothing.
  const heroValue = !state.data
    ? '—'
    : cost
      ? total <= 0 && unpricedTurns > 0 ? '—' : formatUsd(total, locale)
      : formatCompactCount(total)

  const metricOptions: SegmentedOption<UsageHistoryMetric>[] = [
    { value: 'tokens', label: t('settings.usage.history.metric.tokens') },
    { value: 'cost', label: t('settings.usage.history.metric.cost') }
  ]
  const groupOptions: SegmentedOption<UsageHistoryGroup>[] = [
    { value: 'surface', label: t('settings.usage.history.group.surface') },
    { value: 'model', label: t('settings.usage.history.group.model') },
    { value: 'tokenType', label: t('settings.usage.history.group.tokenType'), disabled: cost }
  ]

  let body: JSX.Element
  if (state.error && !state.data) {
    body = <ChartError message={t('settings.usage.loadError')} retryLabel={t('settings.usage.retry')} onRetry={onRetry} />
  } else if (!state.data) {
    body = <Skeleton height={CHART_HEIGHT} radius={8} />
  } else if (total <= 0) {
    body = <ChartEmpty message={cost ? t('settings.usage.history.emptyCost') : t('settings.usage.history.empty')} />
  } else {
    body = (
      <ChartFrame>
        {(width) => (
          <BarChart width={width} height={CHART_HEIGHT} data={points} margin={CHART_MARGIN} barCategoryGap="28%">
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
              tickFormatter={formatAxis}
              tick={AXIS_TICK}
              axisLine={false}
              tickLine={false}
              width={56}
              allowDecimals={cost}
            />
            <Tooltip
              isAnimationActive={false}
              cursor={{ fill: 'var(--bg-hover)' }}
              content={
                <ChartTooltip
                  series={series}
                  labelFor={labelFor}
                  totalLabel={t('settings.usage.chart.total')}
                  formatDate={formatDate}
                  formatValue={formatValue}
                />
              }
            />
            {series.map((entry, index) => (
              <Bar
                key={entry.key}
                dataKey={(point: ChartPoint) => point.values[entry.key] ?? 0}
                name={entry.key}
                stackId="usage"
                fill={entry.color}
                radius={index === series.length - 1 ? [4, 4, 0, 0] : 0}
                isAnimationActive={false}
              />
            ))}
          </BarChart>
        )}
      </ChartFrame>
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
          <SegmentedControl value={metric} options={metricOptions} onChange={onMetricChange} ariaLabel={t('settings.usage.history.metricLabel')} />
          <SegmentedControl value={group} options={groupOptions} onChange={onGroupChange} ariaLabel={t('settings.usage.history.groupLabel')} />
        </div>
        <div className={styles.hero}>
          <span className={styles.heroLabel}>{cost ? t('settings.usage.stat.estimatedCost') : t('settings.usage.stat.totalTokens')}</span>
          <span className={styles.heroValue}>{heroValue}</span>
        </div>
        <div className={styles.chart}>{body}</div>
        <div className={styles.historyLegend}>
          {state.data && total > 0 && <ChartLegend series={series} labelFor={labelFor} />}
        </div>
      </div>
    </SettingsGroup>
  )
}
