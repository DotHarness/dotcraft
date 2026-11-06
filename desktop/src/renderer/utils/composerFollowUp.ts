import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import type { InputPart, QueuedTurnInput } from '../types/conversation'
import { enqueueTurn } from './enqueueTurn'

export type ComposerFollowUpResult = { queuedInput?: QueuedTurnInput; queuedInputs?: QueuedTurnInput[] } | null

export async function sendComposerFollowUp({
  clientUserMessageId = crypto.randomUUID(),
  mode,
  threadId,
  identityWorkspacePath,
  activeTurnId,
  input
}: {
  clientUserMessageId?: string
  mode: FollowUpQueueMode
  threadId: string
  identityWorkspacePath: string
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
  return await enqueueTurn({ identityWorkspacePath, threadId, input, clientUserMessageId, sender: undefined }) as ComposerFollowUpResult
}