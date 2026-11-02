import type { CSSProperties, JSX } from 'react'
import type { TokenHudStrings } from './i18n'
import type { ThreadUsageGroup, ThreadUsageSnapshot } from './usage'

const SERIES_SLOTS = 4

function seriesColor(index: number): string {
  return index < SERIES_SLOTS ? `var(--chart-series-${index + 1})` : 'var(--chart-series-other)'
}

interface ModelSlice {
  label: string
  totalTokens: number
  costUsd: number | null
}

/** The server splits a thread's turns finer than the model; this readout only names the model. */
function byModel(groups: readonly ThreadUsageGroup[], strings: TokenHudStrings): ModelSlice[] {
  const totals = new Map<string, ModelSlice>()
  for (const group of groups) {
    const label = group.model ?? strings.unknownModel
    const slice = totals.get(label) ?? { label, totalTokens: 0, costUsd: null }
    slice.totalTokens += group.totalTokens
    if (group.costUsd !== null) slice.costUsd = (slice.costUsd ?? 0) + group.costUsd
    totals.set(label, slice)
  }
  return [...totals.values()]
    .filter((slice) => slice.totalTokens > 0)
    .sort((a, b) => b.totalTokens - a.totalTokens)
}

interface ThreadUsagePopoverProps {
  usage: ThreadUsageSnapshot | null
  strings: TokenHudStrings
  formatTokens: (value: number) => string
  formatCost: (value: number) => string
  style: CSSProperties
}

export function ThreadUsagePopover({ usage, strings, formatTokens, formatCost, style }: ThreadUsagePopoverProps): JSX.Element {
  const slices = usage === null ? [] : byModel(usage.groups, strings)
  const total = usage?.totalTokens ?? 0
  return (
    <div className="token-hud-popover" role="dialog" aria-label={strings.threadUsageTitle} style={style}>
      <div className="token-hud-popover-title">{strings.threadUsageTitle}</div>
      {usage === null || slices.length === 0 || total <= 0 ? (
        <div className="token-hud-popover-empty">{strings.threadUsageEmpty}</div>
      ) : (
        <>
          <div className="token-hud-popover-bar" aria-hidden="true">
            {slices.map((slice, index) => (
              <span
                key={slice.label}
                style={{ width: `${(slice.totalTokens / total) * 100}%`, background: seriesColor(index) }}
              />
            ))}
          </div>
          <ul className="token-hud-popover-rows">
            {slices.map((slice, index) => (
              <li key={slice.label}>
                <span className="token-hud-popover-swatch" style={{ background: seriesColor(index) }} />
                <span className="token-hud-popover-label">{slice.label}</span>
                <span className="token-hud-popover-value">
                  {`${Math.round((slice.totalTokens / total) * 100)}% · ${formatTokens(slice.totalTokens)}`}
                  {slice.costUsd === null ? '' : ` · ${formatCost(slice.costUsd)}`}
                </span>
              </li>
            ))}
          </ul>
          <div className="token-hud-popover-footer">
            {`${formatTokens(total)} ${strings.total} · ${usage.turns} ${strings.turnsUnit}`}
            {usage.costUsd === null ? '' : ` · ${formatCost(usage.costUsd)}`}
          </div>
        </>
      )}
    </div>
  )
}
