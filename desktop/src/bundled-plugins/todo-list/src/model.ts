import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'

export type TodoStatus = 'pending' | 'in_progress' | 'completed' | 'cancelled'

export interface TodoItem {
  readonly id: string
  readonly content: string
  readonly status: TodoStatus
}

export interface PlanSnapshot {
  readonly title?: string
  readonly todos?: ReadonlyArray<{ readonly id?: string; readonly content?: string; readonly status?: string }>
}

export interface ThreadTodos {
  /** The list belongs to a CreatePlan plan, which the transcript already shows as its own card. */
  readonly fromPlan: boolean
  readonly items: readonly TodoItem[]
}

export function readTodos(plan: PlanSnapshot | null | undefined): ThreadTodos {
  return {
    fromPlan: Boolean(plan?.title?.trim()),
    items: (plan?.todos ?? []).flatMap((todo, index) => todo.content
      ? [{ id: todo.id ?? String(index), content: todo.content, status: (todo.status ?? 'pending') as TodoStatus }]
      : [])
  }
}

export function progressOf(todos: readonly TodoItem[]): { done: number; total: number } {
  const counted = todos.filter((todo) => todo.status !== 'cancelled')
  return { done: counted.filter((todo) => todo.status === 'completed').length, total: counted.length }
}

/**
 * Shows while the turn runs, then only while steps remain. A turn that completes with only its last step
 * still in progress counts that step as done, and an unstarted plan keeps to its own plan card.
 */
export function showsCard(todos: ThreadTodos, status: DesktopPluginTurnStatusContext['status']): boolean {
  const { items } = todos
  if (items.length === 0) return false
  if (todos.fromPlan && items.every((todo) => todo.status === 'pending')) return false
  if (status === 'running') return true
  const lastLeftOpen = items.every((todo, index) => todo.status === (index === items.length - 1 ? 'in_progress' : 'completed'))
  if (status === 'completed' && lastLeftOpen) return false
  return items.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
}

/** Completed steps ahead of the first open one fold away once the list grows past `visibleLimit`. */
export function foldedPrefix(todos: readonly TodoItem[], visibleLimit: number): number {
  if (todos.length <= visibleLimit) return 0
  const firstOpen = todos.findIndex((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  const prefix = firstOpen < 0 ? todos.length : firstOpen
  return prefix >= 2 ? prefix : 0
}
