import { useId, useState, type JSX } from 'react'
import type { ImportCompletedNotification, ImportOutcome } from '@dotcraft/sdk/contracts'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { DisclosureChevron } from '../../ui/DisclosureChevron'
import { StatusIndicator } from '../../ui/StatusIndicator'
import { ImportSourceIcon } from './ImportSourceIcon'
import {
  batchSources,
  batchTitle,
  groupByCategory,
  openOutcome,
  outcomeActionKey,
  outcomeDetail,
  reasonText,
  splitBatch,
  tally,
  tallySummary,
  tallyTone
} from './importOutcomes'
import styles from './ImportHistory.module.css'

interface ImportHistoryBatchProps {
  batch: ImportCompletedNotification
  expanded: boolean
  onToggle: () => void
}

export function ImportHistoryBatch({ batch, expanded, onToggle }: ImportHistoryBatchProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const bodyId = useId()
  const [openCategory, setOpenCategory] = useState<string | null>(null)
  const { items, passErrors } = splitBatch(batch)
  const sources = batchSources(items)
  const completedAt = new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(batch.completedAt))

  return (
    <div className={styles.card}>
      <button type="button" className={styles.disclosure} aria-expanded={expanded} aria-controls={bodyId} onClick={onToggle}>
        <ImportSourceIcon source={sources.length === 1 ? sources[0] : ''} />
        <span className={styles.text}>
          <span className={styles.title}>{batchTitle(batch, sources, locale, t)}</span>
          <span className={styles.meta}>{completedAt} · {tallySummary(tally(items), t)}</span>
        </span>
        <DisclosureChevron expanded={expanded} direction="reveal" />
      </button>
      {expanded && (
        <div id={bodyId}>
          {passErrors.map((outcome, index) => (
            <div key={`${outcome.sourceId}-${index}`} className={styles.item}>
              <span className={styles.detail}>
                <StatusIndicator tone="error" />
                <span>{reasonText(outcome, t)}</span>
              </span>
            </div>
          ))}
          {groupByCategory(items).map(({ category, outcomes }) => {
            const open = openCategory === category
            const listId = `${bodyId}-${category}`
            const summary = tally(outcomes)
            return (
              <div key={category} className={styles.section}>
                <button
                  type="button"
                  className={styles.disclosure}
                  data-level="category"
                  aria-expanded={open}
                  aria-controls={listId}
                  onClick={() => setOpenCategory(open ? null : category)}
                >
                  <span className={styles.text}>
                    <span className={styles.title}>{t(`settings.import.category.${category}`)}</span>
                  </span>
                  <span className={styles.status}>
                    <StatusIndicator tone={tallyTone(summary)} />
                    <span>{tallySummary(summary, t)}</span>
                  </span>
                  <DisclosureChevron expanded={open} direction="reveal" />
                </button>
                {open && (
                  <div id={listId} role="list" className={styles.list}>
                    {outcomes.map((outcome, index) => <HistoryItem key={`${outcome.sourceId}-${index}`} outcome={outcome} />)}
                  </div>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function HistoryItem({ outcome }: { outcome: ImportOutcome }): JSX.Element {
  const t = useT()
  const title = outcome.title ?? (outcome.category === 'sessions' ? t('settings.import.history.untitledChat') : outcome.sourceId)
  const detail = outcomeDetail(outcome, t)
  const action = outcomeActionKey(outcome)
  return (
    <div className={styles.item} role="listitem">
      <span className={styles.text}>
        <span className={styles.itemTitle}>{title}</span>
        {detail && (
          <span className={styles.detail}>
            {detail.tone && <StatusIndicator tone={detail.tone} />}
            <span>{detail.text}</span>
          </span>
        )}
      </span>
      {action && (
        <Button variant="ghost" aria-label={t('settings.import.action.named', { action: t(action), title })} onClick={() => openOutcome(outcome)}>
          {t(action)}
        </Button>
      )}
    </div>
  )
}
