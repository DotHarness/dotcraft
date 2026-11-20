import type { HistoryItem, HistoryTurn } from './history'
import type { PlanTodo, TranscriptEntry } from './transcriptTypes'

export interface TodoItem extends PlanTodo {
  id: string
  activeForm?: string
}

export interface TodoPlan {
  title: string
  todos: TodoItem[]
}

export interface TodoCard {
  todos: TodoItem[]
  live: boolean
}

function text(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

export function todoStatus(value: unknown): PlanTodo['status'] {
  return value === 'in_progress' || value === 'completed' || value === 'cancelled' ? value : 'pending'
}

export function todoPlanOf(plan: { title?: unknown; todos?: unknown } | null | undefined): TodoPlan | null {
  if (!plan || !Array.isArray(plan.todos)) return null
  const todos = (plan.todos as Record<string, unknown>[]).flatMap((todo, index): TodoItem[] => {
    const content = text(todo?.content)
    if (!content) return []
    const activeForm = text(todo.activeForm)
    return [{ id: text(todo.id) || String(index), content, status: todoStatus(todo.status), ...(activeForm ? { activeForm } : {}) }]
  })
  return { title: text(plan.title), todos }
}

function isTodoWrite(item: HistoryItem): boolean {
  const presentation = (item.payload.presentation ?? null) as { presentationId?: unknown; options?: { operation?: unknown } } | null
  return presentation?.presentationId === 'core.todo' && presentation.options?.operation === 'todoWrite'
}

function updatedTodos(items: readonly HistoryItem[]): boolean {
  const calls = new Set(items.filter((item) => item.type === 'toolCall' && isTodoWrite(item)).map((item) => item.payload.callId))
  return items.some((item) => item.type === 'toolResult' && calls.has(item.payload.callId) && item.payload.success === true)
}

export function todoCard(
  plan: TodoPlan | null | undefined,
  turn: HistoryTurn | undefined,
  items: readonly HistoryItem[],
  running: boolean,
  planInput: string | null | undefined,
): TodoCard | null {
  if (!plan || plan.todos.length === 0 || !turn || turn.status === 'completed') return null
  const { todos } = plan
  const input = items.find((item) => item.type === 'userMessage' && item.payload.deliveryMode !== 'guidance')
  const implementsPlan = !!planInput && input?.payload.clientUserMessageId === planInput
  const updated = updatedTodos(items)
  if (!implementsPlan && !updated) return null
  const unfinished = todos.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  return unfinished || (running && updated) ? { todos, live: running } : null
}

export function withTodoCard(lines: TranscriptEntry[], card: TodoCard | null): TranscriptEntry[] {
  const last = lines.at(-1)
  if (!card || !last || last.kind === 'user') return lines
  const end = last.kind === 'thinking' ? lines.length - 1 : lines.length
  const opened = lines.findLastIndex((entry) => entry.kind === 'user')
  const reply = last.kind === 'thinking' && last.working
    ? -1
    : lines.findLastIndex((entry, index) => index > opened && entry.kind === 'assistant' && entry.copy)
  if (reply >= 0) return lines.map((entry, index) => (index === reply && entry.kind === 'assistant' ? { ...entry, todos: card } : entry))
  return [...lines.slice(0, end), { kind: 'todos', id: 'todos', card }, ...lines.slice(end)]
}
