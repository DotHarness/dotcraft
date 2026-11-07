import { getThreadConversation, selectLatestCreatePlanTurnId } from './conversationStore'
import { useThreadStore } from './threadStore'
import { useUIStore } from './uiStore'
import { normalizeThreadRuntime } from '../utils/threadRuntime'

export function applyThreadTurnRuntime(
  threadId: string | undefined,
  turn: Record<string, unknown>,
  running: boolean
): void {
  if (!threadId) return
  const threads = useThreadStore.getState()
  const previous = threads.runtimeSnapshots.get(threadId)
  if (!running && previous?.running && previous.activeTurnId && previous.activeTurnId !== turn.id) return
  const conversation = getThreadConversation(threadId)?.getState()
  const source = !running && conversation?.threadMode === 'plan' && conversation.plan
    ? selectLatestCreatePlanTurnId(conversation) : null
  const waitingOnPlanConfirmation = source != null && useUIStore.getState().planApprovalDismissed[source] !== true
  const runtime = normalizeThreadRuntime({
    running,
    activeTurnId: running ? String(turn.id) : null,
    activeTurnStartedAt: running && typeof turn.startedAt === 'string' ? turn.startedAt : null,
    busy: running,
    waitingOnApproval: false,
    waitingOnInput: false,
    waitingOnPlanConfirmation
  })
  threads.applyRuntimeSnapshot(threadId, runtime, {
    isActive: threads.activeThreadId === threadId,
    isDesktopOrigin: threads.threadList.find(thread => thread.id === threadId)?.originChannel?.toLowerCase() === 'dotcraft-desktop'
  })
}
