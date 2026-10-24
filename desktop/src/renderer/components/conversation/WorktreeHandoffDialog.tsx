import { useEffect, useId, useRef, useState, type JSX } from 'react'
import { ArrowRightLeft, Check, CheckCircle2, XCircle } from 'lucide-react'
import { Spinner } from '../ui/Spinner'
import { Select } from '../ui/Select'
import { useT } from '../../contexts/LocaleContext'
import { readAppServerErrorFields } from '../../../shared/appServerError'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import type { Thread } from '../../types/thread'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { ScriptOutput } from '../worktreeCreation/ScriptOutput'
import { worktreeCleanupFailureOf } from '../../utils/worktreeCleanupFailure'
import styles from './WorktreeHandoffDialog.module.css'

type HandoffMode = 'local' | 'worktree'
type DialogPhase = 'confirm' | 'running' | 'success' | 'error'
type StepStatus = 'started' | 'completed' | 'skipped' | 'failed'

const NEW_WORKTREE = ''

interface HandoffStep {
  step: string
  status: StepStatus
}

interface UnboundWorktree {
  path: string
  branchName: string
}

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
  const titleId = useId()
  const [phase, setPhase] = useState<DialogPhase>('confirm')
  const [branchDraft, setBranchDraft] = useState('')
  const [errorText, setErrorText] = useState<string | null>(null)
  const [conflictPaths, setConflictPaths] = useState<string[]>([])
  const [cleanupOutput, setCleanupOutput] = useState<string | null>(null)
  const [successView, setSuccessView] = useState<HandoffSuccessView | null>(null)
  const [dismissed, setDismissed] = useState(false)
  const [steps, setSteps] = useState<HandoffStep[]>([])
  const [unboundWorktrees, setUnboundWorktrees] = useState<UnboundWorktree[]>([])
  const [targetPath, setTargetPath] = useState(NEW_WORKTREE)
  const dismissedRef = useRef(false)
  const operationIdRef = useRef<string | null>(null)
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
    if (mode !== 'worktree') return
    let cancelled = false
    void window.api.appServer.sendRequest('worktree/list', {})
      .then((result) => {
        if (cancelled) return
        const data = (result as { data?: Array<{ threadId?: string | null; path: string; branchName: string; exists?: boolean }> }).data ?? []
        setUnboundWorktrees(data
          .filter((entry) => !entry.threadId && entry.exists !== false)
          .map((entry) => ({ path: entry.path, branchName: entry.branchName })))
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [mode])

  useEffect(() => {
    return window.api.appServer.onNotification((payload) => {
      const { method, params } = payload as { method: string; params?: { operationId?: string | null; step?: string; status?: StepStatus } }
      if (method !== 'thread/worktree/handoffProgress' || !params?.step || !params.status) return
      if (!operationIdRef.current || params.operationId !== operationIdRef.current) return
      const update = { step: params.step, status: params.status }
      setSteps((current) => current.some((entry) => entry.step === update.step)
        ? current.map((entry) => entry.step === update.step ? update : entry)
        : [...current, update])
    })
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        event.preventDefault()
        handleClose()
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
    setCleanupOutput(null)
    setSuccessView(null)
    setSteps([])
    operationIdRef.current = `handoff-${thread.id}-${Date.now()}`
    setPhase('running')
    onBusyChange?.(true)

    try {
      const params = mode === 'worktree'
        ? {
            threadId: thread.id,
            mode,
            operationId: operationIdRef.current,
            ...(targetPath
              ? { path: targetPath }
              : { branchName: branch || undefined, baseRef: baseRef || undefined }),
            copyDirtyChanges: true
          }
        : {
            threadId: thread.id,
            mode,
            operationId: operationIdRef.current
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
      const message = readAppServerErrorFields(err).data?.code === 'WorktreeInUse'
        ? t('workspaceFooter.handoffWorktreeInUse')
        : err instanceof Error ? err.message : String(err)
      setConflictPaths(conflictPathsOf(err))
      setCleanupOutput(worktreeCleanupFailureOf(err)?.outputTail ?? null)
      setErrorText(t('workspaceFooter.handoffFailed', { error: message }))
      addToast(t('workspaceFooter.handoffFailed', { error: message }), 'error')
      if (dismissedRef.current) onClose()
      else setPhase('error')
    } finally {
      onBusyChange?.(false)
    }
  }

  if (dismissed) return null

  const editable = phase === 'confirm' || phase === 'error'

  return (
    <ModalDialog
      titleId={titleId}
      onDismiss={phase === 'running' ? undefined : onClose}
      onSubmit={editable ? () => void startHandoff() : undefined}
      header={(
        <ModalHeader
          icon={phase === 'success'
            ? <Check size={18} strokeWidth={2.2} aria-hidden />
            : <ArrowRightLeft size={18} strokeWidth={2} aria-hidden />}
          title={title}
          titleId={titleId}
          description={description}
          onClose={handleClose}
          closeLabel={t('workspaceFooter.close')}
        />
      )}
      error={editable ? errorText || branchError : null}
      footer={editable ? (
        <ModalFooter>
          {phase === 'error' && (
            <Button variant="secondary" onClick={onClose}>
              {t('workspaceFooter.close')}
            </Button>
          )}
          <Button type="submit" variant="primary" disabled={handoffDisabled} autoFocus={mode === 'local'}>
            {t('workspaceFooter.handOff')}
          </Button>
        </ModalFooter>
      ) : undefined}
    >
      {phase === 'running' ? (
        <div role="status" className={styles.steps}>
          {steps.filter((entry) => entry.status !== 'skipped').map((entry) => (
            <div key={entry.step} className={styles.step}>
              {entry.status === 'completed' ? (
                <CheckCircle2 size={18} strokeWidth={1.8} aria-hidden />
              ) : entry.status === 'failed' ? (
                <XCircle size={18} strokeWidth={1.8} aria-hidden className={styles.failed} />
              ) : (
                <Spinner size={18} />
              )}
              <span>{t(`workspaceFooter.handoffStep.${entry.step}`, { branch: mode === 'local' ? worktreeBranch : '' })}</span>
            </div>
          ))}
          {steps.length === 0 && <Spinner size={20} />}
        </div>
      ) : editable ? (
        <>
          {mode === 'worktree' ? (
            <>
              {unboundWorktrees.length > 0 && (
                <label className={styles.field}>
                  <span>{t('workspaceFooter.handoffTarget')}</span>
                  <Select
                    value={targetPath}
                    ariaLabel={t('workspaceFooter.handoffTarget')}
                    onValueChange={setTargetPath}
                    options={[
                      { value: NEW_WORKTREE, label: t('workspaceFooter.handoffTargetNew') },
                      ...unboundWorktrees.map((entry) => ({
                        value: entry.path,
                        label: entry.branchName,
                        description: t('workspaceFooter.handoffTargetExisting')
                      }))
                    ]}
                  />
                </label>
              )}
              {!targetPath && (
                <label className={styles.field}>
                  <span>{t('workspaceFooter.branchName')}</span>
                  <Input
                    value={branchDraft}
                    autoFocus
                    placeholder={t('workspaceFooter.branchNameAutomatic')}
                    onChange={(event) => setBranchDraft(event.target.value)}
                  />
                </label>
              )}
            </>
          ) : (
            <div className={styles.summary}>
              <span className={styles.summaryLabel}>{t('workspaceFooter.handoffLocalBranch')}</span>
              <span className={styles.summaryPill}>{worktreeBranch}</span>
              <span className={styles.summaryLabel}>{t('workspaceFooter.handoffLocalWorkspace')}</span>
              <span className={styles.summaryPill}>{targetWorkspace}</span>
            </div>
          )}

          {conflictPaths.length > 0 && (
            <div className={styles.detail}>
              <span>{t('workspaceFooter.handoffConflictPaths')}</span>
              <ul className={styles.conflictList}>
                {conflictPaths.map((path) => <li key={path}>{path}</li>)}
              </ul>
            </div>
          )}

          {cleanupOutput ? (
            <div className={`${styles.detail} ${styles.cleanupOutput}`}>
              <span>{t('workspaceFooter.cleanupOutput')}</span>
              <ScriptOutput text={cleanupOutput} label={t('workspaceFooter.cleanupOutput')} />
            </div>
          ) : null}

          {disabledReason && (
            <div role="status" className={styles.notice}>{disabledReason}</div>
          )}
        </>
      ) : null}
    </ModalDialog>
  )
}
