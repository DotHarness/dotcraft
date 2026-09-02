import type { SubAgentChild } from '../stores/subAgentStore'
import type { ConversationItem } from '../types/conversation'
import type { ThreadSummary } from '../types/thread'

export function makeSubAgent(overrides: Partial<SubAgentChild> = {}): SubAgentChild {
  return {
    childThreadId: 'child-B', parentThreadId: 'parent-B', agentPath: '/root/review_core',
    nickname: 'Core B', agentRole: 'explorer', profileName: 'native', runtimeType: 'native',
    supportsClose: true, supportsResume: true, supportsSendInput: true,
    status: 'open', isCompleted: false, currentTool: null, lastToolDisplay: null,
    lastMessagePreview: null, inputTokens: 0, outputTokens: 0,
    runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false },
    ...overrides
  }
}

export function makeSubAgentThread(id: string, source?: ThreadSummary['source']): ThreadSummary {
  return { id, source, status: 'active', originChannel: 'desktop', createdAt: '', lastActiveAt: '' } as ThreadSummary
}

export function makeSpawn(id = 'spawn-1', overrides: Partial<ConversationItem> = {}): ConversationItem {
  return {
    id, type: 'toolCall', toolCallId: `call-${id}`, toolName: 'SpawnAgent', status: 'completed',
    source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
    presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
    arguments: { taskName: 'review_core', agentNickname: 'Core', message: 'Review changes' },
    result: JSON.stringify({ agentPath: '/root/review_core', agentNickname: 'Core', status: 'running' }),
    success: true, createdAt: '2026-09-02T00:00:00Z', ...overrides
  }
}

export function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}
