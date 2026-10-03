import type { ThreadRuntimeState } from '@dotcraft/sdk/contracts'

export type ChatState = 'running' | 'needs-approval' | 'needs-answer' | 'done' | 'failed'

export function chatState(runtime: ThreadRuntimeState | null, lastTurnFailed: boolean): ChatState {
  if (runtime?.waitingOnApproval) return 'needs-approval'
  if (runtime?.waitingOnInput) return 'needs-answer'
  if (runtime?.running || runtime?.activeTurnId) return 'running'
  return lastTurnFailed ? 'failed' : 'done'
}

export function needsYou(state: ChatState): boolean {
  return state === 'needs-approval' || state === 'needs-answer'
}

export function isLive(state: ChatState): boolean {
  return state !== 'done' && state !== 'failed'
}

export function followUpMethod(runtime: ThreadRuntimeState | null): 'start' | 'steer' | 'enqueue' {
  if (!runtime || (!runtime.running && !runtime.busy && !runtime.activeTurnId)) return 'start'
  if (runtime.activeTurnId && !runtime.maintenanceKind) return 'steer'
  return 'enqueue'
}
