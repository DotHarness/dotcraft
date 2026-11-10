import type { AppServerConnectionSource } from '../../shared/appServerBoundary'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ServerCapabilities } from '../stores/connectionStore'

export interface ThreadObservationTarget extends AppServerConnectionSource {
  threadId: string
  workspaceKey: string
  capabilities: ServerCapabilities | null
  originChannel?: string
}

export interface ActiveThreadObservationState {
  connected: boolean
  workspaceKey: string
  connectionKey: string | null
  connectionId: string | null
  capabilities: ServerCapabilities | null
  threadListWorkspaceKey: string | null
  threadIds: readonly string[]
}

export function resolveThreadObservationTarget(
  state: ActiveThreadObservationState,
  threadId: string,
  retained?: ThreadObservationTarget
): ThreadObservationTarget | null {
  if (!state.connected || !state.connectionKey || !state.connectionId) return null
  if (!state.workspaceKey || normalizeWorkspaceProjectKey(state.connectionKey) !== state.workspaceKey) return null
  if (retained?.threadId === threadId && retained.workspaceKey === state.workspaceKey
    && retained.connectionKey === state.connectionKey && retained.connectionId === state.connectionId) {
    return { ...retained, capabilities: state.capabilities }
  }
  if (state.threadListWorkspaceKey !== state.workspaceKey) return null
  if (!state.threadIds.includes(threadId)) return null

  return {
    threadId, workspaceKey: state.workspaceKey,
    connectionKey: state.connectionKey, connectionId: state.connectionId, capabilities: state.capabilities
  }
}

export function threadObservationTargetKey(target: ThreadObservationTarget): string {
  return [target.connectionKey, target.connectionId, target.threadId].join('\u0000')
}

export function isSameThreadObservationTarget(
  left: ThreadObservationTarget | null | undefined,
  right: ThreadObservationTarget | null | undefined
): boolean {
  return left != null && right != null && threadObservationTargetKey(left) === threadObservationTargetKey(right)
}
