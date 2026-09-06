import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import type { MessageKey } from '../../../shared/locales'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUsageStore, type UsageSummaryWire } from '../../stores/usageStore'
import { formatCompactCount } from '../../utils/formatCompactCount'
import { ActionTooltip } from '../ui/ActionTooltip'
import { RefreshIcon } from '../ui/AppIcons'
import { IconButton } from '../ui/IconButton'
import { Button } from '../ui/Button'
import { RunningSpinner } from '../ui/RunningSpinner'
import { settingsLabelStyle, settingsMetaTextStyle, settingsPlaceholderStyle } from './settingsTypography'
import { SettingsGroup, SettingsRow } from './SettingsGroup'

/**
 * Settings → Usage overview: aggregate token/activity stats pulled from the AppServer
 * `usage/summary` method (spec §27A). Gated behind the `usageTelemetry` capability,
 * which is false when tracing is disabled.
 */
export function UsageOverview(): JSX.Element | null {
  const t = useT()
  const capable = useConnectionStore((s) => s.capabilities?.usageTelemetry === true)
  const summary = useUsageStore((s) => s.summary)
  const loading = useUsageStore((s) => s.loading)
  const loadedOnce = useUsageStore((s) => s.loadedOnce)
  const error = useUsageStore((s) => s.error)
  const fetchSummary = useUsageStore((s) => s.fetchSummary)
  const [refreshing, setRefreshing] = useState(false)

  useEffect(() => {
    if (capable) void fetchSummary()
  }, [capable, fetchSummary])

  const handleRefresh = async (): Promise<void> => {
    setRefreshing(true)
    try {
      await fetchSummary()
    } finally {
      setRefreshing(false)
    }
  }

  if (!capable) {
    return (
      <SettingsGroup title={t('settings.usage.overviewTitle')}>
        <SettingsRow>
          <div style={dimmedTextStyle}>{t('settings.usage.unavailable')}</div>
        </SettingsRow>
      </SettingsGroup>
    )
  }

  const isEmpty =
    summary != null && summary.totalTokens <= 0 && summary.sessionCount <= 0

  return (
    <SettingsGroup
      title={t('settings.usage.overviewTitle')}
      description={t('settings.usage.overviewHint')}
      headerAction={
        <IconButton
          icon={refreshing ? <RunningSpinner size={15} /> : <RefreshIcon size={15} />}
          label={t('settings.usage.refresh')}
          tooltipLabel={t('settings.usage.refresh')}
          onClick={() => void handleRefresh()}
          disabled={refreshing || loading}
        />
      }
    >
      {loading && !loadedOnce ? (
        <SettingsRow>
          <div style={{ ...dimmedTextStyle, display: 'flex', alignItems: 'center', gap: '8px' }}>
            <RunningSpinner size={12} />
            {t('settings.usage.refresh')}
          </div>
        </SettingsRow>
      ) : error && summary == null ? (
        <SettingsRow>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            <div style={{ ...dimmedTextStyle, color: 'var(--error)' }}>
              {t('settings.usage.loadError')}
            </div>
            <Button variant="secondary" onClick={() => void handleRefresh()} style={{ alignSelf: 'flex-start' }}>
              {t('settings.usage.retry')}
            </Button>
          </div>
        </SettingsRow>
      ) : isEmpty ? (
        <SettingsRow>
          <div style={dimmedTextStyle}>{t('settings.usage.empty')}</div>
        </SettingsRow>
      ) : summary != null ? (
        <SettingsRow>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', width: '100%' }}>
            <StatGrid summary={summary} t={t} />
            <TokenBreakdown summary={summary} t={t} />
          </div>
        </SettingsRow>
      ) : null}
    </SettingsGroup>
  )
}

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

