import { useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, Check, CheckCircle2, Circle, Loader2, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { Input } from '../ui/Input'

type HandoffMode = 'local' | 'worktree'
type DialogPhase = 'confirm' | 'running' | 'success' | 'error'

const HANDOFF_STEP_ADVANCE_MS = 520

interface HandoffSuccessView {
  title: string
  description: string
}

interface WorktreeHandoffDialogProps {
  mode: HandoffMode
  thread: Thread
  baseRef: string | null
  defaultBranchName: string
  localWorkspacePath: string
  disabledReason?: string | null
  onClose: () => void
  onComplete: (thread: Thread) => void | Promise<void>
  onBusyChange?: (busy: boolean) => void
}

function normalizeBranchName(value: string): string {
  return value.trim().replace(/^\/+/, '')
}

function branchNameError(value: string, t: ReturnType<typeof useT>): string | null {
  const branch = normalizeBranchName(value)
  if (!branch) return t('workspaceFooter.branchRequired')
  if (branch.endsWith('/')) return t('workspaceFooter.branchCannotEndSlash')
  return null
}

function workspaceName(path: string): string {
  const trimmed = path.trim().replace(/[\\/]+$/, '')
  if (!trimmed) return path
  const parts = trimmed.split(/[\\/]+/)
  return parts[parts.length - 1] || trimmed
}

export function WorktreeHandoffDialog({
  mode,
  thread,
  baseRef,
  defaultBranchName,
  localWorkspacePath,
  disabledReason = null,
  onClose,
  onComplete,
  onBusyChange
}: WorktreeHandoffDialogProps): JSX.Element | null {
  const t = useT()
  const [phase, setPhase] = useState<DialogPhase>('confirm')
  const [branchDraft, setBranchDraft] = useState(defaultBranchName)
  const [errorText, setErrorText] = useState<string | null>(null)
  const [successView, setSuccessView] = useState<HandoffSuccessView | null>(null)
  const [completedStepCount, setCompletedStepCount] = useState(0)
  const [dismissed, setDismissed] = useState(false)
  const mountedRef = useRef(true)
  const dismissedRef = useRef(false)
  const completedStepCountRef = useRef(0)
  const progressTimerRef = useRef<ReturnType<typeof window.setInterval> | null>(null)
  const closeTimerRef = useRef<ReturnType<typeof window.setTimeout> | null>(null)
  const worktreeBranch = thread.worktree?.branchName?.trim() || baseRef || t('workspaceFooter.branchUnknown')
  const targetWorkspace = workspaceName(localWorkspacePath)
  const branchError = mode === 'worktree' ? branchNameError(branchDraft, t) : null
  const handoffDisabled = Boolean(branchError) || Boolean(disabledReason)
  const title = phase === 'success'
    ? successView?.title ?? t('workspaceFooter.handoffWorktreeSuccessTitle')
    : mode === 'worktree'
      ? (phase === 'running' ? t('workspaceFooter.handoffWorktreeRunningTitle') : t('workspaceFooter.handoffWorktreeTitle'))
      : (phase === 'running' ? t('workspaceFooter.handoffLocalRunningTitle') : t('workspaceFooter.handoffLocalTitle'))
  const description = phase === 'success'
    ? successView?.description ?? ''
    : mode === 'worktree'
      ? (phase === 'running'
          ? t('workspaceFooter.handoffWorktreeRunningDescription')
          : t('workspaceFooter.handoffWorktreeDescription'))
      : (phase === 'running'
          ? t('workspaceFooter.handoffLocalRunningDescription')
          : t('workspaceFooter.handoffLocalDescription'))

  const progressSteps = useMemo(() => {
    if (mode === 'worktree') {
      const branch = normalizeBranchName(branchDraft) || defaultBranchName
      return [
        t('workspaceFooter.handoffStepCreateWorktree'),
        t('workspaceFooter.handoffStepCopyToWorktree'),
        t('workspaceFooter.handoffStepCheckoutWorktree', { branch }),
        t('workspaceFooter.handoffStepMoveToWorktree')
      ]
    }

    return [
      t('workspaceFooter.handoffStepStashWorktree'),
      t('workspaceFooter.handoffStepDetachWorktreeBranch'),
      t('workspaceFooter.handoffStepCheckoutLocal', { branch: worktreeBranch }),
      t('workspaceFooter.handoffStepApplyLocal'),
      t('workspaceFooter.handoffStepMoveToLocal')
    ]
  }, [branchDraft, defaultBranchName, mode, t, worktreeBranch])

  useEffect(() => {
    setBranchDraft(defaultBranchName)
  }, [defaultBranchName])

  useEffect(() => {
    return () => {
      mountedRef.current = false
      clearProgressTimer()
      if (closeTimerRef.current != null) window.clearTimeout(closeTimerRef.current)
    }
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        event.preventDefault()
        handleClose()
      } else if (event.key === 'Enter' && phase === 'confirm' && !handoffDisabled) {
        event.preventDefault()
        void startHandoff()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  })

  function clearProgressTimer(): void {
    if (progressTimerRef.current != null) {
      window.clearInterval(progressTimerRef.current)
      progressTimerRef.current = null
    }
  }

  function updateCompletedStepCount(value: number): void {
    completedStepCountRef.current = value
    if (mountedRef.current) setCompletedStepCount(value)
  }

  function advanceCompletedStepCount(maximum: number): void {
    setCompletedStepCount((count) => {
      const next = Math.min(count + 1, maximum)
      completedStepCountRef.current = next
      return next
    })
  }

  async function waitForProgressFrame(ms: number): Promise<void> {
    await new Promise<void>((resolve) => {
      closeTimerRef.current = window.setTimeout(() => {
        closeTimerRef.current = null
        resolve()
      }, ms)
    })
  }

  async function finishProgressSteps(stepCount: number): Promise<void> {
    for (let next = Math.min(completedStepCountRef.current + 1, stepCount); next <= stepCount; next++) {
      if (dismissedRef.current || !mountedRef.current) return
      updateCompletedStepCount(next)
      if (next < stepCount) await waitForProgressFrame(HANDOFF_STEP_ADVANCE_MS)
    }
  }

  function handleClose(): void {
    if (phase === 'running') {
      dismissedRef.current = true
      setDismissed(true)
      return
    }

    onClose()
  }

  function normalizeThreadForCompletedMode(nextThread: Thread): Thread {
    if (mode !== 'local') return nextThread
    return {
      ...nextThread,
      effectiveWorkspacePath: nextThread.effectiveWorkspacePath || nextThread.workspacePath || localWorkspacePath,
      worktree: null
    }
  }

  function applyThreadUpdate(nextThread: Thread): Thread {
    const normalizedThread = normalizeThreadForCompletedMode(nextThread)
    useThreadStore.getState().upsertThreads([normalizedThread])
    if (useThreadStore.getState().activeThreadId === normalizedThread.id) {
      useThreadStore.getState().setActiveThread(normalizedThread)
    }
    return normalizedThread
  }

  async function readThreadMetadata(threadId: string): Promise<Thread | null> {
    try {
      const result = await window.api.appServer.sendRequest('thread/read', {
        threadId,
      }) as unknown as { thread?: Thread }
      return result.thread ?? null
    } catch (err) {
      console.warn('thread/read after worktree handoff failed:', err)
      return null
    }
  }

  async function startHandoff(): Promise<void> {
    if (phase === 'running') return
    if (disabledReason) return
    const branch = normalizeBranchName(branchDraft)
    const error = mode === 'worktree' ? branchNameError(branch, t) : null
    if (error) {
      setErrorText(error)
      return
    }

    setErrorText(null)
    setSuccessView(null)
    setPhase('running')
    updateCompletedStepCount(0)
    onBusyChange?.(true)
    clearProgressTimer()
    const stepCount = progressSteps.length
    progressTimerRef.current = window.setInterval(() => {
      advanceCompletedStepCount(Math.max(stepCount - 1, 0))
    }, HANDOFF_STEP_ADVANCE_MS)

    try {
      const params = mode === 'worktree'
        ? {
            threadId: thread.id,
            mode,
            branchName: branch,
            baseRef: baseRef || undefined,
            copyDirtyChanges: true
          }
        : {
            threadId: thread.id,
            mode
          }
      const result = await window.api.appServer.sendRequest(
        'thread/worktree/handoff',
        params,
        180_000
      ) as { thread?: Thread }
      const resultThread = result.thread
      const normalizedResultThread = resultThread ? applyThreadUpdate(resultThread) : null
      const refreshedThread = await readThreadMetadata(resultThread?.id ?? thread.id)
      const normalizedRefreshedThread = refreshedThread ? applyThreadUpdate(refreshedThread) : null
      const completedThread = normalizedRefreshedThread ?? normalizedResultThread

      clearProgressTimer()
      if (completedThread) {
        await onComplete(completedThread)
      }
      if (!dismissedRef.current) {
        await finishProgressSteps(stepCount)
      }

      if (dismissedRef.current) {
        addToast(
          mode === 'worktree'
            ? t('workspaceFooter.handoffToWorktreeSuccess')
            : t('workspaceFooter.handoffToLocalSuccess'),
          'success'
        )
        onClose()
        return
      }

      const nextBranch = mode === 'worktree'
        ? (completedThread?.worktree?.branchName?.trim() || branch || defaultBranchName)
        : worktreeBranch
      const nextBaseRef = completedThread?.worktree?.baseRef?.trim() || baseRef || t('workspaceFooter.branchUnknown')
      setSuccessView({
        title: mode === 'worktree'
          ? t('workspaceFooter.handoffWorktreeSuccessTitle')
          : t('workspaceFooter.handoffLocalSuccessTitle'),
        description: mode === 'worktree'
          ? t('workspaceFooter.handoffWorktreeSuccessDescription', { branch: nextBranch, baseRef: nextBaseRef })
          : t('workspaceFooter.handoffLocalSuccessDescription', { branch: nextBranch })
      })
      setPhase('success')
    } catch (err) {
      clearProgressTimer()
      const message = err instanceof Error ? err.message : String(err)
      setErrorText(t('workspaceFooter.handoffFailed', { error: message }))
      addToast(t('workspaceFooter.handoffFailed', { error: message }), 'error')
      if (dismissedRef.current) onClose()
      else setPhase('error')
    } finally {
      onBusyChange?.(false)
    }
  }

  if (dismissed) return null

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-label={title}
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && phase !== 'running') onClose()
      }}
    >
      <div style={modalStyle} onMouseDown={(event) => event.stopPropagation()}>
        <IconButton
          icon={<X size={17} strokeWidth={2} aria-hidden />}
          label={t('workspaceFooter.close')}
          size={30}
          style={closeButtonPositionStyle}
          onClick={handleClose}
        />
        <div style={phase === 'success' ? successIconShellStyle : iconShellStyle}>
          {phase === 'success' ? (
            <Check size={28} strokeWidth={2.2} aria-hidden />
          ) : (
            <ArrowRightLeft size={24} strokeWidth={1.9} aria-hidden />
          )}
        </div>
        <h2 style={titleStyle}>{title}</h2>
        <p style={descriptionStyle}>{description}</p>

        {phase === 'success' ? null : phase === 'running' ? (
          <div style={stepsStyle}>
            {progressSteps.map((step, index) => (
              <ProgressStep
                key={`${index}:${step}`}
                label={step}
                status={index < completedStepCount
                  ? 'complete'
                  : index === completedStepCount
                    ? 'active'
                    : 'pending'}
              />
            ))}
          </div>
        ) : (
          <>
            {mode === 'worktree' ? (
              <label style={fieldStyle}>
                <span>{t('workspaceFooter.branchName')}</span>
                <Input
                  value={branchDraft}
                  autoFocus
                  onChange={(event) => setBranchDraft(event.target.value)}
                />
              </label>
            ) : (
              <div style={localSummaryStyle}>
                <span style={summaryLabelStyle}>{t('workspaceFooter.handoffLocalBranch')}</span>
                <span style={summaryPillStyle}>{worktreeBranch}</span>
                <span style={summaryLabelStyle}>{t('workspaceFooter.handoffLocalWorkspace')}</span>
                <span style={summaryPillStyle}>{targetWorkspace}</span>
              </div>
            )}

            {(branchError || errorText) && (
              <div style={errorStyle}>{errorText || branchError}</div>
            )}

            {disabledReason && (
              <div role="status" style={noticeStyle}>{disabledReason}</div>
            )}

            <Button
              variant="primary"
              size="prominent"
              disabled={handoffDisabled}
              style={{ width: '100%' }}
              onClick={() => { void startHandoff() }}
            >
              {t('workspaceFooter.handOff')}
            </Button>
          </>
        )}

        {phase === 'error' && (
          <Button
            variant="secondary"
            size="prominent"
            style={{ width: '100%' }}
            onClick={onClose}
          >
            {t('workspaceFooter.close')}
          </Button>
        )}
      </div>
    </div>,
    document.body
  )
}

