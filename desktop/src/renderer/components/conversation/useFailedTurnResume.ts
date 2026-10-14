import { useCallback, useRef } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { canResumeLastTurn } from '../../utils/failedTurnResume'
import { reissueFailedTurn } from '../../utils/startTurn'

interface FailedTurnResumeOptions {
  threadId: string
  workspacePath: string
  identityWorkspacePath?: string
  enabled: boolean
}

export function useFailedTurnResume({
  threadId,
  workspacePath,
  identityWorkspacePath,
  enabled
}: FailedTurnResumeOptions): { canResume: boolean; resume: () => Promise<void> } {
  const resumable = useConversationStore((s) => s.turnStatus === 'idle' && canResumeLastTurn(s.turns))
  const inFlightRef = useRef(false)

  const resume = useCallback(async (): Promise<void> => {
    if (inFlightRef.current) return
    inFlightRef.current = true
    try {
      await reissueFailedTurn({ threadId, workspacePath, identityWorkspacePath })
    } catch (err) {
      console.error('failed turn resume failed:', err)
    } finally {
      inFlightRef.current = false
    }
  }, [threadId, workspacePath, identityWorkspacePath])

  return { canResume: enabled && resumable, resume }
}
