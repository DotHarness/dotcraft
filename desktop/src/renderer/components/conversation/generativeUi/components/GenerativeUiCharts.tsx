import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Line, LineChart, Pie, PieChart, Tooltip, XAxis, YAxis } from 'recharts'
import { useLocale } from '../../../../contexts/LocaleContext'
import { formatGenerativeUiValue, type GenerativeUiValueFormat } from '../generativeUiFormat'
import type { GenerativeUiProps } from '../generativeUiSchema'
import styles from './GenerativeUiData.module.css'

const AXIS_TICK = { fill: 'var(--text-dimmed)', fontSize: 11 } as const
const SERIES_COLORS = ['var(--chart-series-1)', 'var(--chart-series-2)', 'var(--chart-series-3)', 'var(--chart-series-4)']

function seriesColor(color: string | undefined, index: number): string {
  if (color === 'other') return 'var(--chart-series-other)'
  if (color) return `var(--chart-${color})`
  return SERIES_COLORS[index] ?? 'var(--chart-series-other)'
}

function MeasuredChart({ height, children }: { height: number; children: (width: number) => ReactNode }): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    setWidth(Math.round(element.getBoundingClientRect().width))
    const observer = new ResizeObserver(entries => {
      const next = Math.round(entries[0]?.contentRect.width ?? 0)
      setWidth(current => (current === next ? current : next))
    })
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  return <div ref={ref} className={styles.chart} style={{ height }}>{width > 0 && children(width)}</div>
}

interface TooltipEntry { name?: string | number; value?: unknown; color?: string; payload?: Record<string, unknown> }

function ChartTooltip({ active, payload, label, format }: {
  active?: boolean
  payload?: ReadonlyArray<TooltipEntry>
  label?: unknown
  format: (value: unknown) => string
}): JSX.Element | null {
  if (!active || !payload?.length) return null
  return (
    <div className={styles.tooltip}>
      {label !== undefined && label !== '' && <span className={styles.tooltipLabel}>{String(label)}</span>}
      {payload.map((entry, index) => (
        <span key={index} className={styles.tooltipRow}>
          <span className={styles.legendItem}>
            <span className={styles.legendSwatch} style={{ background: entry.color ?? String(entry.payload?.fill ?? '') }} />
            {String(entry.name ?? '')}
          </span>
          <span>{format(entry.value)}</span>
        </span>
      ))}
    </div>
  )
}

function Legend({ items }: { items: Array<{ label: string; color: string; value?: string }> }): JSX.Element {
  return (
    <div className={styles.legend}>
      {items.map((item, index) => (
        <span key={index} className={styles.legendItem}>
          <span className={styles.legendSwatch} style={{ background: item.color }} />
          <span>{item.label}</span>
          {item.value && <span>{item.value}</span>}
        </span>
      ))}
    </div>
  )
}

function useValueFormat(format: GenerativeUiValueFormat | undefined, currency: string | undefined): (value: unknown) => string {
  const locale = useLocale()
  return value => formatGenerativeUiValue(value, format, locale, currency)
}

export default function GenerativeUiChart({ props }: { props: GenerativeUiProps<'Chart'> }): JSX.Element {
  const height = props.height ?? 200
  const format = useValueFormat(props.format, props.currency)
  const axisFormat = useValueFormat(props.format === 'number' || props.format === undefined ? 'compact' : props.format, props.currency)
  const series = props.series.map((entry, index) => ({ ...entry, name: entry.label ?? entry.key, color: seriesColor(entry.color, index) }))
  const stackId = props.stacked ? 'stack' : undefined
  const common = { data: props.data, margin: { top: 4, right: 4, left: 0, bottom: 0 } }
  const axes = [
    <CartesianGrid key="grid" vertical={false} stroke="var(--border-subtle)" />,
    <XAxis key="x" dataKey={props.xKey} tick={AXIS_TICK} axisLine={false} tickLine={false} interval="preserveStartEnd" minTickGap={24} />,
    <YAxis key="y" tick={AXIS_TICK} axisLine={false} tickLine={false} width={44} tickFormatter={axisFormat} />,
    <Tooltip
      key="tooltip"
      isAnimationActive={false}
      cursor={props.type === 'bar' ? { fill: 'var(--bg-hover)' } : { stroke: 'var(--border-default)' }}
      content={<ChartTooltip format={format} />}
    />
  ]
  return (
    <div>
      <MeasuredChart height={height}>
        {width => props.type === 'bar' ? (
          <BarChart width={width} height={height} {...common} barCategoryGap="24%">
            {axes}
            {series.map((entry, index) => (
              <Bar key={entry.key} dataKey={entry.key} name={entry.name} fill={entry.color} stackId={stackId} isAnimationActive={false}
                radius={!stackId || index === series.length - 1 ? [4, 4, 0, 0] : 0} />
            ))}
          </BarChart>
        ) : props.type === 'area' ? (
          <AreaChart width={width} height={height} {...common}>
            {axes}
            {series.map(entry => (
              <Area key={entry.key} type="monotone" dataKey={entry.key} name={entry.name} stroke={entry.color} strokeWidth={2}
                fill={entry.color} fillOpacity={0.14} stackId={stackId} isAnimationActive={false} />
            ))}
          </AreaChart>
        ) : (
          <LineChart width={width} height={height} {...common}>
            {axes}
            {series.map(entry => (
              <Line key={entry.key} type="monotone" dataKey={entry.key} name={entry.name} stroke={entry.color} strokeWidth={2}
                dot={false} activeDot={{ r: 3 }} isAnimationActive={false} />
            ))}
          </LineChart>
        )}
      </MeasuredChart>
      {(props.showLegend ?? series.length > 1) && <Legend items={series.map(entry => ({ label: entry.name, color: entry.color }))} />}
    </div>
  )
}

export function GenerativeUiPieChart({ props }: { props: GenerativeUiProps<'PieChart'> }): JSX.Element {
  const height = props.height ?? 200
  const format = useValueFormat(props.format, props.currency)
  const slices = props.data.map((entry, index) => ({ ...entry, fill: seriesColor(entry.color, index) }))
  return (
    <div>
      <MeasuredChart height={height}>
        {width => (
          <PieChart width={width} height={height}>
            <Pie data={slices} dataKey="value" nameKey="label" innerRadius={props.donut ? '58%' : 0} outerRadius="88%"
              paddingAngle={slices.length > 1 ? 1 : 0} stroke="none" isAnimationActive={false}>
              {slices.map((slice, index) => <Cell key={index} fill={slice.fill} />)}
            </Pie>
            <Tooltip isAnimationActive={false} content={<ChartTooltip format={format} />} />
          </PieChart>
        )}
      </MeasuredChart>
      {(props.showLegend ?? true) && <Legend items={slices.map(slice => ({ label: slice.label, color: slice.fill, value: format(slice.value) }))} />}
    </div>
  )
}
