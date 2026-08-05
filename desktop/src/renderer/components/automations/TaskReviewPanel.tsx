import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { ConversationTurn } from '../../types/conversation'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useReviewPanelStore } from '../../stores/reviewPanelStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import type { AutomationTask, AutomationWorktreeStatus } from '../../stores/automationsStore'
import type { Thread } from '../../types/thread'
import { StatusBadge } from './StatusBadge'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { AgentResponseBlock } from '../conversation/AgentResponseBlock'
import type { SubAgentEntry } from '../../types/toolCall'
import { ThreadPickerOverlay } from './ThreadPickerOverlay'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { WorktreeHandoffDialog } from '../conversation/WorktreeHandoffDialog'
import { ArrowRightLeft, ExternalLink, GitBranch, RefreshCcw, Trash2, X } from 'lucide-react'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'

function ApprovalPolicyBadge({
  policy,
  t
}: {
  policy?: string | null
  t: ReturnType<typeof useT>
}): JSX.Element {
  const fullAuto =
    policy === 'fullAuto' || policy === 'autoApprove'
  const label = fullAuto ? t('auto.review.fullAuto') : t('auto.review.workspaceScope')
  const title = fullAuto ? t('auto.review.policyFullAuto') : t('auto.review.policyWorkspace')
  return (
    <ActionTooltip label={title}>
      <span
        style={{
          display: 'inline-block',
          padding: '1px 6px',
          borderRadius: '8px',
          backgroundColor: 'var(--bg-tertiary)',
          color: fullAuto ? 'var(--accent)' : 'var(--text-secondary)',
          fontSize: '11px',
          fontWeight: 500,
          lineHeight: '16px'
        }}
      >
        {label}
      </span>
    </ActionTooltip>
  )
}

