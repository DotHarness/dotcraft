import { Archive, LoaderCircle, MoreHorizontal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { AppLocale } from '../../../shared/locales/types'
import type { AutomationRun } from '../../types/automation'
import { openAutomationRun } from '../../stores/automationRunNavigation'
import { formatRelativeTime } from '../../utils/relativeTime'
import { Button } from '../ui/Button'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { runThreadBusy, type RunThread } from './useAutomationRunThreads'

export function AutomationRunRow({ run, thread, automationName, locale, pending, onMenu, onRestore }: {
  run: AutomationRun; thread?: RunThread; automationName: string; locale: AppLocale; pending: boolean
  onMenu(position: ContextMenuPosition): void; onRestore(): void
}): JSX.Element {
  const t = useT()
  const archived = thread?.status === 'archived'
  const running = run.status === 'running' || runThreadBusy(thread)
  const unread = !run.readAt
  const title = thread?.displayName || automationName
  const canOpen = !!thread && !!run.threadId && !!run.turnId && !archived
  const detail = [t(`automation.run.${run.status}`), t(unread ? 'automation.unread' : 'automation.read'),
    archived ? t('automation.archivedHint') : null, run.error,
    run.deliveryStatus === 'failed' ? `${t('automation.deliveryFailed')}${run.deliveryError ? `: ${run.deliveryError}` : ''}` : null].filter(Boolean).join(' · ')
  return <article role="listitem" className="dc-automation-run" data-archived={archived} data-unread={unread} data-running={running}
    title={archived ? t('automation.archivedHint') : undefined}
    onContextMenu={event => { event.preventDefault(); onMenu({ x: event.clientX, y: event.clientY }) }}
    onKeyDown={event => { if (event.key === 'ContextMenu' || (event.shiftKey && event.key === 'F10')) {
      event.preventDefault(); const rect = event.currentTarget.getBoundingClientRect(); onMenu({ x: rect.right, y: rect.bottom })
    } }}>
    <ActionTooltip label={detail}><span className="dc-automation-run-status" aria-label={detail}>
      {running ? <LoaderCircle size={14} className="animate-spin-custom" aria-hidden /> : archived && !unread ? <Archive size={14} aria-hidden /> : null}
    </span></ActionTooltip>
    <button type="button" className="dc-automation-run-main" aria-disabled={!canOpen} aria-label={`${title}, ${detail}`}
      onClick={() => { if (canOpen) openAutomationRun(run) }}>
      <span className="dc-automation-run-identity"><span className="dc-automation-run-title">{title}</span>
        {run.worktree && <span className="dc-automation-run-meta">{run.worktree.branchName}</span>}</span>
      <time dateTime={run.createdAt} title={new Date(run.createdAt).toLocaleString(locale)}>{formatRelativeTime(run.createdAt, new Date(), locale)}</time>
    </button>
    {archived && <Button variant="ghost" size="sm" className="dc-automation-run-restore" disabled={pending} onClick={onRestore}>{t('automation.unarchive')}</Button>}
    <Button variant="ghost" size="iconSm" className="dc-automation-run-touch-menu" disabled={pending} aria-label={t('automation.historyActions')}
      onClick={event => { const rect = event.currentTarget.getBoundingClientRect(); onMenu({ x: rect.right, y: rect.bottom }) }}><MoreHorizontal size={15} /></Button>
  </article>
}
