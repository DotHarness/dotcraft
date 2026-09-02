import { useMemo, useState, type JSX } from 'react'
import { CalendarClock } from 'lucide-react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { parseCronCreatedResult } from '../../utils/cronToolDisplay'
import { formatNextRun } from '../../utils/cronNextRunDisplay'
import { useUIStore } from '../../stores/uiStore'
import { useCronStore } from '../../stores/cronStore'
import { ToolDisclosure } from './ToolDisclosure'

interface CronCreatedCardProps {
  item: ConversationItem
  locale: AppLocale
}

export function CronCreatedCard({ item, locale }: CronCreatedCardProps): JSX.Element | null {
  const parsed = useMemo(() => parseCronCreatedResult(item.result, locale), [item.result, locale])
  const [expanded, setExpanded] = useState(false)

  if (!parsed) return null

  const name = parsed.jobName ?? parsed.message ?? translate(locale, 'cron.card.nameFallback')
  const nextRun = formatNextRun(parsed.nextRunAtMs ?? null, true, locale)
  const hasNextRun = !!(nextRun.absolute || nextRun.relative)

  return (
    <ToolDisclosure
      expanded={expanded}
      onToggle={() => setExpanded((value) => !value)}
      expandable={!!parsed.schedulePhrase || hasNextRun}
      title={(
        <>
          {translate(locale, 'cron.card.createdBadge')}
          {' '}
          <CronRef name={name} jobId={parsed.jobId ?? null} locale={locale} />
        </>
      )}
    >
      <div className="dc-cron-detail">
        {parsed.schedulePhrase ? <span>{parsed.schedulePhrase}</span> : null}
        {hasNextRun ? (
          <span>
            {translate(locale, 'cron.card.scheduledForPrefix')}{' '}
            <span className="dc-cron-detail__value">{nextRun.absolute}</span>
            {nextRun.relative ? ` · ${nextRun.relative}` : ''}
          </span>
        ) : null}
      </div>
    </ToolDisclosure>
  )
}

function CronRef({
  name,
  jobId,
  locale
}: {
  name: string
  jobId: string | null
  locale: AppLocale
}): JSX.Element {
  const openInAutomations = (): void => {
    const ui = useUIStore.getState()
    ui.setActiveMainView('automations')
    ui.setAutomationsTab('cron')
    if (jobId) useCronStore.getState().selectCronJob(jobId)
  }

  return (
    <button
      type="button"
      className="dc-ref dc-ref-cron"
      aria-label={translate(locale, 'cron.card.viewInAutomations')}
      // The chip sits inside a `<summary>`, whose click would otherwise toggle the row.
      onClick={(event) => {
        event.preventDefault()
        event.stopPropagation()
        openInAutomations()
      }}
    >
      <CalendarClock size={12} strokeWidth={2.25} aria-hidden />
      <span>{name}</span>
    </button>
  )
}