function ProgressStep({
  label,
  status
}: {
  label: string
  status: 'active' | 'complete' | 'pending'
}): JSX.Element {
  const color = status === 'pending' ? 'var(--text-dimmed)' : 'var(--text-primary)'
  return (
    <div style={{ ...stepStyle, color, opacity: status === 'pending' ? 0.62 : 1 }}>
      {status === 'complete' ? (
        <CheckCircle2 size={20} strokeWidth={1.8} aria-hidden />
      ) : status === 'active' ? (
        <Loader2 size={20} strokeWidth={1.9} className="animate-spin-custom" aria-hidden />
      ) : (
        <Circle size={20} strokeWidth={1.7} aria-hidden />
      )}
      <span>{label}</span>
    </div>
  )
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--overlay-scrim)'
}

const modalStyle: CSSProperties = {
  position: 'relative',
  width: '520px',
  maxWidth: 'calc(100vw - 48px)',
  padding: '24px 28px 28px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  boxShadow: 'var(--shadow-level-3)'
}

const closeButtonPositionStyle: CSSProperties = {
  position: 'absolute',
  top: '16px',
  right: '16px',
  width: '30px',
  height: '30px',
  border: 'none',
  borderRadius: '8px',
  background: 'transparent',
  color: 'var(--text-muted)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer'
}

