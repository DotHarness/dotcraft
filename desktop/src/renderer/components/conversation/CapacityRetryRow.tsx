import { useCallback, useState } from 'react'
import { CapacityRetryControl } from './CapacityRetryControl'
import { selectCapacityRetryDelaySeconds, useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { reissueFailedTurn } from '../../utils/startTurn'

export function CapacityRetryRow(): JSX.Element | null {
  const delaySeconds = useConversationStore(selectCapacityRetryDelaySeconds)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const threadId = useThreadStore((s) => s.activeThreadId)
  const [busy, setBusy] = useState(false)

  const retry = useCallback(() => {
    if (!threadId || busy) return
    setBusy(true)
    void reissueFailedTurn({ threadId, workspacePath })
      .catch((err) => console.error('capacity retry failed:', err))
      .finally(() => setBusy(false))
  }, [threadId, workspacePath, busy])

  if (!threadId || turnStatus !== 'idle') return null

  return <CapacityRetryControl delaySeconds={delaySeconds} busy={busy} onRetry={retry} />
}
