import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import type { InputPart } from '../types/conversation'

export async function sendComposerFollowUp({
  mode,
  threadId,
  activeTurnId,
  input
}: {
  mode: FollowUpQueueMode
  threadId: string
  activeTurnId: string | null
  input: InputPart[]
}): Promise<void> {
  if (mode === 'steer') {
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) {
      throw new Error('The active turn is not ready for steering yet. Your draft was preserved.')
    }
    await window.api.appServer.sendRequest('turn/steer', {
      threadId,
      expectedTurnId: activeTurnId,
      input,
      sender: undefined
    })
  } else {
    await window.api.appServer.sendRequest('turn/enqueue', { threadId, input, sender: undefined })
  }
}
