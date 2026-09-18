import { useLayoutEffect, useRef, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { flushSync } from 'react-dom'
import type { UsageRange } from '../../../stores/usageStore'
import { Button } from '../../ui/Button'
import { SegmentedControl, type SegmentedOption } from '../ui/SegmentedControl'
import type { ChartPoint, ChartSeries } from './chartSeries'
import type { TFn } from './usageLabels'
import styles from './usage.module.css'

export const AXIS_TICK = { fill: 'var(--text-dimmed)', fontSize: 11 } as const
export const CHART_HEIGHT = 144
export const CHART_MARGIN = { top: 4, right: 0, left: 0, bottom: 0 } as const
export const SECTION_STYLE: CSSProperties = { gap: '22px' }

/** Sizes the chart in the same frame as its container, so the plot never trails the card edge while the window is being resized. */
export function ChartFrame({ children }: { children: (width: number) => ReactNode }): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    setWidth(Math.round(element.getBoundingClientRect().width))
    const observer = new ResizeObserver((entries) => {
      const next = Math.round(entries[0]?.contentRect.width ?? 0)
      flushSync(() => setWidth((current) => (current === next ? current : next)))
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return (
    <div ref={ref} className={styles.chart}>
      {width > 0 && children(width)}
    </div>
  )
}

/** Only the first, middle and last day are labelled so the axis never crowds. */
export function axisTicks(dayKeys: string[]): string[] {
  if (dayKeys.length <= 3) return dayKeys
  return [dayKeys[0], dayKeys[Math.floor(dayKeys.length / 2)], dayKeys[dayKeys.length - 1]]
}

interface AxisTickProps {
  x?: number | string
  y?: number | string
  payload: { value: unknown }
  index: number
  visibleTicksCount: number
  tickFormatter?: (value: string, index: number) => string
}

/** The edge labels hug the plot so the last day never clips at the card padding. */
export function AxisTick({ x, y, payload, index, visibleTicksCount, tickFormatter }: AxisTickProps): JSX.Element {
  const value = String(payload.value)
  const anchor = index === 0 ? 'start' : index === visibleTicksCount - 1 ? 'end' : 'middle'
  return (
    <text x={x} y={y} dy={11} textAnchor={anchor} fill={AXIS_TICK.fill} fontSize={AXIS_TICK.fontSize}>
      {tickFormatter ? tickFormatter(value, index) : value}
    </text>
  )
}

export function RangeToggle({
  value,
  onChange,
  t
}: {
  value: UsageRange
  onChange: (range: UsageRange) => void
  t: TFn
}): JSX.Element {
  const options: SegmentedOption<UsageRange>[] = [
    { value: '7d', label: t('settings.usage.range.7d') },
    { value: '30d', label: t('settings.usage.range.30d') }
  ]
  return <SegmentedControl value={value} options={options} onChange={onChange} ariaLabel={t('settings.usage.range.label')} />
}

export function ChartLegend({
  series,
  labelFor
}: {
  series: ChartSeries[]
  labelFor: (key: string) => string
}): JSX.Element {
  return (
    <div className={styles.legend}>
      {series.map((entry) => (
        <span key={entry.key} className={styles.legendItem}>
          <span className={styles.legendSwatch} style={{ background: entry.color }} />
          <span>{labelFor(entry.key)}</span>
        </span>
      ))}
    </div>
  )
}

export function ChartEmpty({ message }: { message: string }): JSX.Element {
  return <div className={styles.empty}>{message}</div>
}

export function ChartError({
  message,
  retryLabel,
  onRetry
}: {
  message: string
  retryLabel: string
  onRetry: () => void
}): JSX.Element {
  return (
    <div className={styles.error}>
      <span>{message}</span>
      <Button variant="secondary" onClick={onRetry}>
        {retryLabel}
      </Button>
    </div>
  )
}

interface ChartTooltipProps {
  active?: boolean
  payload?: ReadonlyArray<{ payload?: ChartPoint }>
  series: ChartSeries[]
  labelFor: (key: string) => string
  totalLabel: string
  formatDate: (date: string) => string
}

/** Rendered by recharts, which injects `active` and `payload` for the hovered day. */
export function ChartTooltip({
  active,
  payload,
  series,
  labelFor,
  totalLabel,
  formatDate
}: ChartTooltipProps): JSX.Element | null {
  const point = payload?.[0]?.payload
  if (!active || !point) return null
  return (
    <div className={styles.tooltip}>
      <div className={styles.tooltipTitle}>{formatDate(point.date)}</div>
      {series.map((entry) => (
        <div key={entry.key} className={styles.tooltipRow}>
          <span className={styles.tooltipRowLabel}>
            <span className={styles.legendSwatch} style={{ background: entry.color }} />
            {labelFor(entry.key)}
          </span>
          <span className={styles.tooltipValue}>{(point.values[entry.key] ?? 0).toLocaleString()}</span>
        </div>
      ))}
      <div className={`${styles.tooltipRow} ${styles.tooltipRowTotal}`}>
        <span className={styles.tooltipRowLabel}>{totalLabel}</span>
        <span className={styles.tooltipValue}>{point.total.toLocaleString()}</span>
      </div>
    </div>
  )
}
