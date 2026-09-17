import { useState, type JSX } from 'react'
import type { LoadState, UsageRange, UsageThreadWire } from '../../../stores/usageStore'
import { useThreadStore } from '../../../stores/threadStore'
import { useUIStore } from '../../../stores/uiStore'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { Button } from '../../ui/Button'
import { Skeleton } from '../../ui/Skeleton'
import { SettingsGroup } from '../SettingsGroup'
import { ChartEmpty, ChartError, RangeToggle, SECTION_STYLE } from './UsageChartParts'
import { usageKeyLabel, type TFn } from './usageLabels'
import styles from './usage.module.css'

const COLLAPSED_ROWS = 5

interface TopThreadsTableProps {
  state: LoadState<UsageThreadWire[]>
  range: UsageRange
  onRangeChange: (range: UsageRange) => void
  formatDateTime: (iso: string) => string
  onRetry: () => void
  t: TFn
}

function openThread(threadId: string): void {
  useUIStore.getState().setActiveMainView('conversation')
  useThreadStore.getState().setActiveThreadId(threadId)
}

export function TopThreadsTable({ state, range, onRangeChange, formatDateTime, onRetry, t }: TopThreadsTableProps): JSX.Element {
  const [expanded, setExpanded] = useState(false)
  const rows = state.data ?? []
  const visible = expanded ? rows : rows.slice(0, COLLAPSED_ROWS)

  let body: JSX.Element
  if (state.error && !state.data) {
    body = <ChartError message={t('settings.usage.loadError')} retryLabel={t('settings.usage.retry')} onRetry={onRetry} />
  } else if (!state.data) {
    body = (
      <div className={styles.tableLoading}>
        <Skeleton height={120} radius={8} />
      </div>
    )
  } else if (rows.length === 0) {
    body = <ChartEmpty message={t('settings.usage.threads.empty')} />
  } else {
    body = (
      <>
        <table className={styles.table}>
          <thead>
            <tr>
              <th className={styles.threadCell}>{t('settings.usage.threads.col.thread')}</th>
              <th>{t('settings.usage.threads.col.surface')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.turns')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.tokens')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.cacheHit')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.lastActive')}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((thread) => (
              <tr key={thread.threadId}>
                <td className={styles.threadCell}>
                  <button
                    type="button"
                    className={styles.threadButton}
                    title={thread.title ?? thread.threadId}
                    onClick={() => openThread(thread.threadId)}
                  >
                    {thread.title?.trim() || t('settings.usage.threads.untitled')}
                  </button>
                </td>
                <td className={styles.threadMeta}>{usageKeyLabel(t, 'surface', thread.originChannel)}</td>
                <td className={styles.numeric}>{thread.turns.toLocaleString()}</td>
                <td className={styles.numeric} title={thread.totalTokens.toLocaleString()}>
                  {formatCompactCount(thread.totalTokens)}
                </td>
                <td className={styles.numeric}>{`${(thread.cacheHitRate * 100).toFixed(0)}%`}</td>
                <td className={`${styles.numeric} ${styles.threadMeta}`}>{formatDateTime(thread.lastActiveAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {rows.length > COLLAPSED_ROWS && (
          <div className={styles.tableFooter}>
            <Button variant="ghost" onClick={() => setExpanded((value) => !value)}>
              {expanded ? t('settings.usage.threads.seeLess') : t('settings.usage.threads.seeMore')}
            </Button>
          </div>
        )}
      </>
    )
  }

  return (
    <SettingsGroup
      title={t('settings.usage.threads.title')}
      description={t('settings.usage.threads.hint')}
      headerAction={<RangeToggle value={range} onChange={onRangeChange} t={t} />}
      framed={false}
      style={SECTION_STYLE}
    >
      <div className={styles.frame}>{body}</div>
    </SettingsGroup>
  )
}
