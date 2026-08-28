import type { CSSProperties, JSX } from 'react'
import type { MessageKey } from '../../../../shared/locales'
import type { ProfileInsightsWire, RankedMetricWire } from '../../../stores/profileStore'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

/** Known reasoning-effort tokens (lowercased server-side) → localized label keys. */
const EFFORT_LABEL_KEYS: Record<string, MessageKey> = {
  low: 'settings.profile.insights.effort.low',
  medium: 'settings.profile.insights.effort.medium',
  high: 'settings.profile.insights.effort.high',
  extrahigh: 'settings.profile.insights.effort.extrahigh'
}

/** "key · 72%" — share omitted when the denominator is zero. */
function formatRanked(metric: RankedMetricWire | null, label: string): string {
  if (!metric || !metric.key) return '—'
  if (metric.total <= 0) return label
  const pct = Math.round((metric.count / metric.total) * 100)
  return `${label} · ${pct}%`
}

/**
 * Spec §27A.5. Reasoning and skill metrics are forward-only, so they read 0 or an
 * em-dash until usage accrues.
 */
export function ActivityInsights({
  insights,
  t
}: {
  insights: ProfileInsightsWire
  t: TFn
}): JSX.Element {
  const reasoningLabel = insights.topReasoning?.key
    ? t(EFFORT_LABEL_KEYS[insights.topReasoning.key] ?? insights.topReasoning.key)
    : ''

  const rows: Array<{ label: MessageKey; value: string }> = [
    {
      label: 'settings.profile.insights.mostUsedModel',
      value: formatRanked(insights.topModel, insights.topModel?.key ?? '')
    },
    {
      label: 'settings.profile.insights.mostUsedReasoning',
      value: formatRanked(insights.topReasoning, reasoningLabel)
    },
    {
      label: 'settings.profile.insights.skillsExplored',
      value: insights.skillsExplored.toLocaleString()
    },
    {
      label: 'settings.profile.insights.totalSkillsUsed',
      value: insights.totalSkillsUsed.toLocaleString()
    },
    {
      label: 'settings.profile.insights.totalThreads',
      value: insights.totalThreads.toLocaleString()
    }
  ]

  return (
    <section style={{ display: 'flex', flexDirection: 'column', gap: '12px', minWidth: 0 }}>
      <div style={headingStyle}>{t('settings.profile.insights.title')}</div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
        {rows.map((row) => (
          <div key={row.label} style={rowStyle}>
            <span style={labelStyle}>{t(row.label)}</span>
            <span title={row.value} style={valueStyle}>
              {row.value}
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}

const headingStyle: CSSProperties = {
  fontSize: '15px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const rowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  gap: '12px'
}

const labelStyle: CSSProperties = {
  fontSize: '13px',
  color: 'var(--text-dimmed)',
  flexShrink: 0
}

const valueStyle: CSSProperties = {
  fontSize: '13px',
  fontWeight: 500,
  color: 'var(--text-primary)',
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  textAlign: 'right'
}
