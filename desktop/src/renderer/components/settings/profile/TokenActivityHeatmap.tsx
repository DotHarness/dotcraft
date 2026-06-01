import { useMemo, useState, type JSX, type MouseEvent as ReactMouseEvent } from 'react'
import { createPortal } from 'react-dom'
import type { MessageKey } from '../../../../shared/locales'
import { useT, useLocale } from '../../../contexts/LocaleContext'
import type { UsageDayWire } from '../../../stores/profileStore'

export type HeatmapMode = 'daily' | 'weekly' | 'cumulative'

interface TokenActivityHeatmapProps {
  days: UsageDayWire[]
  mode: HeatmapMode
}

const CELL = 11
const GAP = 3
const STEP = CELL + GAP
const ROWS = 7
const WEEKS = 53
const LABEL_HEIGHT = 16

/** Intrinsic pixel width of the heatmap grid; used to align the section header (tabs) to it. */
export const HEATMAP_WIDTH = WEEKS * STEP - GAP

// Entrance reveal: cells pop in as a left→right "wave", staggered by grid
// position. Delay = col * COL_STEP + row * ROW_STEP (ms). The keyframes,
// duration, easing, and reduced-motion fallback live in `styles/tokens.css`
// (`.heatmap-cell` / `.heatmap-month-label`). The reveal runs once on mount —
// opening the Profile tab remounts this component — and recolors via a `fill`
// transition when the mode changes without re-running.
const ENTER_COL_STEP = 11
const ENTER_ROW_STEP = 7
/** Month labels fade in just after the wave reaches their column. */
const ENTER_LABEL_OFFSET = 76

/** Local YYYY-MM-DD, matching the server's local-day bucketing (spec §27A.3). */
function localKey(d: Date): string {
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate())
}

function addDays(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate() + n)
}

interface Cell {
  key: string
  x: number
  y: number
  /** Grid column/row, driving the entrance stagger delay. */
  col: number
  row: number
  /** The per-day token total (always the raw daily value). */
  dayTokens: number
  /** The value driving the cell color, depends on the active mode. */
  intensityValue: number
}

/**
 * GitHub-contribution-style heatmap of daily token usage. Pure SVG, no chart
 * dependency. Color intensity is bucketed into five levels using `--accent`.
 * The three modes recolor the same daily grid: `daily` uses each day's tokens,
 * `weekly` shares each week column's total, `cumulative` ramps by running total.
 */
