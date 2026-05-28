import type { ThreadGoal } from '../types/thread'

export type GoalSlashCommand =
  | { kind: 'show' }
  | { kind: 'set'; objective: string }
  | { kind: 'pause' }
  | { kind: 'resume' }
  | { kind: 'clear' }

export function parseGoalSlashCommand(text: string): GoalSlashCommand | null {
  const trimmed = text.trim()
  if (!trimmed.toLowerCase().startsWith('/goal')) return null
  const after = trimmed.slice('/goal'.length)
  if (after.length > 0 && !/^\s/.test(after)) return null
  const args = after.trim()
  if (!args) return { kind: 'show' }
  const normalized = args.toLowerCase()
  if (normalized === 'pause') return { kind: 'pause' }
  if (normalized === 'resume') return { kind: 'resume' }
  if (normalized === 'clear') return { kind: 'clear' }
  return { kind: 'set', objective: args }
}

export function extractGoal(result: unknown): ThreadGoal | null {
  if (result == null || typeof result !== 'object') return null
  const goal = (result as { goal?: unknown }).goal
  return isThreadGoal(goal) ? goal : null
}

export function isThreadGoal(value: unknown): value is ThreadGoal {
  if (value == null || typeof value !== 'object') return false
  const goal = value as Partial<ThreadGoal>
  return typeof goal.threadId === 'string'
    && typeof goal.goalId === 'string'
    && typeof goal.objective === 'string'
    && (goal.status === 'active'
      || goal.status === 'paused'
      || goal.status === 'budgetLimited'
      || goal.status === 'complete')
}

export function formatGoalUsage(goal: ThreadGoal): string {
  const total = Math.max(0, Math.trunc(goal.tokensUsed?.totalTokens ?? 0))
  if (typeof goal.tokenBudget === 'number' && Number.isFinite(goal.tokenBudget) && goal.tokenBudget > 0) {
    return `${formatNumber(total)} / ${formatNumber(goal.tokenBudget)} tokens`
  }
  if (total > 0) {
    return `${formatNumber(total)} tokens`
  }
  if (goal.timeUsedSeconds > 0) {
    return formatDuration(goal.timeUsedSeconds)
  }
  return ''
}

export function formatDuration(seconds: number): string {
  const total = Math.max(0, Math.trunc(seconds))
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  if (hours > 0) return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`
  if (minutes > 0) return `${minutes}m`
  return `${total}s`
}

function formatNumber(value: number): string {
  return value.toLocaleString()
}
