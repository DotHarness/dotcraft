import type { JSX } from 'react'
import type { MessageKey } from '../../../../shared/locales'
import type { ProfileInsightsWire, RankedMetricWire } from '../../../stores/profileStore'
import styles from './ProfileInsights.module.css'

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
    <section className={styles.column}>
      <h2 className={styles.heading}>{t('settings.profile.insights.title')}</h2>
      <dl className={styles.list}>
        {rows.map((row) => (
          <div key={row.label} className={styles.row}>
            <dt className={styles.label}>{t(row.label)}</dt>
            <dd title={row.value} className={styles.value}>
              {row.value}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}
