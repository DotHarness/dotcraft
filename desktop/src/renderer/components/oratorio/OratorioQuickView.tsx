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
import { Button, IconButton, Textarea } from './ui'
import { GithubGlyph, GitlabGlyph } from './ProviderGlyphs'
import type { OratorioTask, TaskStage } from './oratorio-model'
import { deriveQuickActionGroups, type QuickActionId } from './oratorio-workflow'

export function OratorioQuickView({
  task,
  onClose,
  onOpenDetail,
  onOpenThread,
  onTaskChange,
  onAction,
}: {
  task: OratorioTask
  onClose: () => void
  onOpenDetail: (stage?: TaskStage, options?: { focus?: 'discussion' }) => void
  onOpenThread: () => void
  onTaskChange: (task: OratorioTask) => void
  onAction?: (action: QuickActionId, note?: string) => Promise<OratorioTask>
}) {
  const [busyAction, setBusyAction] = useState<QuickActionId | null>(null)
  const [requestChangesOpen, setRequestChangesOpen] = useState(false)
  const [requestChanges, setRequestChanges] = useState('')
  const [rejectOpen, setRejectOpen] = useState(false)
  const [rejectNote, setRejectNote] = useState('')
  const [cancelRunOpen, setCancelRunOpen] = useState(false)
  const [cancelRunNote, setCancelRunNote] = useState('')
  const [undoTask, setUndoTask] = useState<OratorioTask | null>(null)
  const [actionMessage, setActionMessage] = useState<string | null>(null)
  const [failedAction, setFailedAction] = useState<{ action: QuickActionId; note?: string } | null>(null)
  const groups = deriveQuickActionGroups(task)
  const totalDrafts = task.artifacts.reviewDrafts + task.artifacts.implementationDrafts + task.artifacts.followUpDrafts

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
    if (!onAction) { setFailedAction({ action, note }); return }
    setBusyAction(action)
    setFailedAction(null)
    void onAction(action, note).then((next) => {
      setUndoTask(null)
      setActionMessage(actionMessageFor(action))
      setRequestChangesOpen(false)
      setRejectOpen(false)
      setCancelRunOpen(false)
      setRequestChanges('')
      setRejectNote('')
      setCancelRunNote('')
      onTaskChange(next)
    }).catch(() => setFailedAction({ action, note })).finally(() => setBusyAction(null))
  }

  return (
    <aside className="ora-quick" aria-label="Task quick view">
      <header>
        <span><small>Quick view</small><strong>{task.shortId}</strong></span>
        <span className="ora-quick__header-actions">
          <IconButton icon={<Maximize2 size={15} />} label="Full details" tooltipLabel="Full details" onClick={() => onOpenDetail()} />
          <IconButton icon={<X size={15} />} label="Close quick view" onClick={onClose} />
        </span>
      </header>
      <div className="ora-quick__body">
        <div className="ora-quick__identity"><span className="ora-chip"><ProviderIcon task={task} />{task.repository}</span><span className="ora-chip"><KindIcon task={task} />{task.kind}</span></div>
        <h2>{task.title}</h2>
        <p>{task.description}</p>

        {task.run ? <div className="ora-quick__run"><span className="ora-state-dot" data-tone={task.state === 'failed' ? 'error' : task.state === 'running' || task.state === 'dispatching' ? 'info' : 'success'} /><span><strong>{task.run.activity}</strong><small>Attempt {task.run.attempt} · {task.run.status.replace('-', ' ')}</small></span></div> : null}

        <div className="ora-quick__actions" aria-label="Available task actions">
          {groups.map((group) => (
            <section className="ora-quick-action" data-kind={group.kind} key={`${group.kind}-${group.title}`}>
              <header><strong>{group.title}</strong><small>{group.description}</small></header>
              <div>{group.actions.map((action) => <QuickActionButton key={action} action={action} busy={busyAction === action} disabled={busyAction !== null || (group.kind === 'decision' && !task.capabilities.decide)} onClick={() => runAction(action)} />)}</div>
              {group.kind === 'decision' && requestChangesOpen ? (
                <form className="ora-quick__inline-form" onSubmit={(event) => { event.preventDefault(); if (requestChanges.trim()) runAction('request-changes', requestChanges.trim(), true) }}>
                  <Textarea autoFocus value={requestChanges} onChange={(event) => setRequestChanges(event.target.value)} placeholder="What should the Agent change?" aria-label="Request changes feedback" />
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

        {failedAction ? <div className="ora-quick__action-error" role="alert"><span><strong>Action failed</strong><small>The managed service did not accept this action. Your task state was not changed.</small></span><Button variant="secondary" size="sm" iconLeft={<RefreshCw size={13} />} onClick={() => runAction(failedAction.action, failedAction.note, true)}>Retry</Button></div> : null}

        {actionMessage && undoTask ? <div className="ora-quick__undo" role="status"><span>{actionMessage}</span><Button variant="ghost" size="sm" iconLeft={<RotateCcw size={13} />} onClick={() => { onTaskChange(undoTask); setUndoTask(null); setActionMessage(null) }}>Undo</Button></div> : null}

        <div className="ora-quick__counts" aria-label="Task artifacts">
          <button type="button" onClick={() => onOpenDetail('review')}><strong>{totalDrafts}</strong><span>Drafts</span></button>
          <button type="button" aria-label={`Open discussion · ${task.artifacts.comments} comments`} onClick={() => onOpenDetail('review', { focus: 'discussion' })}><strong>{task.artifacts.comments}</strong><span>Comments</span></button>
          <button type="button" onClick={() => onOpenDetail('decision')}><strong>{task.artifacts.writes}</strong><span>Writes</span></button>
        </div>
        <dl>
          <div><dt>State</dt><dd>{task.state.replace('-', ' ')}</dd></div>
          <div><dt>Assignee</dt><dd>{task.assignee ?? 'Unassigned'}</dd></div>
          {task.branch ? <div><dt>Branch</dt><dd>{task.branch}</dd></div> : null}
          <div><dt>Round</dt><dd>{task.round ?? 1}</dd></div>
          <div><dt>Updated</dt><dd>{task.updated}</dd></div>
        </dl>
      </div>
    </aside>
  )
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
