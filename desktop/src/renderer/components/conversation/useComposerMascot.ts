import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { createElement } from 'react'
import { ArrowRight, FileText, MessageSquareWarning, RotateCcw, Stethoscope } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { usePluginStore } from '../../stores/pluginStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import type { ContextMenuItem } from '../ui/ContextMenu'
import type { ComposerMascotInteraction } from './ComposerShell'
import type { InputPart } from '../../types/conversation'
import type { ThreadSummary } from '../../types/thread'

/**
 * Drives the composer mascot from live conversation state and wires its
 * right-click preset actions.
 *
 * Scenarios (kept deliberately small — context has its own indicator, and
 * approvals keep their dedicated decision UI, so the mascot only nudges there):
 * - turn failed   → operator face + red light, error bubble + diagnose/report/retry menu
 * - turn complete → happy face + green light, brief success bubble (auto-dismiss)
 * - awaiting input approval → operator face, informational bubble (no duplicate buttons)
 * - running       → operator face
 * - idle          → ambient (focus/drag driven; hook returns undefined)
 *
 * The diagnose/report actions install the built-in `dotcraft-doctor` plugin
 * (with an explicit confirm step) and open a fresh thread that runs the
 * matching skill, per the agreed design.
 */

const DOCTOR_PLUGIN_ID = 'dotcraft-doctor'
const SUCCESS_BUBBLE_MS = 3200

type LocalBubble =
  | { kind: 'error'; turnId: string }
  | { kind: 'success'; turnId: string }
  | { kind: 'approval' }
  | { kind: 'confirmInstall'; skill: string; context: string }
  | { kind: 'busy' }
  | null

interface UseComposerMascotArgs {
  threadId: string
  workspacePath: string
}

