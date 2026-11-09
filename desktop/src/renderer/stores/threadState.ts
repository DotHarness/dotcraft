import {
  activateThreadConversation,
  createThreadConversation,
  getThreadConversation,
  releaseThreadConversation
} from './conversationStore'
import { activateThreadHistory, beginThreadHistory, releaseThreadHistory } from './threadHistoryStore'
import { releaseThreadConnection } from './threadConnectionStore'
import { useThreadStore } from './threadStore'
import { useSubAgentStore } from './subAgentStore'
import { releaseThreadElicitations } from './threadServerRequests'

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
  releaseThreadConnection(threadId)
  releaseThreadElicitations(threadId)
  useThreadStore.getState().releaseRetainedThread(threadId)
  useSubAgentStore.getState().clearParent(threadId)
}
