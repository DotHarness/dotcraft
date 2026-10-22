import { useEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, Check } from 'lucide-react'
import { Spinner } from '../ui/Spinner'
import { useT } from '../../contexts/LocaleContext'
import { readAppServerErrorFields } from '../../../shared/appServerError'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'
import { Button } from '../ui/Button'
import { DialogCloseButton } from '../ui/DialogCloseButton'
import { Input } from '../ui/Input'

type HandoffMode = 'local' | 'worktree'
type DialogPhase = 'confirm' | 'running' | 'success' | 'error'

interface HandoffSuccessView {
  title: string
  description: string
}

interface WorktreeHandoffDialogProps {
  mode: HandoffMode
  thread: Thread
  baseRef: string | null
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
  return normalizeBranchName(value).endsWith('/') ? t('workspaceFooter.branchCannotEndSlash') : null
}

function conflictPathsOf(error: unknown): string[] {
  const paths = readAppServerErrorFields(error).data?.params?.conflictPaths
  return Array.isArray(paths) ? paths.filter((path): path is string => typeof path === 'string') : []
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
  localWorkspacePath,
  disabledReason = null,
  onClose,
  onComplete,
  onBusyChange
}: WorktreeHandoffDialogProps): JSX.Element | null {
  const t = useT()
  const [phase, setPhase] = useState<DialogPhase>('confirm')
  const [branchDraft, setBranchDraft] = useState('')
  const [errorText, setErrorText] = useState<string | null>(null)
  const [conflictPaths, setConflictPaths] = useState<string[]>([])
  const [successView, setSuccessView] = useState<HandoffSuccessView | null>(null)
  const [dismissed, setDismissed] = useState(false)
  const dismissedRef = useRef(false)
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
    setConflictPaths([])
    setSuccessView(null)
    setPhase('running')
    onBusyChange?.(true)

    try {
      const params = mode === 'worktree'
        ? {
            threadId: thread.id,
            mode,
            branchName: branch || undefined,
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

      if (completedThread) {
        await onComplete(completedThread)
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
        ? (completedThread?.worktree?.branchName?.trim() || branch)
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
      const message = err instanceof Error ? err.message : String(err)
      setConflictPaths(conflictPathsOf(err))
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
        <DialogCloseButton label={t('workspaceFooter.close')} onClose={handleClose} style={closeButtonPositionStyle} />
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
          <div role="status" style={busyStyle}>
            <Spinner size={20} />
          </div>
        ) : (
          <>
            {mode === 'worktree' ? (
              <label style={fieldStyle}>
                <span>{t('workspaceFooter.branchName')}</span>
                <Input
                  value={branchDraft}
                  autoFocus
                  placeholder={t('workspaceFooter.branchNameAutomatic')}
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

            {conflictPaths.length > 0 && (
              <div style={conflictStyle}>
                <span>{t('workspaceFooter.handoffConflictPaths')}</span>
                <ul style={conflictListStyle}>
                  {conflictPaths.map((path) => <li key={path}>{path}</li>)}
                </ul>
              </div>
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
  right: '16px'
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

const busyStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'center',
  marginTop: '8px'
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

const conflictStyle: CSSProperties = {
  marginTop: '10px',
  color: 'var(--text-secondary)',
  fontSize: '13px',
  lineHeight: 1.45
}

const conflictListStyle: CSSProperties = {
  margin: '6px 0 0',
  paddingLeft: '18px',
  maxHeight: '140px',
  overflowY: 'auto',
  fontFamily: 'var(--font-mono)',
  color: 'var(--text-primary)'
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
