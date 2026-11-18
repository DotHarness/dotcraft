import type { DesktopPluginPlanTodo, DesktopPluginTurnPlan, DesktopPluginTurnStatusContext } from '@dotcraft/plugin'

export type TodoItem = DesktopPluginPlanTodo
export type TodoStatus = TodoItem['status']

export function progressOf(todos: readonly TodoItem[]): { done: number; total: number } {
  const counted = todos.filter((todo) => todo.status !== 'cancelled')
  return { done: counted.filter((todo) => todo.status === 'completed').length, total: counted.length }
}

export function showsCard(plan: DesktopPluginTurnPlan, status: DesktopPluginTurnStatusContext['status']): boolean {
  const { title, todos, implementsPlan, updatedTodos } = plan
  if (todos.length === 0) return false
  if (title && !implementsPlan && !updatedTodos && todos.every((todo) => todo.status === 'pending')) return false
  const unfinished = todos.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  if (status === 'running') return unfinished || updatedTodos
  const lastLeftOpen = todos.every((todo, index) => todo.status === (index === todos.length - 1 ? 'in_progress' : 'completed'))
  return unfinished && !(status === 'completed' && lastLeftOpen)
}

/** Completed steps ahead of the first open one fold away once the list grows past `visibleLimit`. */
export function foldedPrefix(todos: readonly TodoItem[], visibleLimit: number): number {
  if (todos.length <= visibleLimit) return 0
  const firstOpen = todos.findIndex((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  const prefix = firstOpen < 0 ? todos.length : firstOpen
  return prefix >= 2 ? prefix : 0
}