export function TokenActivityHeatmap({ days, mode }: TokenActivityHeatmapProps): JSX.Element {
  const t = useT()
  const locale = useLocale()

  const { cells, maxIntensity, monthLabels } = useMemo(() => {
    const tokensByDay = new Map<string, number>()
    for (const d of days) tokensByDay.set(d.date, d.totalTokens)

    const today = startOfDay(new Date())
    // Align the grid so the last column is the current (Sunday-started) week.
    const currentWeekSunday = addDays(today, -today.getDay())
    const gridStart = addDays(currentWeekSunday, -(WEEKS - 1) * 7)

    // Precompute a cumulative running total across the full history (sorted).
    const cumulativeByDay = new Map<string, number>()
    if (mode === 'cumulative') {
      const sorted = [...days].sort((a, b) => a.date.localeCompare(b.date))
      let running = 0
      for (const d of sorted) {
        running += d.totalTokens
        cumulativeByDay.set(d.date, running)
      }
    }

    const grid: Cell[] = []
    const weekTotals: number[] = []
    for (let w = 0; w < WEEKS; w++) {
      let weekTotal = 0
      for (let r = 0; r < ROWS; r++) {
        const date = addDays(gridStart, w * 7 + r)
        const key = localKey(date)
        weekTotal += tokensByDay.get(key) ?? 0
      }
      weekTotals.push(weekTotal)
    }

    // Running cumulative value as of the end of each column, for the cumulative mode.
    let lastCumulative = 0
    for (let w = 0; w < WEEKS; w++) {
      for (let r = 0; r < ROWS; r++) {
        const date = addDays(gridStart, w * 7 + r)
        if (date > today) continue
        const key = localKey(date)
        const dayTokens = tokensByDay.get(key) ?? 0
        let intensityValue = dayTokens
        if (mode === 'weekly') {
          intensityValue = weekTotals[w]
        } else if (mode === 'cumulative') {
          const c = cumulativeByDay.get(key)
          if (c !== undefined) lastCumulative = c
          intensityValue = lastCumulative
        }
        grid.push({ key, x: w * STEP, y: r * STEP, col: w, row: r, dayTokens, intensityValue })
      }
    }

    const max = grid.reduce((m, c) => Math.max(m, c.intensityValue), 0)

    // Month labels: place one at each column where the month changes.
    const monthFmt = new Intl.DateTimeFormat(locale, { month: 'short' })
    const labels: Array<{ x: number; col: number; text: string }> = []
    let lastMonth = -1
    for (let w = 0; w < WEEKS; w++) {
      const colDate = addDays(gridStart, w * 7)
      if (colDate > today) break
      const month = colDate.getMonth()
      if (month !== lastMonth) {
        labels.push({ x: w * STEP, col: w, text: monthFmt.format(colDate) })
        lastMonth = month
      }
    }

    return { cells: grid, maxIntensity: max, monthLabels: labels }
  }, [days, mode, locale])

  const width = HEATMAP_WIDTH
  const height = ROWS * STEP - GAP
  const [tip, setTip] = useState<{ text: string; left: number; top: number } | null>(null)

  const showTip = (event: ReactMouseEvent<SVGRectElement>, cell: Cell): void => {
    const rect = event.currentTarget.getBoundingClientRect()
    setTip({ text: cellTooltip(cell, t), left: rect.left + rect.width / 2, top: rect.top })
  }

  return (
    <div className="heatmap-root" style={{ overflowX: 'auto', paddingBottom: '2px' }}>
      <svg
        width={width}
        height={height + LABEL_HEIGHT}
        viewBox={`0 0 ${width} ${height + LABEL_HEIGHT}`}
        preserveAspectRatio="xMinYMid meet"
        role="img"
        aria-label={t('settings.profile.heatmap.title')}
        style={{ display: 'block', width: '100%', height: 'auto' }}
      >
        {cells.map((cell) => (
          <rect
            key={cell.key}
            x={cell.x}
            y={cell.y}
            width={CELL}
            height={CELL}
            rx={2}
            ry={2}
            fill={levelColor(intensityLevel(cell.intensityValue, maxIntensity))}
            className="heatmap-cell"
            style={{ animationDelay: `${cell.col * ENTER_COL_STEP + cell.row * ENTER_ROW_STEP}ms` }}
            onMouseEnter={(e) => showTip(e, cell)}
            onMouseLeave={() => setTip(null)}
          />
        ))}
        {monthLabels.map((label) => (
          <text
            key={`${label.text}-${label.x}`}
            x={label.x}
            y={height + LABEL_HEIGHT - 4}
            fontSize={10}
            fill="var(--text-dimmed)"
            className="heatmap-month-label"
            style={{ animationDelay: `${label.col * ENTER_COL_STEP + ENTER_LABEL_OFFSET}ms` }}
          >
            {label.text}
          </text>
        ))}
      </svg>
      {tip &&
        createPortal(
          <div
            className="dc-action-tooltip"
            role="tooltip"
            style={{
              position: 'fixed',
              left: tip.left,
              top: tip.top - GAP,
              transform: 'translate(-50%, -100%)',
              zIndex: 'var(--z-tooltip)',
              pointerEvents: 'none'
            }}
          >
            <span className="dc-action-tooltip__label">{tip.text}</span>
          </div>,
          document.body
        )}
    </div>
  )
}

function intensityLevel(value: number, max: number): number {
  if (value <= 0 || max <= 0) return 0
  return Math.min(4, Math.ceil((value / max) * 4))
}

function levelColor(level: number): string {
  switch (level) {
    case 1:
      return 'color-mix(in srgb, var(--accent) 25%, var(--bg-tertiary))'
    case 2:
      return 'color-mix(in srgb, var(--accent) 45%, var(--bg-tertiary))'
    case 3:
      return 'color-mix(in srgb, var(--accent) 70%, var(--bg-tertiary))'
    case 4:
      return 'var(--accent)'
    default:
      return 'var(--bg-tertiary)'
  }
}

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

function cellTooltip(cell: Cell, t: TFn): string {
  if (cell.dayTokens <= 0) return t('settings.profile.heatmap.empty', { date: cell.key })
  return t('settings.profile.heatmap.tooltip', {
    date: cell.key,
    tokens: cell.dayTokens.toLocaleString()
  })
}
