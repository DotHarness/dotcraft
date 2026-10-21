import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import type { InputPart, QueuedTurnInput } from '../types/conversation'

/** The server's queue after an enqueue, or null for a steer. */
export type ComposerFollowUpResult = { queuedInput?: QueuedTurnInput; queuedInputs?: QueuedTurnInput[] } | null

export async function sendComposerFollowUp({
  clientUserMessageId = crypto.randomUUID(),
  mode,
  threadId,
  activeTurnId,
  input
}: {
  clientUserMessageId?: string
  mode: FollowUpQueueMode
  threadId: string
  activeTurnId: string | null
  input: InputPart[]
}): Promise<ComposerFollowUpResult> {
  if (mode === 'steer') {
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) {
      throw new Error('The active turn is not ready for steering yet. Your draft was preserved.')
    }
    await window.api.appServer.sendRequest('turn/steer', {
      threadId,
      clientUserMessageId,
      expectedTurnId: activeTurnId,
      input,
      sender: undefined
    })
    return null
  }
  return await window.api.appServer.sendRequest('turn/enqueue', { threadId, input, clientUserMessageId, sender: undefined }) as ComposerFollowUpResult
}