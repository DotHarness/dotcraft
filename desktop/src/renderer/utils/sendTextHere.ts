import { useConversationStore } from '../stores/conversationStore'
import { buildComposerInputParts } from './composeInputParts'
import { enqueueTurn } from './enqueueTurn'
import { startTurnWithOptimisticUI } from './startTurn'

export async function sendTextHere(threadId: string, workspacePath: string, text: string, fallbackThreadName: string): Promise<void> {
  if (useConversationStore.getState().turnStatus === 'idle') {
    await startTurnWithOptimisticUI({
      threadId,
      workspacePath,
      text,
      segments: [],
      images: [],
      files: [],
      fallbackThreadName,
      throwOnStartError: true
    })
    return
  }
  const { inputParts } = buildComposerInputParts({ text })
  await enqueueTurn({
    identityWorkspacePath: workspacePath,
    clientUserMessageId: crypto.randomUUID(),
    threadId,
    input: inputParts
  })
}