function StatGrid({ summary, t }: { summary: UsageSummaryWire; t: TFn }): JSX.Element {
  const stats: Array<{ label: MessageKey; value: string; full: number; danger?: boolean }> = [
    { label: 'settings.usage.stat.totalTokens', value: formatCompactCount(summary.totalTokens), full: summary.totalTokens },
    { label: 'settings.usage.stat.inputTokens', value: formatCompactCount(summary.totalInputTokens), full: summary.totalInputTokens },
    { label: 'settings.usage.stat.outputTokens', value: formatCompactCount(summary.totalOutputTokens), full: summary.totalOutputTokens },
    { label: 'settings.usage.stat.cacheHitRate', value: formatPercent(summary.cacheHitRate), full: summary.cacheHitRate },
    { label: 'settings.usage.stat.sessions', value: formatCompactCount(summary.sessionCount), full: summary.sessionCount },
    { label: 'settings.usage.stat.requests', value: formatCompactCount(summary.totalRequests), full: summary.totalRequests },
    { label: 'settings.usage.stat.toolCalls', value: formatCompactCount(summary.totalToolCalls), full: summary.totalToolCalls },
    { label: 'settings.usage.stat.errors', value: formatCompactCount(summary.totalErrors), full: summary.totalErrors, danger: summary.totalErrors > 0 }
  ]

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(130px, 1fr))',
        gap: '16px'
      }}
    >
      {stats.map((stat) => (
        <div key={stat.label} style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0 }}>
          <ActionTooltip
            label={stat.full.toLocaleString()}
            wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}
          >
            <div
              style={{
                fontSize: '20px',
                fontWeight: 600,
                lineHeight: 1.2,
                color: stat.danger ? 'var(--error)' : 'var(--text-primary)',
                display: 'block'
              }}
            >
              {stat.value}
            </div>
          </ActionTooltip>
          <div style={settingsMetaTextStyle()}>{t(stat.label)}</div>
        </div>
      ))}
    </div>
  )
}

function TokenBreakdown({ summary, t }: { summary: UsageSummaryWire; t: TFn }): JSX.Element | null {
  const total = summary.totalTokens
  if (total <= 0) return null

  const allSegments: Array<{ label: MessageKey; value: number; color: string }> = [
    { label: 'settings.usage.breakdown.fresh', value: Math.max(0, summary.totalFreshInputTokens), color: 'var(--accent)' },
    { label: 'settings.usage.breakdown.cached', value: Math.max(0, summary.totalCachedInputTokens), color: 'color-mix(in srgb, var(--accent) 40%, transparent)' },
    { label: 'settings.usage.breakdown.cacheWrite', value: Math.max(0, summary.totalCacheWriteInputTokens), color: 'color-mix(in srgb, var(--accent) 65%, transparent)' },
    { label: 'settings.usage.breakdown.output', value: Math.max(0, summary.totalOutputTokens), color: 'color-mix(in srgb, var(--text-primary) 35%, transparent)' }
  ]
  const segments = allSegments.filter((s) => s.value > 0)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      <div style={settingsLabelStyle()}>
        {t('settings.usage.breakdownTitle')}
      </div>
      <div
        style={{
          display: 'flex',
          width: '100%',
          height: '10px',
          borderRadius: '5px',
          overflow: 'hidden',
          background: 'var(--bg-tertiary)'
        }}
      >
        {segments.map((seg) => (
          <ActionTooltip
            key={seg.label}
            label={`${t(seg.label)}: ${seg.value.toLocaleString()}`}
            wrapperStyle={{ width: `${(seg.value / total) * 100}%` }}
          >
            <div
              style={{ width: '100%', background: seg.color }}
            />
          </ActionTooltip>
        ))}
      </div>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: '12px 16px' }}>
        {segments.map((seg) => (
          <div key={seg.label} style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span style={{ width: '10px', height: '10px', borderRadius: '3px', background: seg.color, flexShrink: 0 }} />
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>{t(seg.label)}</span>
            <span style={{ fontSize: '12px', color: 'var(--text-dimmed)' }}>{formatCompactCount(seg.value)}</span>
          </div>
        ))}
      </div>
      {summary.totalReasoningOutputTokens > 0 && (
        <div style={settingsMetaTextStyle()}>
          {t('settings.usage.breakdown.reasoningNote', { count: formatCompactCount(summary.totalReasoningOutputTokens) })}
        </div>
      )}
    </div>
  )
}

function formatPercent(ratio: number): string {
  if (!Number.isFinite(ratio)) return '0%'
  return `${(ratio * 100).toFixed(1)}%`
}

const dimmedTextStyle: CSSProperties = settingsPlaceholderStyle()
