import { useMemo } from 'react'
import { useComposerPreferencesStore } from '../../stores/composerPreferencesStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { ConversationTurn, QueuedTurnInput } from '../../types/conversation'
import { isVisibleQueuedInput } from '../../utils/visibleUserMessage'

export interface ComposerRuntime {
  turns: ConversationTurn[]
  running: boolean
  interrupting: boolean
  onStop: () => void
}

const NO_QUEUED_INPUTS: QueuedTurnInput[] = []

export function useComposerRunState(external: boolean, runtime: ComposerRuntime | undefined) {
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const pendingMessage = useConversationStore((s) => s.pendingMessage)
  const allQueuedInputs = useConversationStore((s) => s.queuedInputs)
  const queuedInputs = useMemo(() => allQueuedInputs.filter(isVisibleQueuedInput), [allQueuedInputs])
  const interruptingTurnId = useConversationStore((s) => s.interruptingTurnId)
  const maintenanceKind = useConversationStore((s) => s.maintenanceKind)
  const followUpPreference = useComposerPreferencesStore((state) => state.followUpQueueMode)

  const isRunning = external ? runtime?.running === true : turnStatus === 'running'
  const isWaitingApproval = !external && turnStatus === 'waitingApproval'
  const isWaitingInput = !external && turnStatus === 'waitingInput'
  const isMaintenanceActive = !external && maintenanceKind === 'compacting'
  const lastTurnStatus = turns[turns.length - 1]?.status
  const queuePaused = !external && turnStatus === 'idle' && lastTurnStatus === 'cancelled' && queuedInputs.length > 0
  return {
    turns,
    historyTurns: runtime?.turns ?? turns,
    turnStatus,
    activeTurnId,
    visiblePendingMessage: external ? null : pendingMessage,
    visibleQueuedInputs: external ? NO_QUEUED_INPUTS : queuedInputs,
    stopping: external ? runtime?.interrupting === true : interruptingTurnId != null,
    isRunning,
    isWaitingApproval,
    isWaitingInput,
    isMaintenanceActive,
    isBusyForInput: isRunning || isMaintenanceActive,
    queuePaused,
    followUpMode: isRunning ? (runtime ? 'steer' as const : followUpPreference) : 'queue' as const
  }
}
