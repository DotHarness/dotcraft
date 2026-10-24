import { useEffect } from 'react'
import { useConversationStore } from '../stores/conversationStore'
import type { ThreadMode } from '../types/conversation'
import { useThreadStore } from '../stores/threadStore'

interface PendingModeChange {
  observedMode: ThreadMode | null
}

interface ModeScope {
  workspaceKey: string
  threadId: string | null
  confirmedMode: ThreadMode | null
  metadataMode: ThreadMode | null
  pending: PendingModeChange | null
}

let workspaceKey = ''
let scope: ModeScope | null = null

function configuredMode(configuration: Record<string, unknown> | null | undefined): ThreadMode | null {
  const mode = configuration?.mode
  return mode === 'agent' || mode === 'plan' ? mode : null
}

function synchronizeMode(): ModeScope {
  const threads = useThreadStore.getState()
  const threadId = threads.activeThreadId
  const mode = threads.activeThread?.id === threadId
    ? configuredMode(threads.activeThread.configuration)
    : null
  if (!scope || scope.workspaceKey !== workspaceKey || scope.threadId !== threadId) {
    scope = { workspaceKey, threadId, confirmedMode: null, metadataMode: null, pending: null }
  }
  if (mode != null && mode !== scope.metadataMode) {
    scope.metadataMode = mode
    scope.confirmedMode = mode
    if (scope.pending) scope.pending.observedMode = mode
    else useConversationStore.getState().setThreadMode(mode)
  }
  return scope
}

function isSelected(target: ModeScope): boolean {
  return scope === target
    && workspaceKey === target.workspaceKey
    && useThreadStore.getState().activeThreadId === target.threadId
}

export async function requestThreadModeChange(threadId: string, mode: ThreadMode): Promise<boolean> {
  if (useThreadStore.getState().activeThreadId !== threadId) return false
  const target = synchronizeMode()
  if (target.pending) return false
  target.confirmedMode ??= useConversationStore.getState().threadMode
  const token: PendingModeChange = { observedMode: null }
  target.pending = token
  useConversationStore.getState().setThreadMode(mode)
  try {
    await window.api.appServer.sendRequest('thread/mode/set', { threadId, mode })
    if (!isSelected(target) || target.pending !== token) return false
    const confirmedMode = token.observedMode ?? mode
    target.confirmedMode = confirmedMode
    useConversationStore.getState().setThreadMode(confirmedMode)
    return confirmedMode === mode
  } catch (error) {
    if (isSelected(target) && target.pending === token && target.confirmedMode != null) {
      useConversationStore.getState().setThreadMode(target.confirmedMode)
    }
    throw error
  } finally {
    if (target.pending === token) target.pending = null
  }
}

export function useActiveThreadModeSync(selectedWorkspaceKey: string): void {
  const activeThreadId = useThreadStore((state) => state.activeThreadId)
  const activeThread = useThreadStore((state) => state.activeThread)

  useEffect(() => {
    workspaceKey = selectedWorkspaceKey
    synchronizeMode()
    const unsubscribe = useThreadStore.subscribe((state, previous) => {
      if (state.activeThreadId !== previous.activeThreadId) {
        scope = null
      } else if (scope) {
        synchronizeMode()
      }
    })
    return () => {
      unsubscribe()
      scope = null
    }
  }, [selectedWorkspaceKey])

  useEffect(() => {
    synchronizeMode()
  }, [activeThread, activeThreadId, selectedWorkspaceKey])
}
