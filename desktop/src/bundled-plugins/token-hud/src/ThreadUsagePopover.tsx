import type { CSSProperties, JSX } from 'react'
import type { TokenHudStrings } from './i18n'
import type { ThreadUsageGroup, ThreadUsageSnapshot } from './usage'

const SERIES_SLOTS = 4

function seriesColor(index: number): string {
  return index < SERIES_SLOTS ? `var(--chart-series-${index + 1})` : 'var(--chart-series-other)'
}

function reasoningLabel(value: string | null, strings: TokenHudStrings): string | null {
  switch (value) {
    case null:
      return null
    case 'low':
      return strings.reasoningLow
    case 'medium':
      return strings.reasoningMedium
    case 'high':
      return strings.reasoningHigh
    case 'extrahigh':
      return strings.reasoningExtraHigh
    default:
      return value
  }
}

function speedLabel(value: string | null, strings: TokenHudStrings): string | null {
  switch (value) {
    case null:
      return null
    case 'standard':
      return strings.speedStandard
    case 'fast':
      return strings.speedFast
    default:
      return value
  }
}

function groupLabel(group: ThreadUsageGroup, strings: TokenHudStrings): string {
  return [group.model ?? strings.unknownModel, reasoningLabel(group.reasoningEffort, strings), speedLabel(group.speed, strings)]
    .filter((part): part is string => part !== null)
    .join(' · ')
}

interface ThreadUsagePopoverProps {
  usage: ThreadUsageSnapshot | null
  strings: TokenHudStrings
  formatTokens: (value: number) => string
  style: CSSProperties
}

export function ThreadUsagePopover({ usage, strings, formatTokens, style }: ThreadUsagePopoverProps): JSX.Element {
  const groups = usage?.groups.filter((group) => group.totalTokens > 0) ?? []
  const total = usage?.totalTokens ?? 0
  return (
    <div className="token-hud-popover" role="dialog" aria-label={strings.threadUsageTitle} style={style}>
      <div className="token-hud-popover-title">{strings.threadUsageTitle}</div>
      {usage === null || groups.length === 0 || total <= 0 ? (
        <div className="token-hud-popover-empty">{strings.threadUsageEmpty}</div>
      ) : (
        <>
          <div className="token-hud-popover-bar" aria-hidden="true">
            {groups.map((group, index) => (
              <span
                key={groupLabel(group, strings)}
                style={{ width: `${(group.totalTokens / total) * 100}%`, background: seriesColor(index) }}
              />
            ))}
          </div>
          <ul className="token-hud-popover-rows">
            {groups.map((group, index) => (
              <li key={groupLabel(group, strings)}>
                <span className="token-hud-popover-swatch" style={{ background: seriesColor(index) }} />
                <span className="token-hud-popover-label">{groupLabel(group, strings)}</span>
                <span className="token-hud-popover-value">
                  {`${Math.round((group.totalTokens / total) * 100)}% · ${formatTokens(group.totalTokens)}`}
                </span>
              </li>
            ))}
          </ul>
          <div className="token-hud-popover-footer">
            {`${formatTokens(total)} ${strings.total} · ${usage.turns} ${strings.turnsUnit}`}
          </div>
        </>
      )}
    </div>
  )
}
