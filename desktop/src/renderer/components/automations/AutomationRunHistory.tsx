import { Archive, ArchiveRestore, Check, Circle, GitCompareArrows, MoreHorizontal } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { openAutomationRun } from '../../stores/automationRunNavigation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import type { AutomationRun } from '../../types/automation'
import { Button } from '../ui/Button'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { AutomationRunRow } from './AutomationRunRow'
import { runThreadBusy, useAutomationRunThreads, type RunThread } from './useAutomationRunThreads'

const emptyRuns: AutomationRun[] = []

export function AutomationRunHistory({ automationId, automationName }: { automationId: string; automationName: string }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const runs = useAutomationsStore(state => state.runs[automationId] ?? emptyRuns)
  const { threads, refresh, error: threadError } = useAutomationRunThreads(runs)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const locked = useRef(false)
  const [menu, setMenu] = useState<{ position: ContextMenuPosition; run?: AutomationRun } | null>(null)
  const [archiveIds, setArchiveIds] = useState<string[] | null>(null)
  useEffect(() => {
    let active = true
    setError(null)
    void useAutomationsStore.getState().fetchRuns(automationId).catch(failure => { if (active) setError(String(failure)) })
    return () => { active = false }
  }, [automationId])
  const archiveable = (run: AutomationRun): boolean => !!run.threadId && !!threads[run.threadId]
    && threads[run.threadId].status !== 'archived' && !runThreadBusy(threads[run.threadId])
    && !['queued', 'running'].includes(run.status)
    && !runs.some(other => other.threadId === run.threadId && ['queued', 'running'].includes(other.status))
  const targets = [...new Set(runs.filter(archiveable).map(run => run.threadId!))]
  const unreadIds = runs.filter(run => !run.readAt).map(run => run.id)
  async function action(operation: () => Promise<void>): Promise<void> {
    if (locked.current) return
    locked.current = true; setPending(true); setError(null); setMenu(null)
    try { await operation() } catch (failure) { setError(String(failure)) }
    finally { locked.current = false; setPending(false) }
  }
  async function archive(ids: string[]): Promise<void> {
    let failed = 0
    for (const threadId of ids) {
      try {
        const { thread } = await window.api.appServer.sendRequest('thread/read', { threadId }) as unknown as { thread: RunThread }
        if (runThreadBusy(thread)) throw new Error(t('automation.run.running'))
        await window.api.appServer.sendRequest('thread/archive', { threadId })
        useThreadStore.getState().removeThreadTree(threadId)
      } catch { failed++ }
    }
    await refresh()
    if (failed) throw new Error(t('automation.archivePartial', { succeeded: ids.length - failed, failed }))
  }
  const restore = (threadId: string): void => { void action(async () => {
    await window.api.appServer.sendRequest('thread/unarchive', { threadId })
    const current = await refresh()
    if (current[threadId]) useThreadStore.getState().upsertThreads([current[threadId]])
  }) }
  const mark = (ids: string[], read: boolean): void => { void action(() => useAutomationsStore.getState().markRunsRead(automationId, ids, read)) }
  const selected = menu?.run
  const selectedArchived = !!selected?.threadId && threads[selected.threadId]?.status === 'archived'
  return <section className="dc-automation-section">
    <div className="dc-automation-history-heading"><h3>{t('automation.history')}</h3>
      {!!runs.length && <Button variant="ghost" size="iconSm" aria-label={t('automation.historyActions')} disabled={pending}
        onClick={event => { const rect = event.currentTarget.getBoundingClientRect(); setMenu({ position: { x: rect.right, y: rect.bottom } }) }}><MoreHorizontal size={16} /></Button>}
    </div>
    {error || threadError ? <p role="alert" className="dc-automation-error">{error ?? threadError}</p> : null}
    {runs.length === 0 ? <p className="dc-automation-hint">{t('automation.noRuns')}</p> :
      <div className="dc-automation-run-list" role="list">
        {runs.map(run => <AutomationRunRow key={run.id} run={run} thread={run.threadId ? threads[run.threadId] : undefined}
          automationName={automationName} locale={locale} pending={pending}
          onMenu={position => setMenu({ position, run })} onRestore={() => run.threadId && restore(run.threadId)} />)}
      </div>}
    {menu && <ContextMenu position={menu.position} onClose={() => setMenu(null)} items={selected ? [
      { label: t(selected.readAt ? 'automation.markUnread' : 'automation.markRead'), icon: selected.readAt ? <Circle size={15} /> : <Check size={15} />,
        disabled: pending, onClick: () => mark([selected.id], !selected.readAt) },
      ...(selectedArchived ? [{ label: t('automation.unarchive'), icon: <ArchiveRestore size={15} />, disabled: pending,
        onClick: () => restore(selected.threadId!) }] : [{ label: t('automation.archive'), icon: <Archive size={15} />,
        disabled: pending || !archiveable(selected), onClick: () => { setMenu(null); setArchiveIds([selected.threadId!]) } }]),
      ...(selected.worktree ? [{ label: t('automation.reviewChanges'), icon: <GitCompareArrows size={15} />,
        disabled: pending || selectedArchived || !selected.threadId || !selected.turnId || !threads[selected.threadId],
        onClick: () => { setMenu(null); openAutomationRun(selected); useUIStore.getState().setActiveDetailTab('changes') } }] : [])
    ] : [
      { label: t('automation.markAllRead'), icon: <Check size={15} />, disabled: pending || !unreadIds.length, onClick: () => mark(unreadIds, true) },
      { label: t('automation.archiveAll'), icon: <Archive size={15} />, disabled: pending || !targets.length,
        onClick: () => { setMenu(null); setArchiveIds(targets) } }
    ]} />}
    {archiveIds && <ConfirmDialog title={t('automation.archive')} message={t('automation.archiveConfirm', { count: archiveIds.length })}
      confirmLabel={t('automation.archive')} cancelLabel={t('common.cancel')}
      onCancel={() => setArchiveIds(null)} onConfirm={() => { const ids = archiveIds; setArchiveIds(null); void action(() => archive(ids)) }} />}
  </section>
}
