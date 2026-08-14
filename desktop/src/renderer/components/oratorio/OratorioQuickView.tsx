import { useState, type ReactNode } from 'react'
import {
  Archive,
  Check,
  CircleDot,
  ExternalLink,
  FileText,
  Folder,
  GitBranch,
  GitPullRequest,
  Maximize2,
  MessageSquare,
  Play,
  RefreshCw,
  RotateCcw,
  Send,
  X,
  XCircle,
} from 'lucide-react'
import { addToast } from '../../stores/toastStore'
import { Button, IconButton, Skeleton, Textarea } from './ui'
import { latestFormalResult, sourceSummary } from './oratorio-quick-view'
import { useOratorioQuickViewT } from './oratorio-quick-view-i18n'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { deriveQuickActionGroups, type QuickActionId } from './oratorio-workflow'

export function OratorioQuickView({
  task,
  loading = false,
  onClose,
  onOpenDetail,
  onOpenThread,
  onTaskChange,
  onAction,
}: {
  task: OratorioTask
  loading?: boolean
  onClose: () => void
  onOpenDetail: (stage?: TaskStage, options?: { focus?: 'discussion' }) => void
  onOpenThread: () => void
  onTaskChange: (task: OratorioTask) => void
  onAction?: (action: QuickActionId, note?: string) => Promise<OratorioTask>
}) {
  const t = useOratorioQuickViewT()
  const [busyAction, setBusyAction] = useState<QuickActionId | null>(null)
  const [requestChangesOpen, setRequestChangesOpen] = useState(false)
  const [requestChanges, setRequestChanges] = useState('')
  const [rejectOpen, setRejectOpen] = useState(false)
  const [rejectNote, setRejectNote] = useState('')
  const [cancelRunOpen, setCancelRunOpen] = useState(false)
  const [cancelRunNote, setCancelRunNote] = useState('')
  const groups = deriveQuickActionGroups(task)
  const summary = task.detail ? sourceSummary(task.description) : null
  const result = latestFormalResult(task.detail)
  const kindLabel = task.kind === 'Task' ? t('localTask') : task.kind

  function navigateForAction(action: QuickActionId): boolean {
    if (action === 'open-thread') {
      onOpenThread()
      return true
    }
    if (action === 'review-draft' || action === 'review-delivery' || action === 'review-follow-ups') {
      onOpenDetail('review')
      return true
    }
    if (action === 'request-changes') {
      setRequestChangesOpen((current) => !current)
      setRejectOpen(false)
      setCancelRunOpen(false)
      return true
    }
    if (action === 'reject') {
      setRejectOpen((current) => !current)
      setRequestChangesOpen(false)
      setCancelRunOpen(false)
      return true
    }
    if (action === 'cancel-run') {
      setCancelRunOpen((current) => !current)
      setRequestChangesOpen(false)
      setRejectOpen(false)
      return true
    }
    return false
  }

  function runAction(action: QuickActionId, note?: string, confirmed = false): void {
    if (busyAction !== null) return
    if (!confirmed && navigateForAction(action)) return
    if (!onAction) {
      addToast(t('actionFailed'), 'error')
      return
    }
    setBusyAction(action)
    void onAction(action, note).then((next) => {
      setRequestChangesOpen(false)
      setRejectOpen(false)
      setCancelRunOpen(false)
      setRequestChanges('')
      setRejectNote('')
      setCancelRunNote('')
      onTaskChange(next)
      addToast(actionMessageFor(action), 'success')
    }).catch((error) => {
      addToast(error instanceof Error ? error.message : t('actionFailed'), 'error')
    }).finally(() => setBusyAction(null))
  }

  return (
    <aside className="ora-quick" aria-label={`${kindLabel} ${task.sourceLabel}: ${task.title} · ${task.shortId}`}>
      <header>
        <h2 title={task.title}>{task.title}</h2>
        <span className="ora-quick__header-actions">
          <IconButton icon={<Maximize2 size={15} />} label="Full details" tooltipLabel="Full details" onClick={() => onOpenDetail()} />
          <IconButton icon={<X size={15} />} label="Close quick view" onClick={onClose} />
        </span>
      </header>
      <div className="ora-quick__body dc-scrollbar-stable">
        <div className="ora-quick__identity" aria-label={t('taskIdentity')}>
          <span className="ora-chip"><ProviderIcon task={task} />{task.repository}</span>
          <span className="ora-chip"><KindIcon task={task} />{kindLabel}{task.provider === 'local' ? '' : ` ${task.sourceLabel}`}</span>
        </div>

        {task.branch ? <div className="ora-quick__branch"><GitBranch size={14} aria-hidden="true" /><span>{task.branch}</span></div> : null}

        <section className="ora-quick__section ora-quick__summary" aria-labelledby="ora-quick-summary-title">
          <h3 id="ora-quick-summary-title">{t('summary')}</h3>
          {loading ? <QuickViewTextSkeleton /> : summary ? <p>{summary}</p> : <p className="ora-quick__empty">{t('noSummary')}</p>}
        </section>

        {loading ? (
          <section className="ora-quick__section" aria-label={t('loadingResult')}><QuickViewTextSkeleton compact /></section>
        ) : result ? (
          <section className="ora-quick__section ora-quick__result" aria-labelledby="ora-quick-result-title">
            <h3 id="ora-quick-result-title">{t('result')}</h3>
            <p>{result.summary}</p>
          </section>
        ) : null}

        {groups.map((group) => (
          <section className="ora-quick__section ora-quick-action" data-kind={group.kind} key={`${group.kind}-${group.title}`}>
            <header><h3>{group.title}</h3><p>{group.description}</p></header>
            <div>{group.actions.map((action) => <QuickActionButton key={action} action={action} busy={busyAction === action} disabled={loading || busyAction !== null || (group.kind === 'decision' && !task.capabilities.decide)} onClick={() => runAction(action)} />)}</div>
            {group.kind === 'decision' && requestChangesOpen ? (
              <form className="ora-quick__inline-form" onSubmit={(event) => { event.preventDefault(); if (requestChanges.trim()) runAction('request-changes', requestChanges.trim(), true) }}>
                <label htmlFor="ora-quick-request-changes">{t('feedbackForDotCraft')}</label>
                <Textarea id="ora-quick-request-changes" autoFocus value={requestChanges} onChange={(event) => setRequestChanges(event.target.value)} placeholder="What should the Agent change?" aria-label="Request changes feedback" />
                <div><Button type="button" variant="ghost" size="sm" disabled={busyAction !== null} onClick={() => { setRequestChanges(''); setRequestChangesOpen(false) }}>Cancel</Button><Button type="submit" variant="primary" size="sm" iconLeft={<Send size={13} />} loading={busyAction === 'request-changes'} disabled={busyAction !== null || !requestChanges.trim()}>Send feedback</Button></div>
              </form>
            ) : null}
            {group.kind === 'decision' && rejectOpen ? (
              <form className="ora-quick__inline-form ora-quick__inline-form--danger" onSubmit={(event) => { event.preventDefault(); runAction('reject', rejectNote.trim(), true) }}>
                <strong>Reject this result?</strong>
                <Textarea autoFocus value={rejectNote} onChange={(event) => setRejectNote(event.target.value)} placeholder="Optional reason" aria-label="Reject note" />
                <div><Button type="button" variant="ghost" size="sm" disabled={busyAction !== null} onClick={() => { setRejectNote(''); setRejectOpen(false) }}>Cancel</Button><Button type="submit" variant="danger" size="sm" loading={busyAction === 'reject'} disabled={busyAction !== null}>Reject</Button></div>
              </form>
            ) : null}
            {cancelRunOpen && group.actions.includes('cancel-run') ? (
              <form className="ora-quick__inline-form ora-quick__inline-form--danger" onSubmit={(event) => { event.preventDefault(); runAction('cancel-run', cancelRunNote.trim(), true) }}>
                <strong>Cancel this run?</strong>
                <Textarea autoFocus value={cancelRunNote} onChange={(event) => setCancelRunNote(event.target.value)} placeholder="Optional reason" aria-label="Cancellation reason" />
                <div><Button type="button" variant="ghost" size="sm" disabled={busyAction !== null} onClick={() => { setCancelRunNote(''); setCancelRunOpen(false) }}>Keep running</Button><Button type="submit" variant="danger" size="sm" loading={busyAction === 'cancel-run'} disabled={busyAction !== null}>Cancel run</Button></div>
              </form>
            ) : null}
          </section>
        ))}
      </div>
    </aside>
  )
}

