import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import type { InputPart, QueuedTurnInput } from '../types/conversation'
import { enqueueTurn } from './enqueueTurn'
import { captureComposerAttachmentTarget } from './prepareComposerAttachments'
import type { TypedAppServerRequestApi } from '../../shared/appServerBoundary'

export type ComposerFollowUpResult = { queuedInput?: QueuedTurnInput; queuedInputs?: QueuedTurnInput[] } | null

export async function sendComposerFollowUp({
  clientUserMessageId = crypto.randomUUID(),
  mode,
  threadId,
  identityWorkspacePath,
  activeTurnId,
  input,
  request = captureComposerAttachmentTarget(threadId).request
}: {
  clientUserMessageId?: string
  mode: FollowUpQueueMode
  threadId: string
  identityWorkspacePath: string
  activeTurnId: string | null
  input: InputPart[]
  request?: TypedAppServerRequestApi['sendRequest']
}): Promise<ComposerFollowUpResult> {
  if (mode === 'steer') {
    if (!activeTurnId || activeTurnId.startsWith('local-turn-')) {
      throw new Error('The active turn is not ready for steering yet. Your draft was preserved.')
    }
    await request('turn/steer', {
      threadId,
      clientUserMessageId,
      expectedTurnId: activeTurnId,
      input,
      sender: undefined
    })
    return null
  }
  return await enqueueTurn({ request, identityWorkspacePath, threadId, input, clientUserMessageId, sender: undefined }) as ComposerFollowUpResult
}