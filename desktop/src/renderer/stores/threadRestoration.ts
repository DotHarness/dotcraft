import { create } from 'zustand'

interface RestoreGate {
  threadId: string
  notifications: Array<() => void>
}

export const useThreadRestorationStore = create<{ restoringThreadId: string | null }>(() => ({
  restoringThreadId: null
}))

let gate: RestoreGate | null = null

export function beginThreadRestoration(threadId: string): RestoreGate {
  const next = { threadId, notifications: [] }
  gate = next
  useThreadRestorationStore.setState({ restoringThreadId: threadId })
  return next
}

export function isThreadRestoring(threadId: string | null): boolean {
  return threadId != null && gate?.threadId === threadId
}

export function stageThreadNotification(threadId: string | undefined, deliver: () => void): boolean {
  if (!threadId || gate?.threadId !== threadId) return false
  gate.notifications.push(deliver)
  return true
}

export function applyThreadRestorationBoundary(target: RestoreGate, apply: () => void): boolean {
  if (gate !== target) return false
  target.notifications = []
  apply()
  return true
}

export function finishThreadRestoration(target?: RestoreGate, deliverPending = false): void {
  if (target && gate !== target) return
  const previous = gate
  gate = null
  useThreadRestorationStore.setState({ restoringThreadId: null })
  if (deliverPending) for (const deliver of previous?.notifications ?? []) deliver()
}