const iconShellStyle: CSSProperties = {
  width: '48px',
  height: '48px',
  borderRadius: '10px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  marginBottom: '18px'
}

const successIconShellStyle: CSSProperties = {
  ...iconShellStyle,
  background: 'color-mix(in srgb, var(--success) 24%, var(--bg-tertiary))',
  color: 'var(--success)'
}

const titleStyle: CSSProperties = {
  margin: '0 36px 10px 0',
  fontSize: '24px',
  lineHeight: 1.2,
  letterSpacing: 0,
  color: 'var(--text-primary)'
}

const descriptionStyle: CSSProperties = {
  margin: '0 0 22px',
  color: 'var(--text-secondary)',
  fontSize: '15px',
  lineHeight: 1.55
}

const fieldStyle: CSSProperties = {
  display: 'grid',
  gap: '9px',
  color: 'var(--text-primary)',
  fontSize: '13px',
  fontWeight: 600
}

const stepsStyle: CSSProperties = {
  display: 'grid',
  gap: '18px',
  marginTop: '24px'
}

const stepStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '14px',
  minHeight: '28px',
  fontSize: '15px',
  lineHeight: 1.35,
  fontWeight: 600
}

const localSummaryStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'max-content minmax(0, 1fr)',
  alignItems: 'center',
  gap: '12px 10px',
  margin: '8px 0 0'
}

const summaryLabelStyle: CSSProperties = {
  color: 'var(--text-secondary)',
  fontSize: '14px'
}

const summaryPillStyle: CSSProperties = {
  minWidth: 0,
  justifySelf: 'start',
  maxWidth: '100%',
  padding: '6px 10px',
  borderRadius: '8px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  fontSize: '14px',
  fontWeight: 600,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const errorStyle: CSSProperties = {
  marginTop: '10px',
  color: 'var(--error)',
  fontSize: '13px',
  lineHeight: 1.45
}

const noticeStyle: CSSProperties = {
  marginTop: '12px',
  padding: '10px 12px',
  borderRadius: '8px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  lineHeight: 1.45
}