function QuickViewTextSkeleton({ compact = false }: { compact?: boolean }) {
  return <div className="ora-quick__text-skeleton" aria-hidden="true"><Skeleton height={12} width={compact ? '72%' : '100%'} /><Skeleton height={12} width={compact ? '46%' : '78%'} /></div>
}

function QuickActionButton({ action, busy, disabled, onClick }: { action: QuickActionId; busy: boolean; disabled: boolean; onClick: () => void }) {
  const presentation = actionPresentation(action)
  return <Button variant={presentation.variant} size="sm" iconLeft={presentation.icon} disabled={disabled} onClick={onClick}>{busy ? 'Working…' : presentation.label}</Button>
}

function actionPresentation(action: QuickActionId): { label: string; icon: ReactNode; variant: 'primary' | 'secondary' | 'ghost' | 'danger' } {
  const map: Record<QuickActionId, { label: string; icon: ReactNode; variant: 'primary' | 'secondary' | 'ghost' | 'danger' }> = {
    dispatch: { label: 'Dispatch', icon: <Play size={14} />, variant: 'primary' },
    implement: { label: 'Implement', icon: <GitBranch size={14} />, variant: 'primary' },
    'auto-target': { label: 'Auto PR / MR', icon: <GitPullRequest size={14} />, variant: 'secondary' },
    'review-only': { label: 'Review only', icon: <Play size={14} />, variant: 'secondary' },
    'open-thread': { label: 'Open thread', icon: <ExternalLink size={14} />, variant: 'secondary' },
    'cancel-run': { label: 'Cancel run', icon: <XCircle size={14} />, variant: 'danger' },
    retry: { label: 'Retry', icon: <RefreshCw size={14} />, variant: 'primary' },
    'review-draft': { label: 'Review draft', icon: <FileText size={14} />, variant: 'primary' },
    'review-delivery': { label: 'Review delivery', icon: <GitPullRequest size={14} />, variant: 'primary' },
    'review-follow-ups': { label: 'Review follow-ups', icon: <CircleDot size={14} />, variant: 'primary' },
    approve: { label: 'Approve', icon: <Check size={14} />, variant: 'primary' },
    'request-changes': { label: 'Request changes', icon: <MessageSquare size={14} />, variant: 'secondary' },
    reject: { label: 'Reject…', icon: <XCircle size={14} />, variant: 'danger' },
    're-review': { label: 'Review new revision', icon: <RefreshCw size={14} />, variant: 'primary' },
    archive: { label: 'Archive', icon: <Archive size={14} />, variant: 'secondary' },
    reopen: { label: 'Reopen', icon: <RotateCcw size={14} />, variant: 'primary' },
  }
  return map[action]
}

function actionMessageFor(action: QuickActionId): string {
  if (action === 'approve') return 'Task approved'
  if (action === 'reject') return 'Task rejected'
  if (action === 'request-changes') return 'Changes requested'
  if (action === 'archive') return 'Task archived'
  if (action === 'reopen') return 'Task reopened'
  if (action === 'cancel-run') return 'Run cancelled'
  if (action === 'retry') return 'Retry queued'
  return 'Agent work queued'
}

function ProviderIcon({ task }: { task: OratorioTask }) {
  if (task.provider === 'github') return <GithubGlyph />
  if (task.provider === 'gitlab') return <GitlabGlyph />
  return <Folder size={14} />
}

function KindIcon({ task }: { task: OratorioTask }) {
  if (task.kind === 'Pull request') return <GitPullRequest size={13} />
  if (task.kind === 'Issue') return <CircleDot size={13} />
  return <FileText size={13} />
}