export function useComposerMascot({
  threadId,
  workspacePath
}: UseComposerMascotArgs): ComposerMascotInteraction | undefined {
  const t = useT()
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const pendingApproval = useConversationStore((s) => s.pendingApproval)

  const lastTurn = turns.length > 0 ? turns[turns.length - 1] : undefined
  const lastTurnId = lastTurn?.id
  const lastTurnStatus = lastTurn?.status
  const lastTurnError = lastTurn?.error
  const approvalItemId = pendingApproval?.itemId ?? null
  const isFailed = lastTurnStatus === 'failed'

  const [local, setLocal] = useState<LocalBubble>(null)
  /** turnId:status we've already auto-surfaced, so a dismissed bubble stays dismissed. */
  const handledRef = useRef<string | null>(null)
  /** Approval itemId the user dismissed, so the nudge doesn't immediately reappear. */
  const dismissedApprovalRef = useRef<string | null>(null)
  /** Previous turnStatus / threadId, to distinguish a live finish from a thread switch. */
  const prevStatusRef = useRef<string | null>(null)
  const prevThreadRef = useRef<string | null>(null)

  // Surface the outcome of the latest finished turn / approval, without clobbering
  // a user-initiated override (install confirm or busy state).
  useEffect(() => {
    const prevStatus = prevStatusRef.current
    const prevThread = prevThreadRef.current
    prevStatusRef.current = turnStatus
    prevThreadRef.current = threadId
    const threadChanged = prevThread !== threadId
    // Was the active thread mid-work, so an idle now means it just finished live?
    const wasActive =
      prevStatus === 'running' || prevStatus === 'waitingApproval' || prevStatus === 'waitingInput'

    // Don't disturb a user-initiated override (install confirm or busy state).
    if (local?.kind === 'confirmInstall' || local?.kind === 'busy') return

    // Switching into (or first mounting) a thread must never auto-pop a stale
    // outcome: treat its loaded last turn as already-seen. A genuinely pending
    // approval still surfaces, since that is a live decision waiting in this thread.
    if (threadChanged) {
      if (lastTurnId) handledRef.current = `${lastTurnId}:${lastTurnStatus}`
      dismissedApprovalRef.current = null
      setLocal(turnStatus === 'waitingApproval' ? { kind: 'approval' } : null)
      return
    }

    if (turnStatus === 'waitingApproval') {
      if (approvalItemId && dismissedApprovalRef.current === approvalItemId) return
      setLocal((prev) => (prev?.kind === 'approval' ? prev : { kind: 'approval' }))
      return
    }

    dismissedApprovalRef.current = null

    if (turnStatus === 'running') {
      setLocal((prev) =>
        prev && (prev.kind === 'error' || prev.kind === 'success' || prev.kind === 'approval') ? null : prev
      )
      return
    }

    // Surface a completion/failure only when it just happened live in this thread,
    // never when loading a thread that was already in that state.
    if (wasActive && lastTurnId && (lastTurnStatus === 'failed' || lastTurnStatus === 'completed')) {
      const key = `${lastTurnId}:${lastTurnStatus}`
      if (handledRef.current !== key) {
        handledRef.current = key
        setLocal({ kind: lastTurnStatus === 'failed' ? 'error' : 'success', turnId: lastTurnId })
      }
    }
  }, [turnStatus, lastTurnId, lastTurnStatus, approvalItemId, local?.kind, threadId])

  // Auto-dismiss the celebratory success bubble.
  useEffect(() => {
    if (local?.kind !== 'success') return
    const timer = setTimeout(() => {
      setLocal((prev) => (prev?.kind === 'success' ? null : prev))
    }, SUCCESS_BUBBLE_MS)
    return () => clearTimeout(timer)
  }, [local])

  // Dismiss via an anthropomorphic reply action ("OK" / "Got it" / "Not now").
  const dismiss = useCallback(() => {
    setLocal((prev) => {
      if (prev?.kind === 'approval' && approvalItemId) dismissedApprovalRef.current = approvalItemId
      return null
    })
  }, [approvalItemId])

  const diagnoseContext = useCallback(
    () => t('mascot.prompt.diagnose', { error: (lastTurnError ?? '').slice(0, 600) }),
    [lastTurnError, t]
  )
  const reportContext = useCallback(
    () => t('mascot.prompt.report', { error: (lastTurnError ?? '').slice(0, 600) }),
    [lastTurnError, t]
  )

  // Install (idempotent) the doctor plugin, open a fresh thread, and run `skill`.
  const startDoctorThread = useCallback(
    async (skill: string, context: string): Promise<void> => {
      setLocal({ kind: 'busy' })
      try {
        const store = usePluginStore.getState()
        if (store.plugins.length === 0) {
          try {
            await store.fetchPlugins()
          } catch {
            /* best effort; install below still attempts */
          }
        }
        const installed = usePluginStore.getState().plugins.some((p) => p.id === DOCTOR_PLUGIN_ID && p.installed)
        if (!installed) {
          await usePluginStore.getState().installPlugin(DOCTOR_PLUGIN_ID)
        }

        const res = (await window.api.appServer.sendRequest('thread/start', {
          identity: {
            channelName: 'dotcraft-desktop',
            userId: 'local',
            channelContext: `workspace:${workspacePath}`,
            workspacePath
          },
          historyMode: 'server'
        })) as { thread: ThreadSummary }

        const inputParts: InputPart[] = [
          { type: 'skillRef', name: skill },
          { type: 'text', text: context }
        ]
        useThreadStore.getState().addThread(res.thread)
        useUIStore.getState().setPendingWelcomeTurn({
          threadId: res.thread.id,
          text: context,
          inputParts,
          mode: 'agent'
        })
        useThreadStore.getState().setActiveThreadId(res.thread.id)
        useUIStore.getState().setActiveMainView('conversation')
        setLocal(null)
      } catch (err) {
        console.error('mascot doctor thread failed:', err)
        addToast(t('mascot.toast.startFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
        setLocal(null)
      }
    },
    [t, workspacePath]
  )

  // Entry point for diagnose/report: confirm install when the plugin is absent.
  const runDoctor = useCallback(
    async (skill: string, context: string): Promise<void> => {
      const store = usePluginStore.getState()
      if (store.plugins.length === 0) {
        try {
          await store.fetchPlugins()
        } catch {
          /* ignore */
        }
      }
      const installed = usePluginStore.getState().plugins.some((p) => p.id === DOCTOR_PLUGIN_ID && p.installed)
      if (installed) {
        void startDoctorThread(skill, context)
      } else {
        setLocal({ kind: 'confirmInstall', skill, context })
      }
    },
    [startDoctorThread]
  )

  const retry = useCallback(async (): Promise<void> => {
    let text = ''
    outer: for (let i = turns.length - 1; i >= 0; i--) {
      const items = turns[i].items
      for (let j = items.length - 1; j >= 0; j--) {
        const item = items[j]
        if (item.type === 'userMessage' && item.text && item.text.trim().length > 0) {
          text = item.text
          break outer
        }
      }
    }
    if (!text) {
      addToast(t('mascot.toast.retryNothing'), 'warning')
      return
    }
    setLocal(null)
    await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text,
      fallbackThreadName: text,
      renameThreadFromText: false
    })
  }, [t, threadId, turns, workspacePath])

  // Submit a ready-made prompt to the CURRENT thread (idle "summarize" / "keep going").
  const submitToCurrent = useCallback(
    async (text: string): Promise<void> => {
      setLocal(null)
      await startTurnWithOptimisticUI({
        threadId,
        workspacePath,
        text,
        fallbackThreadName: text,
        renameThreadFromText: false
      })
    },
    [threadId, workspacePath]
  )

  return useMemo<ComposerMascotInteraction | undefined>(() => {
    // Right-click presets. Failed turn -> recovery actions (persist even after the
    // bubble is dismissed). Idle -> a lightweight helper hub. Otherwise (running /
    // waiting) -> no menu.
    const menuItems: ContextMenuItem[] = isFailed
      ? [
          {
            label: t('mascot.menu.diagnose'),
            icon: createElement(Stethoscope, { size: 14 }),
            onClick: () => void runDoctor('error-diagnosis', diagnoseContext())
          },
          {
            label: t('mascot.menu.report'),
            icon: createElement(MessageSquareWarning, { size: 14 }),
            onClick: () => void runDoctor('report-issue', reportContext())
          },
          {
            label: t('mascot.menu.retry'),
            icon: createElement(RotateCcw, { size: 14 }),
            onClick: () => void retry()
          }
        ]
      : turnStatus === 'idle'
        ? [
            // Placeholder slot — will become a dynamically generated suggestion
            // (welcome-suggestion style) later.
            {
              label: t('mascot.menu.continue'),
              title: t('mascot.tip.continue'),
              icon: createElement(ArrowRight, { size: 14 }),
              onClick: () => void submitToCurrent(t('mascot.prompt.continue'))
            },
            {
              label: t('mascot.menu.summarize'),
              title: t('mascot.tip.summarize'),
              icon: createElement(FileText, { size: 14 }),
              onClick: () => void submitToCurrent(t('mascot.prompt.summarize'))
            },
            {
              label: t('mascot.menu.reportShort'),
              title: t('mascot.tip.report'),
              icon: createElement(MessageSquareWarning, { size: 14 }),
              onClick: () => void runDoctor('report-issue', t('mascot.prompt.reportGeneric'))
            }
          ]
        : []

    let expression: ComposerMascotInteraction['expression']
    let light: ComposerMascotInteraction['light']
    let bubble: ComposerMascotInteraction['bubble'] = null

    switch (local?.kind) {
      case 'error':
        expression = 'operator'
        light = 'error'
        bubble = {
          tone: 'error',
          title: t('mascot.error.title'),
          body: t('mascot.error.body'),
          actions: [
            { label: t('mascot.action.diagnose'), primary: true, onClick: () => void runDoctor('error-diagnosis', diagnoseContext()) },
            { label: t('mascot.action.report'), onClick: () => void runDoctor('report-issue', reportContext()) },
            { label: t('mascot.action.notNow'), onClick: dismiss }
          ]
        }
        break
      case 'success':
        expression = 'happy'
        light = 'success'
        bubble = {
          tone: 'success',
          title: t('mascot.success.title'),
          body: t('mascot.success.body'),
          actions: [{ label: t('mascot.action.ok'), onClick: dismiss }]
        }
        break
      case 'approval':
        expression = 'operator'
        bubble = {
          tone: 'warning',
          title: t('mascot.approval.title'),
          body: pendingApproval?.reason?.trim() || t('mascot.approval.body'),
          actions: [{ label: t('mascot.action.gotIt'), onClick: dismiss }]
        }
        break
      case 'confirmInstall':
        expression = 'operator'
        bubble = {
          tone: 'info',
          title: t('mascot.install.title'),
          body: t('mascot.install.body'),
          actions: [
            { label: t('mascot.install.confirm'), primary: true, onClick: () => void startDoctorThread(local.skill, local.context) },
            { label: t('mascot.action.cancel'), onClick: () => setLocal(null) }
          ]
        }
        break
      case 'busy':
        expression = 'operator'
        bubble = { tone: 'info', title: t('mascot.busy.title') }
        break
      default:
        if (turnStatus === 'running' || turnStatus === 'waitingApproval') expression = 'operator'
        break
    }

    if (!bubble && menuItems.length === 0 && expression === undefined && (light === undefined || light === 'default')) {
      return undefined
    }

    return {
      expression,
      light,
      bubble,
      menuItems
    }
  }, [
    isFailed,
    local,
    turnStatus,
    pendingApproval?.reason,
    t,
    runDoctor,
    retry,
    submitToCurrent,
    startDoctorThread,
    diagnoseContext,
    reportContext,
    dismiss
  ])
}
