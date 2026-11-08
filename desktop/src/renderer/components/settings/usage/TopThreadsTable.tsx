import { useState, type JSX } from 'react'
import { useLocale } from '../../../contexts/LocaleContext'
import type { LoadState, UsageRange, UsageThreadWire } from '../../../stores/usageStore'
import { useWorkspaceProjectsStore } from '../../../stores/workspaceProjectsStore'
import { isDefaultChatWorkspacePathCandidate } from '../../../../shared/defaultChatWorkspace'
import { sameWorkspaceProjectKey, normalizeWorkspaceProjectKey } from '../../../../shared/workspaceProjectKey'
import { openThread } from '../../../utils/desktopNavigation'
import { projectLabel } from '../../conversation/workspaceProjectMenu'
import { addToast } from '../../../stores/toastStore'
import { formatCompactCount } from '../../../utils/formatCompactCount'
import { formatUsd } from '../../../utils/formatUsd'
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

export function TopThreadsTable({ state, range, onRangeChange, formatDateTime, onRetry, t }: TopThreadsTableProps): JSX.Element {
  const locale = useLocale()
  const projects = useWorkspaceProjectsStore((state) => state.projects)
  const workspaceName = (path: string): string => {
    if (isDefaultChatWorkspacePathCandidate(path)) return t('chatsRail.title')
    const project = projects.find((item) => item.kind !== 'remote' && sameWorkspaceProjectKey(item.path, path))
    return project ? projectLabel(project) : path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || path
  }
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
              <th className={styles.numeric}>{t('settings.usage.threads.col.cost')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.cacheHit')}</th>
              <th className={styles.numeric}>{t('settings.usage.threads.col.lastActive')}</th>
            </tr>
          </thead>
          <tbody>
            {visible.map((thread) => (
              <tr key={`${normalizeWorkspaceProjectKey(thread.workspacePath)}:${thread.threadId}`}>
                <td className={styles.threadCell}>
                  <button
                    type="button"
                    className={styles.threadButton}
                    title={thread.title ?? thread.threadId}
                    onClick={() => void openThread(thread.threadId, thread.workspacePath, { includeArchived: thread.archived }).catch(() => {
                      addToast(t('settings.workspace.switchFailed', { name: workspaceName(thread.workspacePath) }), 'error')
                    })}
                  >
                    {thread.title?.trim() || t('settings.usage.threads.untitled')}
                  </button>
                  <span className={styles.threadWorkspace} title={thread.workspacePath}>
                    {workspaceName(thread.workspacePath)}
                  </span>
                </td>
                <td className={styles.threadMeta}>{usageKeyLabel(t, 'surface', thread.originChannel)}</td>
                <td className={styles.numeric}>{thread.turns.toLocaleString()}</td>
                <td className={styles.numeric} title={thread.totalTokens.toLocaleString()}>
                  {formatCompactCount(thread.totalTokens)}
                </td>
                <td className={`${styles.numeric} ${thread.costUsd == null ? styles.threadMeta : ''}`}>
                  {thread.costUsd == null ? '—' : formatUsd(thread.costUsd, locale)}
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
