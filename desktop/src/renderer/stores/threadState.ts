import {
  activateThreadConversation,
  createThreadConversation,
  getThreadConversation,
  releaseThreadConversation
} from './conversationStore'
import { activateThreadHistory, beginThreadHistory, releaseThreadHistory } from './threadHistoryStore'

export function openThreadState(threadId: string | null): void {
  if (threadId && getThreadConversation(threadId)) {
    activateThreadConversation(threadId)
    activateThreadHistory(threadId)
    return
  }
  if (threadId) createThreadConversation(threadId)
  activateThreadConversation(threadId)
  beginThreadHistory(threadId)
}

export function releaseThreadState(threadId: string): void {
  releaseThreadConversation(threadId)
  releaseThreadHistory(threadId)
}
