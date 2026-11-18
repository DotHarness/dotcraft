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

/** Shows while the turn runs, then only while steps remain. An unstarted plan keeps to its own plan card. */
export function showsCard(todos: ThreadTodos, running: boolean): boolean {
  const open = todos.items.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  if (todos.fromPlan && todos.items.every((todo) => todo.status === 'pending')) return false
  return todos.items.length > 0 && (running || open)
}

/** Completed steps ahead of the first open one fold away once the list grows past `visibleLimit`. */
export function foldedPrefix(todos: readonly TodoItem[], visibleLimit: number): number {
  if (todos.length <= visibleLimit) return 0
  const firstOpen = todos.findIndex((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  const prefix = firstOpen < 0 ? todos.length : firstOpen
  return prefix >= 2 ? prefix : 0
}
