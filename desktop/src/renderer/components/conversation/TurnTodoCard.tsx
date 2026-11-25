import { Circle, CircleCheck, CircleDot, CircleX } from 'lucide-react'
import { useEffect, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useRunningTaskCount } from './summary/store'
import { desktopSummaryServices } from './summary/summaryServices'
import { setSummaryShown } from './summary/view'
import { claimTurnTasks } from './turnTaskClaims'
import styles from './TurnTodoCard.module.css'

export interface TurnTodo {
  id: string
  content: string
  status: 'pending' | 'in_progress' | 'completed' | 'cancelled'
  activeForm?: string
}

export interface TurnTodoPlan {
  title: string
  todos: readonly TurnTodo[]
  implementsPlan: boolean
  updatedTodos: boolean
}

export type TurnTodoStatus = 'running' | 'completed' | 'failed' | 'cancelled'

const WINDOW_SIZE = 12
const ICON = { size: 15, strokeWidth: 1.8, 'aria-hidden': true } as const

export function progressOf(todos: readonly TurnTodo[]): { done: number; total: number } {
  const counted = todos.filter((todo) => todo.status !== 'cancelled')
  return { done: counted.filter((todo) => todo.status === 'completed').length, total: counted.length }
}

function showsTodoCard(plan: TurnTodoPlan, status: TurnTodoStatus, waitingOnTasks: boolean): boolean {
  const { todos, implementsPlan, updatedTodos } = plan
  if (todos.length === 0 || !(implementsPlan || updatedTodos)) return false
  if (status === 'completed') return waitingOnTasks
  const unfinished = todos.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  return unfinished || (status === 'running' && updatedTodos)
}

export function todoWindow(todos: readonly TurnTodo[], size = WINDOW_SIZE): {
  visible: readonly TurnTodo[]
  hiddenBefore: number
  openAfter: number
} {
  const active = todos.findIndex((todo) => todo.status === 'in_progress')
  const current = active >= 0 ? active : todos.findIndex((todo) => todo.status !== 'completed')
  const anchor = current >= 0 ? current : todos.length
  const start = Math.max(0, Math.min(anchor - 1, todos.length - size))
  const after = todos.slice(start + size)
  return {
    visible: todos.slice(start, start + size),
    hiddenBefore: start,
    openAfter: after.filter((todo) => todo.status !== 'completed').length
  }
}

export function TurnTodoCard({ threadId, turnId, status, waitingOnTasks, plan }: {
  threadId: string
  turnId: string
  status: TurnTodoStatus
  waitingOnTasks: boolean
  plan: TurnTodoPlan | null
}): JSX.Element | null {
  const t = useT()
  const taskCount = useRunningTaskCount(desktopSummaryServices, threadId, false)
  const shown = plan != null && showsTodoCard(plan, status, waitingOnTasks)
  const showsTasks = shown && status !== 'running' && taskCount > 0
  useEffect(() => (showsTasks ? claimTurnTasks(turnId) : undefined), [showsTasks, turnId])
  if (!shown) return null

  const { done, total } = progressOf(plan.todos)
  const live = status === 'running' || waitingOnTasks
  const tasksLabel = showsTasks
    ? t(taskCount === 1 ? 'conversation.runningTasks.one' : 'conversation.runningTasks.other', { count: taskCount })
    : null
  return (
    <section className={styles.card} data-turn-todo-card aria-label={`${t('conversation.todo.label')}, ${t('conversation.todo.progress', { done, total })}`}>
      <TodoChecklist todos={plan.todos} live={live} tasksLabel={tasksLabel} />
    </section>
  )
}

function TodoChecklist({ todos, live, tasksLabel }: {
  todos: readonly TurnTodo[]
  live: boolean
  tasksLabel: string | null
}): JSX.Element {
  const t = useT()
  const { visible, hiddenBefore, openAfter } = todoWindow(todos)
  const tasksAnchor = (visible.find((todo) => todo.status === 'in_progress') ?? visible.at(-1))?.id

  return (
    <ul className={styles.checklist}>
      {hiddenBefore > 0 ? (
        <li className={styles.windowNote}>
          {t(hiddenBefore === 1 ? 'conversation.todo.earlierSteps.one' : 'conversation.todo.earlierSteps.other', { count: hiddenBefore })}
        </li>
      ) : null}
      {visible.map((todo) => (
        <li key={todo.id} className={styles.item} data-status={todo.status}>
          <StatusMark status={todo.status} live={live} />
          <span className={styles.text}>{live && todo.status === 'in_progress' ? todo.activeForm ?? todo.content : todo.content}</span>
          <span className={styles.sr}>, {statusLabel(t, todo.status, live)}</span>
          {tasksLabel && todo.id === tasksAnchor ? (
            <button type="button" className={`dc-quiet-action ${styles.tasks}`} onClick={() => setSummaryShown(true)}>{tasksLabel}</button>
          ) : null}
        </li>
      ))}
      {openAfter > 0 ? (
        <li className={styles.windowNote}>
          {t(openAfter === 1 ? 'conversation.todo.moreToGo.one' : 'conversation.todo.moreToGo.other', { count: openAfter })}
        </li>
      ) : null}
    </ul>
  )
}

function StatusMark({ status, live }: { status: TurnTodo['status']; live: boolean }): JSX.Element {
  return (
    <span className={styles.mark} data-status={status}>
      {status === 'completed' ? <CircleCheck {...ICON} />
        : status === 'cancelled' ? <CircleX {...ICON} />
          : status === 'in_progress' ? (live ? <TodoSpinner /> : <CircleDot {...ICON} />)
            : <Circle {...ICON} />}
    </span>
  )
}

function TodoSpinner(): JSX.Element {
  return (
    <svg className={`dc-spinner ${styles.spinner}`} width={ICON.size} height={ICON.size} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle className="dc-spinner__track" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth={ICON.strokeWidth} />
      <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth={ICON.strokeWidth} strokeDasharray="47 63" />
    </svg>
  )
}

function statusLabel(t: ReturnType<typeof useT>, status: TurnTodo['status'], live: boolean): string {
  switch (status) {
    case 'completed': return t('conversation.todo.status.completed')
    case 'cancelled': return t('conversation.todo.status.cancelled')
    case 'in_progress': return live ? t('conversation.todo.status.active') : t('conversation.todo.status.stopped')
    default: return t('conversation.todo.status.pending')
  }
}
