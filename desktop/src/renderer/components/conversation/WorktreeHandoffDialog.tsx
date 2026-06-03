import { useEffect, useMemo, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, CheckCircle2, Circle, Loader2, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'

type HandoffMode = 'local' | 'worktree'
type DialogPhase = 'confirm' | 'running' | 'error'

interface WorktreeHandoffDialogProps {
  mode: HandoffMode
  thread: Thread
  baseRef: string | null
  defaultBranchName: string
  localWorkspacePath: string
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
  onClose,
  onComplete,
  onBusyChange
}: WorktreeHandoffDialogProps): JSX.Element | null {
  const t = useT()
  const [phase, setPhase] = useState<DialogPhase>('confirm')
  const [branchDraft, setBranchDraft] = useState(defaultBranchName)
  const [errorText, setErrorText] = useState<string | null>(null)
  const [completedStepCount, setCompletedStepCount] = useState(0)
  const [dismissed, setDismissed] = useState(false)
  const dismissedRef = useRef(false)
  const progressTimerRef = useRef<ReturnType<typeof window.setInterval> | null>(null)
  const closeTimerRef = useRef<ReturnType<typeof window.setTimeout> | null>(null)
  const worktreeBranch = thread.worktree?.branchName?.trim() || baseRef || t('workspaceFooter.branchUnknown')
  const targetWorkspace = workspaceName(localWorkspacePath)
  const branchError = mode === 'worktree' ? branchNameError(branchDraft, t) : null
  const title = mode === 'worktree'
    ? (phase === 'running' ? t('workspaceFooter.handoffWorktreeRunningTitle') : t('workspaceFooter.handoffWorktreeTitle'))
    : (phase === 'running' ? t('workspaceFooter.handoffLocalRunningTitle') : t('workspaceFooter.handoffLocalTitle'))
  const description = mode === 'worktree'
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
      clearProgressTimer()
      if (closeTimerRef.current != null) window.clearTimeout(closeTimerRef.current)
    }
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        event.preventDefault()
        handleClose()
      } else if (event.key === 'Enter' && phase === 'confirm' && !branchError) {
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

  function handleClose(): void {
    if (phase === 'running') {
      dismissedRef.current = true
      setDismissed(true)
      return
    }

    onClose()
  }

  async function startHandoff(): Promise<void> {
    if (phase === 'running') return
    const branch = normalizeBranchName(branchDraft)
    const error = mode === 'worktree' ? branchNameError(branch, t) : null
    if (error) {
      setErrorText(error)
      return
    }

    setErrorText(null)
    setPhase('running')
    setCompletedStepCount(0)
    onBusyChange?.(true)
    clearProgressTimer()
    progressTimerRef.current = window.setInterval(() => {
      setCompletedStepCount((count) => Math.min(count + 1, Math.max(progressSteps.length - 1, 0)))
    }, 1100)

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

      clearProgressTimer()
      setCompletedStepCount(progressSteps.length)
      if (result.thread) {
        useThreadStore.getState().upsertThreads([result.thread])
        if (useThreadStore.getState().activeThreadId === result.thread.id) {
          useThreadStore.getState().setActiveThread(result.thread)
        }
        await onComplete(result.thread)
      }

      addToast(
        mode === 'worktree'
          ? t('workspaceFooter.handoffToWorktreeSuccess')
          : t('workspaceFooter.handoffToLocalSuccess'),
        'success'
      )
      closeTimerRef.current = window.setTimeout(onClose, dismissedRef.current ? 0 : 360)
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
        <button
          type="button"
          aria-label={t('workspaceFooter.close')}
          style={closeButtonStyle}
          onClick={handleClose}
        >
          <X size={17} strokeWidth={2} aria-hidden />
        </button>
        <div style={iconShellStyle}>
          <ArrowRightLeft size={24} strokeWidth={1.9} aria-hidden />
        </div>
        <h2 style={titleStyle}>{title}</h2>
        <p style={descriptionStyle}>{description}</p>

        {phase === 'running' ? (
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
                <input
                  value={branchDraft}
                  autoFocus
                  onChange={(event) => setBranchDraft(event.target.value)}
                  style={inputStyle}
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

            <button
              type="button"
              disabled={Boolean(branchError)}
              style={{
                ...primaryButtonStyle,
                opacity: branchError ? 0.55 : 1,
                cursor: branchError ? 'default' : 'pointer'
              }}
              onClick={() => { void startHandoff() }}
            >
              {t('workspaceFooter.handOff')}
            </button>
          </>
        )}

        {phase === 'error' && (
          <button
            type="button"
            style={secondaryButtonStyle}
            onClick={onClose}
          >
            {t('workspaceFooter.close')}
          </button>
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

const closeButtonStyle: CSSProperties = {
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

const inputStyle: CSSProperties = {
  height: '46px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  padding: '0 14px',
  font: 'inherit',
  outline: 'none'
}

const primaryButtonStyle: CSSProperties = {
  width: '100%',
  height: '52px',
  marginTop: '26px',
  border: 'none',
  borderRadius: '8px',
  background: 'var(--text-primary)',
  color: 'var(--bg-primary)',
  font: 'inherit',
  fontWeight: 700
}

const secondaryButtonStyle: CSSProperties = {
  width: '100%',
  height: '44px',
  marginTop: '16px',
  border: 'none',
  borderRadius: '8px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  font: 'inherit',
  cursor: 'pointer'
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