function WorktreeReviewSection({ task }: { task: AutomationTask }): JSX.Element | null {
  const t = useT()
  const getTaskWorktreeStatus = useAutomationsStore((s) => s.getTaskWorktreeStatus)
  const discardTaskWorktree = useAutomationsStore((s) => s.discardTaskWorktree)
  const fetchTasks = useAutomationsStore((s) => s.fetchTasks)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const upsertThreads = useThreadStore((s) => s.upsertThreads)
  const setActiveThreadId = useThreadStore((s) => s.setActiveThreadId)
  const setActiveThread = useThreadStore((s) => s.setActiveThread)
  const [status, setStatus] = useState<AutomationWorktreeStatus | null>(null)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmDiscard, setConfirmDiscard] = useState(false)
  const [handoffThread, setHandoffThread] = useState<Thread | null>(null)

  const isWorktreeTask =
    task.workspaceMode === 'worktree' && !task.threadBinding?.threadId
  const provisioned = Boolean(task.threadId && task.worktree)
  const hasUnreviewedWork =
    Boolean(status?.hasUncommittedChanges)
    || Boolean(status?.hasCommitsAheadOfBase)
    || (status?.aheadCount ?? 0) > 0

  useEffect(() => {
    if (!isWorktreeTask || !provisioned) {
      setStatus(null)
      setError(null)
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)
    setError(null)
    getTaskWorktreeStatus(task)
      .then((next) => {
        if (!cancelled) setStatus(next)
      })
      .catch((err: unknown) => {
        if (!cancelled) {
          setStatus(null)
          setError(err instanceof Error ? err.message : String(err))
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [
    getTaskWorktreeStatus,
    isWorktreeTask,
    provisioned,
    task.id,
    task.threadId,
    task.updatedAt,
    task.worktree?.path
  ])

  if (!isWorktreeTask) return null

  async function refresh(): Promise<void> {
    if (!provisioned) return
    setLoading(true)
    setError(null)
    try {
      setStatus(await getTaskWorktreeStatus(task))
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setLoading(false)
    }
  }

  async function readThread(): Promise<Thread | null> {
    if (!task.threadId) return null
    const result = (await window.api.appServer.sendRequest('thread/read', {
      threadId: task.threadId,
    })) as unknown as { thread?: Thread }
    return result.thread ?? null
  }

  async function openThread(): Promise<void> {
    try {
      const thread = await readThread()
      if (!thread) throw new Error(t('auto.review.threadMissing'))
      upsertThreads([thread])
      setActiveThread(thread)
      setActiveThreadId(thread.id)
      setActiveMainView('conversation')
    } catch (err: unknown) {
      addToast(
        t('auto.review.openThreadFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function startHandoff(): Promise<void> {
    if (task.status === 'running' || !provisioned) return
    try {
      const thread = await readThread()
      if (!thread?.worktree) throw new Error(t('auto.review.worktreeNotProvisioned'))
      setHandoffThread(thread)
    } catch (err: unknown) {
      addToast(
        t('auto.review.handoffFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    }
  }

  async function discard(): Promise<void> {
    setBusy(true)
    try {
      await discardTaskWorktree(task)
      setStatus(null)
      setConfirmDiscard(false)
      addToast(t('auto.review.discardSuccess'), 'success')
    } catch (err: unknown) {
      addToast(
        t('auto.review.discardFailed', {
          error: err instanceof Error ? err.message : String(err)
        }),
        'error'
      )
    } finally {
      setBusy(false)
    }
  }

  const branch = status?.branchName || task.worktree?.branchName || t('workspaceFooter.branchUnknown')
  const aheadCount = status?.aheadCount ?? 0
  const canHandoff = provisioned && task.status !== 'running' && status?.exists !== false
  const disableHandoffReason =
    task.status === 'running'
      ? t('auto.review.handoffDisabledRunning')
      : !provisioned || status?.exists === false
        ? t('auto.review.worktreeNotProvisioned')
        : undefined

  return (
    <>
      <div style={worktreeSectionStyle}>
        <div style={worktreeHeaderStyle}>
          <div style={worktreeTitleStyle}>
            <GitBranch size={14} aria-hidden />
            <span>{t('auto.review.worktreeHeading')}</span>
          </div>
          <IconButton
            label={t('auto.review.worktreeRefresh')}
            disabled={!provisioned || loading}
            onClick={() => void refresh()}
            size={28}
            icon={<RefreshCcw size={13} aria-hidden />}
          />
        </div>

        {!provisioned ? (
          <p style={worktreeMutedTextStyle}>{t('auto.review.worktreeNotProvisioned')}</p>
        ) : (
          <div style={{ display: 'grid', gap: '8px' }}>
            <div style={worktreeMetaRowStyle}>
              <span style={worktreeMetaLabelStyle}>{t('auto.review.worktreeBranch')}</span>
              <span style={worktreeBranchStyle}>{branch}</span>
            </div>
            <div style={worktreePillsStyle}>
              <span style={worktreePillStyle(status?.hasUncommittedChanges ? 'warning' : 'neutral')}>
                {status?.hasUncommittedChanges
                  ? t('auto.review.worktreeDirty')
                  : t('auto.review.worktreeClean')}
              </span>
              <span style={worktreePillStyle(aheadCount > 0 ? 'warning' : 'neutral')}>
                {aheadCount > 0
                  ? t('auto.review.worktreeAhead', { count: aheadCount })
                  : t('auto.review.worktreeNoAhead')}
              </span>
              {loading && <span style={worktreeMutedTextStyle}>{t('threadList.loading')}</span>}
            </div>
            {status?.exists === false && (
              <p style={worktreeMutedTextStyle}>{t('auto.review.worktreeNotProvisioned')}</p>
            )}
            {error && <p style={worktreeErrorStyle}>{error}</p>}
            <div style={worktreeActionsStyle}>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => void openThread()}
              >
                <ExternalLink size={13} aria-hidden />
                <span>{t('auto.review.openThread')}</span>
              </Button>
              <ActionTooltip label={t('auto.review.handoffToLocal')} disabledReason={disableHandoffReason}>
                <Button
                  size="sm"
                  variant="primary"
                  disabled={!canHandoff}
                  onClick={() => void startHandoff()}
                >
                  <ArrowRightLeft size={13} aria-hidden />
                  <span>{t('auto.review.handoffToLocal')}</span>
                </Button>
              </ActionTooltip>
              <Button
                size="sm"
                variant="danger"
                disabled={!provisioned || busy}
                onClick={() => {
                  if (hasUnreviewedWork) setConfirmDiscard(true)
                  else void discard()
                }}
                loading={busy}
              >
                <Trash2 size={13} aria-hidden />
                <span>{busy ? t('auto.deleting') : t('auto.review.discardWorktree')}</span>
              </Button>
            </div>
          </div>
        )}
      </div>

      {confirmDiscard && (
        <ConfirmDialog
          title={t('auto.review.discardConfirmTitle')}
          message={t('auto.review.discardConfirmMessage')}
          confirmLabel={busy ? t('auto.deleting') : t('auto.review.discardWorktree')}
          danger
          onConfirm={() => void discard()}
          onCancel={() => setConfirmDiscard(false)}
        />
      )}

      {handoffThread && (
        <WorktreeHandoffDialog
          mode="local"
          thread={handoffThread}
          baseRef={handoffThread.worktree?.baseRef ?? null}
          defaultBranchName={handoffThread.worktree?.branchName ?? branch}
          localWorkspacePath={handoffThread.workspacePath}
          onClose={() => setHandoffThread(null)}
          onComplete={async () => {
            setHandoffThread(null)
            setStatus(null)
            await fetchTasks({ silent: true })
          }}
          onBusyChange={setBusy}
        />
      )}
    </>
  )
}

function ReviewTurnBlock({
  turn,
  activeTurnId,
  turnStatus,
  streamingMessage,
  streamingMessageLastDeltaAt,
  streamingReasoning,
  activeItemId,
  subAgentEntriesOverride,
  isLastTurn
}: {
  turn: ConversationTurn
  activeTurnId: string | null
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  streamingMessage: string
  streamingMessageLastDeltaAt: number | null
  streamingReasoning: string
  activeItemId: string | null
  /** Scoped to the review thread; not the global conversation store. */
  subAgentEntriesOverride: SubAgentEntry[]
  isLastTurn: boolean
}): JSX.Element {
  const isRunning = turnStatus === 'running' && turn.id === activeTurnId

  // Orchestrator-submitted workflow prompt is modeled as userMessage; omit from automation review UI.
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
      <AgentResponseBlock
        turn={turn}
        streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
        streamingMessageLastDeltaAt={
          turn.id === activeTurnId ? streamingMessageLastDeltaAt : null
        }
        streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
        isRunning={isRunning}
        showIdleThinkingFallback={isRunning}
        isActiveTurn={turn.id === activeTurnId}
        activeItemIdOverride={isRunning ? activeItemId ?? null : undefined}
        subAgentEntriesOverride={subAgentEntriesOverride}
        shellRuntimeScope="review"
        isLastTurn={isLastTurn}
      />
    </div>
  )
}

/**
 * Side panel for automation task activity: history, live stream, and summary.
 */
export function TaskReviewPanel(): JSX.Element {
  const t = useT()
  const selectedTaskId = useAutomationsStore((s) => s.selectedTaskId)
  const tasks = useAutomationsStore((s) => s.tasks)
  const openReviewPanel = useReviewPanelStore((s) => s.openReviewPanel)
  const destroyReviewPanel = useReviewPanelStore((s) => s.destroyReviewPanel)
  const closeReviewPanel = useReviewPanelStore((s) => s.closeReviewPanel)
  const maybeAdvancePendingThread = useReviewPanelStore((s) => s.maybeAdvancePendingThread)

  const loading = useReviewPanelStore((s) => s.loading)
  const loadError = useReviewPanelStore((s) => s.loadError)
  const taskDetail = useReviewPanelStore((s) => s.taskDetail)
  const reviewThreadId = useReviewPanelStore((s) => s.reviewThreadId)
  const turns = useReviewPanelStore((s) => s.turns)
  const turnStatus = useReviewPanelStore((s) => s.turnStatus)
  const activeTurnId = useReviewPanelStore((s) => s.activeTurnId)
  const streamingMessage = useReviewPanelStore((s) => s.streamingMessage)
  const streamingMessageLastDeltaAt = useReviewPanelStore((s) => s.streamingMessageLastDeltaAt)
  const streamingReasoning = useReviewPanelStore((s) => s.streamingReasoning)
  const activeItemId = useReviewPanelStore((s) => s.activeItemId)
  const subAgentEntries = useReviewPanelStore((s) => s.subAgentEntries)

  const scrollRef = useRef<HTMLDivElement>(null)
  const contentKey =
    turns.length +
    streamingMessage.length +
    streamingReasoning.length +
    (turnStatus === 'running' ? (streamingMessageLastDeltaAt ?? 0) : 0)

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTop = el.scrollHeight
  }, [contentKey])

  useEffect(() => {
    if (!selectedTaskId) {
      destroyReviewPanel()
      return
    }
    void openReviewPanel(selectedTaskId)
  }, [selectedTaskId, openReviewPanel, destroyReviewPanel])

  useEffect(() => {
    void maybeAdvancePendingThread()
  }, [tasks, maybeAdvancePendingThread])

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') {
        closeReviewPanel()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [closeReviewPanel])

  const listTask = selectedTaskId ? tasks.find((t) => t.id === selectedTaskId) : undefined
  const displayTask: AutomationTask | null =
    listTask && taskDetail
      ? { ...taskDetail, ...listTask }
      : listTask ?? taskDetail

  const threadList = useThreadStore((s) => s.threadList)
  const updateBinding = useAutomationsStore((s) => s.updateBinding)
  const [showThreadPicker, setShowThreadPicker] = useState(false)

  const boundThreadName = useMemo(() => {
    const id = displayTask?.threadBinding?.threadId
    if (!id) return null
    return threadList.find((th) => th.id === id)?.displayName ?? id
  }, [displayTask?.threadBinding, threadList])

  const isBound = !!displayTask?.threadBinding?.threadId

  async function handleUnbind(): Promise<void> {
    if (!displayTask) return
    try {
      await updateBinding(displayTask, null)
      addToast(t('auto.review.unbindSuccess'), 'success')
    } catch (err: unknown) {
      addToast(
        t('auto.dnd.bindFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
    }
  }

  const showWaitingThread =
    !!displayTask &&
    !displayTask.threadId &&
    (displayTask.status === 'pending' ||
      displayTask.status === 'running')

  const showNoActivity =
    !!displayTask &&
    !reviewThreadId &&
    !showWaitingThread &&
    ['completed', 'failed'].includes(
      displayTask.status
    )

  return (
    <div
      style={{
        width: '100%',
        minWidth: 0,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        borderLeft: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-primary)',
        flexShrink: 0
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: '12px 14px',
          borderBottom: '1px solid var(--border-default)',
          display: 'flex',
          alignItems: 'flex-start',
          justifyContent: 'space-between',
          gap: '8px',
          flexShrink: 0
        }}
      >
        <div style={{ minWidth: 0, flex: 1 }}>
          <div
            style={{
              fontSize: '14px',
              fontWeight: 600,
              color: 'var(--text-primary)',
              lineHeight: 1.3,
              wordBreak: 'break-word'
            }}
          >
            {displayTask?.title ?? t('auto.taskTitleFallback')}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '6px', flexWrap: 'wrap' }}>
            {displayTask && <StatusBadge status={displayTask.status} />}
            {displayTask && <ApprovalPolicyBadge policy={displayTask.approvalPolicy} t={t} />}
          </div>
        </div>
        <IconButton
          icon={<X size={16} aria-hidden />}
          label={t('auto.review.panelCloseAria')}
          tooltipLabel={t('auto.review.panelCloseAria')}
          tooltipPlacement="bottom"
          size={28}
          radius={6}
          onClick={() => closeReviewPanel()}
        />
      </div>

      {loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--text-tertiary)' }}>
          {t('threadList.loading')}
        </div>
      )}

      {loadError && !loading && (
        <div style={{ padding: '16px', fontSize: '13px', color: 'var(--error)' }}>{loadError}</div>
      )}

      {displayTask && (
        <div
          style={{
            padding: '10px 14px',
            borderBottom: '1px solid var(--border-default)',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            flexWrap: 'wrap',
            backgroundColor: isBound
              ? 'color-mix(in srgb, var(--accent) 6%, transparent)'
              : 'transparent'
          }}
        >
          <span
            style={{
              fontSize: '11px',
              fontWeight: 600,
              color: 'var(--text-tertiary)',
              textTransform: 'uppercase',
              letterSpacing: '0.04em'
            }}
          >
            {t('auto.review.boundTo')}
          </span>
          <span
            style={{
              fontSize: '12px',
              fontWeight: 500,
              color: isBound ? 'var(--accent)' : 'var(--text-secondary)'
            }}
          >
            {isBound ? `💬 ${boundThreadName ?? ''}` : t('auto.review.noBinding')}
          </span>
          <div style={{ flex: 1 }} />
          <button
            type="button"
            onClick={() => setShowThreadPicker(true)}
            style={{
              padding: '3px 10px',
              borderRadius: '6px',
              border: '1px solid var(--border-default)',
              backgroundColor: 'transparent',
              color: 'var(--text-secondary)',
              fontSize: '11px',
              fontWeight: 500,
              cursor: 'pointer'
            }}
          >
            {isBound ? t('auto.review.change') : t('auto.review.bind')}
          </button>
          {isBound && (
            <button
              type="button"
              onClick={() => void handleUnbind()}
              style={{
                padding: '3px 10px',
                borderRadius: '6px',
                border: '1px solid var(--border-default)',
                backgroundColor: 'transparent',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                fontWeight: 500,
                cursor: 'pointer'
              }}
            >
              {t('auto.review.unbind')}
            </button>
          )}
        </div>
      )}

      {showThreadPicker && displayTask && (
        <ThreadPickerOverlay
          onClose={() => setShowThreadPicker(false)}
          onSelect={(th) => {
            void updateBinding(displayTask, { threadId: th.id, mode: 'run-in-thread' })
              .then(() =>
                addToast(
                  t('auto.dnd.bindSuccess', {
                    task: displayTask.title,
                    thread: th.displayName ?? th.id
                  }),
                  'success'
                )
              )
              .catch((err: unknown) =>
                addToast(
                  t('auto.dnd.bindFailed', {
                    error: err instanceof Error ? err.message : String(err)
                  }),
                  'error'
                )
              )
          }}
        />
      )}

      {displayTask && <WorktreeReviewSection task={displayTask} />}

      {!loading && displayTask?.agentSummary && displayTask.agentSummary.trim().length > 0 ? (
        <div
          style={{
            padding: '12px 14px',
            borderBottom: '1px solid var(--border-default)',
            flexShrink: 0
          }}
        >
          <div
            style={{
              fontSize: '11px',
              fontWeight: 600,
              color: 'var(--text-tertiary)',
              textTransform: 'uppercase',
              letterSpacing: '0.04em',
              marginBottom: '8px'
            }}
          >
            {t('auto.review.agentSummaryHeading')}
          </div>
          <div style={{ fontSize: '13px', color: 'var(--text-primary)' }}>
            <MarkdownRenderer content={displayTask.agentSummary} />
          </div>
        </div>
      ) : null}

      <div
        ref={scrollRef}
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: '12px 14px',
          minHeight: 0
        }}
      >
        <div
          style={{
            fontSize: '11px',
            fontWeight: 600,
            color: 'var(--text-tertiary)',
            textTransform: 'uppercase',
            letterSpacing: '0.04em',
            marginBottom: '10px'
          }}
        >
          {t('auto.review.agentActivityHeading')}
        </div>

        {showWaitingThread && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.waitingAgent')}
          </p>
        )}

        {showNoActivity && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.noActivityRecorded')}
          </p>
        )}

        {reviewThreadId && turns.length === 0 && !showWaitingThread && !showNoActivity && !loading && (
          <p style={{ margin: 0, fontSize: '13px', color: 'var(--text-secondary)' }}>
            {t('auto.review.noTurnsYet')}
          </p>
        )}

        {turns.map((turn, idx) => (
          <div key={turn.id} style={{ marginBottom: '16px' }}>
            <ReviewTurnBlock
              turn={turn}
              activeTurnId={activeTurnId}
              turnStatus={turnStatus}
              streamingMessage={streamingMessage}
              streamingMessageLastDeltaAt={streamingMessageLastDeltaAt}
              streamingReasoning={streamingReasoning}
              activeItemId={activeItemId}
              subAgentEntriesOverride={subAgentEntries}
              isLastTurn={idx === turns.length - 1}
            />
          </div>
        ))}
      </div>

    </div>
  )
}

