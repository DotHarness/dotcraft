export interface ThreadObservationTarget {
  threadId: string
  workspaceIdentity: string
  workspaceKey: string
  connectionEpoch: number
}

export interface ActiveThreadObservationState {
  connected: boolean
  workspaceIdentity: string
  workspaceKey: string
  connectionEpoch: number
  threadListWorkspaceKey: string | null
  threadIds: readonly string[]
}

export function resolveThreadObservationTarget(
  state: ActiveThreadObservationState,
  threadId: string
): ThreadObservationTarget | null {
  if (!state.connected) return null
  if (!state.workspaceIdentity || !state.workspaceKey) return null
  if (state.threadListWorkspaceKey !== state.workspaceKey) return null
  if (!state.threadIds.includes(threadId)) return null

  return {
    threadId,
    workspaceIdentity: state.workspaceIdentity,
    workspaceKey: state.workspaceKey,
    connectionEpoch: state.connectionEpoch
  }
}

export function threadObservationTargetKey(target: ThreadObservationTarget): string {
  return [
    target.workspaceIdentity,
    target.workspaceKey,
    String(target.connectionEpoch),
    target.threadId
  ].join('\u0000')
}

export function isSameThreadObservationTarget(
  left: ThreadObservationTarget | null | undefined,
  right: ThreadObservationTarget | null | undefined
): boolean {
  return left != null && right != null && threadObservationTargetKey(left) === threadObservationTargetKey(right)
}

export function isThreadObservationConnectionCurrent(
  target: ThreadObservationTarget,
  current: Pick<ThreadObservationTarget, 'workspaceIdentity' | 'workspaceKey' | 'connectionEpoch'>
): boolean {
  return target.workspaceIdentity === current.workspaceIdentity
    && target.workspaceKey === current.workspaceKey
    && target.connectionEpoch === current.connectionEpoch
}
