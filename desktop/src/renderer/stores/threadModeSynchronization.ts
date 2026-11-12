import { useConversationStore } from './conversationStore'
import type { ThreadMode } from '../types/conversation'
import { useThreadStore } from './threadStore'
import { useConnectionStore } from './connectionStore'

interface PendingModeChange {
  observedMode: ThreadMode | null
}

interface ModeScope {
  connectionEpoch: number
  workspaceKey: string
  threadId: string | null
  confirmedMode: ThreadMode | null
  metadataMode: ThreadMode | null
  pending: PendingModeChange | null
}

let scope: ModeScope | null = null

function configuredMode(configuration: Record<string, unknown> | null | undefined): ThreadMode | null {
  const mode = configuration?.mode
  return mode === 'agent' || mode === 'plan' ? mode : null
}

export function synchronizeThreadMode(workspaceKey: string): ModeScope {
  const threads = useThreadStore.getState()
  const threadId = threads.activeThreadId
  const mode = threads.activeThread?.id === threadId
    ? configuredMode(threads.activeThread.configuration)
    : null
  const connectionEpoch = useConnectionStore.getState().connectionEpoch
  if (!scope || scope.workspaceKey !== workspaceKey || scope.threadId !== threadId || scope.connectionEpoch !== connectionEpoch) {
    scope = { workspaceKey, connectionEpoch, threadId, confirmedMode: null, metadataMode: null, pending: null }
  }
  if (mode != null && mode !== scope.metadataMode) {
    scope.metadataMode = mode
    scope.confirmedMode = mode
    if (scope.pending) scope.pending.observedMode = mode
    else useConversationStore.getState().setThreadMode(mode)
  }
  return scope
}

export function resetThreadModeScope(): void {
  scope = null
}

export function confirmThreadMode(threadId: string, mode: ThreadMode): void {
  if (useThreadStore.getState().activeThreadId !== threadId) return
  if (scope && isSelected(scope)) {
    scope.confirmedMode = mode
    scope.metadataMode = mode
    if (scope.pending) scope.pending.observedMode = mode
  }
  useConversationStore.getState().setThreadMode(mode)
}

function isSelected(target: ModeScope): boolean {
  return scope === target
    && useConnectionStore.getState().connectionEpoch === target.connectionEpoch
    && useThreadStore.getState().activeThreadId === target.threadId
}

export async function requestThreadModeChange(threadId: string, mode: ThreadMode): Promise<boolean> {
  if (useThreadStore.getState().activeThreadId !== threadId) return false
  const target = synchronizeThreadMode(scope?.workspaceKey ?? '')
  if (target.pending) return false
  target.confirmedMode ??= useConversationStore.getState().threadMode
  const pending: PendingModeChange = { observedMode: null }
  target.pending = pending
  useConversationStore.getState().setThreadMode(mode)
  try {
    await window.api.appServer.sendRequest('thread/mode/set', { threadId, mode })
    if (!isSelected(target)) return false
    const confirmedMode = pending.observedMode ?? mode
    target.confirmedMode = confirmedMode
    useConversationStore.getState().setThreadMode(confirmedMode)
    return confirmedMode === mode
  } catch (error) {
    if (isSelected(target) && target.confirmedMode != null) {
      useConversationStore.getState().setThreadMode(target.confirmedMode)
    }
    throw error
  } finally {
    target.pending = null
  }
}