const worktreeSectionStyle: CSSProperties = {
  padding: '12px 14px',
  borderBottom: '1px solid var(--border-default)',
  display: 'grid',
  gap: '10px',
  flexShrink: 0
}

const worktreeHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: '8px'
}

const worktreeTitleStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  fontSize: '11px',
  fontWeight: 600,
  color: 'var(--text-tertiary)',
  textTransform: 'uppercase',
  letterSpacing: '0.04em'
}

const worktreeMutedTextStyle: CSSProperties = {
  margin: 0,
  fontSize: '12px',
  color: 'var(--text-secondary)',
  lineHeight: 1.45
}

const worktreeErrorStyle: CSSProperties = {
  ...worktreeMutedTextStyle,
  color: 'var(--error)'
}

const worktreeMetaRowStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'max-content minmax(0, 1fr)',
  gap: '8px',
  alignItems: 'center'
}

const worktreeMetaLabelStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-tertiary)'
}

const worktreeBranchStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontSize: '12px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const worktreePillsStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  flexWrap: 'wrap'
}

function worktreePillStyle(kind: 'neutral' | 'warning'): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    minHeight: '20px',
    padding: '2px 7px',
    borderRadius: '8px',
    backgroundColor: kind === 'warning'
      ? 'color-mix(in srgb, var(--warning) 18%, var(--bg-tertiary))'
      : 'var(--bg-tertiary)',
    color: kind === 'warning' ? 'var(--warning)' : 'var(--text-secondary)',
    fontSize: '11px',
    fontWeight: 600
  }
}

const worktreeActionsStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '6px',
  flexWrap: 'wrap'
}
