import { useMemo, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { parseCronCreatedResult } from '../../utils/cronToolDisplay'
import { formatNextRun } from '../../utils/cronNextRunDisplay'
import { useUIStore } from '../../stores/uiStore'
import { useCronStore } from '../../stores/cronStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface CronCreatedCardProps {
  item: ConversationItem
  locale: AppLocale
}

export function CronCreatedCard({ item, locale }: CronCreatedCardProps): JSX.Element | null {
  const parsed = useMemo(() => parseCronCreatedResult(item.result, locale), [item.result, locale])
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setAutomationsTab = useUIStore((s) => s.setAutomationsTab)
  const selectCronJob = useCronStore((s) => s.selectCronJob)

  if (!parsed) return null

  const title = parsed.jobName ?? parsed.message ?? translate(locale, 'cron.card.nameFallback')
  const schedulePhrase = parsed.schedulePhrase
  const nextRun = formatNextRun(parsed.nextRunAtMs ?? null, true, locale)

  function openInAutomations(): void {
    setActiveMainView('automations')
    setAutomationsTab('cron')
    if (parsed?.jobId) {
      selectCronJob(parsed.jobId)
    }
  }

  const canOpen = !!parsed.jobId

  return (
    <div
      style={{
        border: '1px solid var(--border-default)',
        borderRadius: '10px',
        background: 'var(--bg-secondary)',
        padding: '12px 14px',
        display: 'flex',
        flexDirection: 'column',
        gap: '6px'
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '8px',
          fontSize: '11px',
          color: 'var(--text-dimmed)'
        }}
      >
        <span aria-hidden style={{ fontSize: '13px' }}>⏰</span>
        <span style={{ fontWeight: 600, color: 'var(--text-secondary)' }}>
          {translate(locale, 'cron.card.title')}
        </span>
        <span
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            padding: '1px 8px',
            borderRadius: '999px',
            background: 'var(--bg-tertiary)',
            color: 'var(--success)',
            fontSize: '10px',
            fontWeight: 600,
            letterSpacing: '0.02em'
          }}
        >
          {translate(locale, 'cron.card.createdBadge')}
        </span>
        <span style={{ flex: 1 }} />
        {canOpen && (
          <ActionTooltip label={translate(locale, 'cron.card.viewInAutomations')} placement="top">
            <button
              type="button"
              onClick={openInAutomations}
              style={viewButtonStyle}
              aria-label={translate(locale, 'cron.card.viewInAutomations')}
            >
              {translate(locale, 'cron.card.view')}
            </button>
          </ActionTooltip>
        )}
      </div>

      <div
        style={{
          color: 'var(--text-primary)',
          fontSize: '14px',
          fontWeight: 600,
          lineHeight: 1.3,
          wordBreak: 'break-word'
        }}
      >
        {title}
      </div>

      <div
        style={{
          color: 'var(--text-secondary)',
          fontSize: '12px',
          lineHeight: 1.4
        }}
      >
        {schedulePhrase}
      </div>

      {(nextRun.absolute || nextRun.relative) && (
        <div
          style={{
            color: 'var(--text-dimmed)',
            fontSize: '11px',
            lineHeight: 1.4,
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            flexWrap: 'wrap'
          }}
        >
          <span>{translate(locale, 'cron.card.scheduledForPrefix')}</span>
          <span style={{ color: 'var(--text-secondary)' }}>{nextRun.absolute}</span>
          {nextRun.relative && <span>· {nextRun.relative}</span>}
        </div>
      )}
    </div>
  )
}

const viewButtonStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '999px',
  padding: '2px 10px',
  background: 'var(--bg-primary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  fontSize: '11px',
  lineHeight: 1.3
}
