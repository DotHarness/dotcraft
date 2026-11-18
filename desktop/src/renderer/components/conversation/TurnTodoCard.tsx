import { ChevronRight, Circle, CircleCheck, CircleDot, CircleX } from 'lucide-react'
import { useEffect, useState, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { Spinner } from '../ui/Spinner'
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

const FOLD_AFTER = 6
const ICON = { size: 15, strokeWidth: 1.8, 'aria-hidden': true } as const

export function progressOf(todos: readonly TurnTodo[]): { done: number; total: number } {
  const counted = todos.filter((todo) => todo.status !== 'cancelled')
  return { done: counted.filter((todo) => todo.status === 'completed').length, total: counted.length }
}

export function showsTodoCard(plan: TurnTodoPlan, status: TurnTodoStatus): boolean {
  const { title, todos, implementsPlan, updatedTodos } = plan
  if (todos.length === 0) return false
  if (title && !implementsPlan && !updatedTodos && todos.every((todo) => todo.status === 'pending')) return false
  const unfinished = todos.some((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  if (status === 'running') return unfinished || updatedTodos
  const lastLeftOpen = todos.every((todo, index) => todo.status === (index === todos.length - 1 ? 'in_progress' : 'completed'))
  return unfinished && !(status === 'completed' && lastLeftOpen)
}

export function foldedPrefix(todos: readonly TurnTodo[], visibleLimit: number): number {
  if (todos.length <= visibleLimit) return 0
  const firstOpen = todos.findIndex((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  const prefix = firstOpen < 0 ? todos.length : firstOpen
  return prefix >= 2 ? prefix : 0
}

export function TurnTodoCard({ threadId, turnId, status, plan }: {
  threadId: string
  turnId: string
  status: TurnTodoStatus
  plan: TurnTodoPlan | null
}): JSX.Element | null {
  const t = useT()
  const taskCount = useRunningTaskCount(desktopSummaryServices, threadId)
  const shown = plan != null && showsTodoCard(plan, status)
  const showsTasks = shown && taskCount > 0
  useEffect(() => (showsTasks ? claimTurnTasks(turnId) : undefined), [showsTasks, turnId])
  if (!shown) return null

  const { done, total } = progressOf(plan.todos)
  const live = status === 'running' && (plan.implementsPlan || plan.updatedTodos)
  const tasksLabel = showsTasks
    ? t(taskCount === 1 ? 'conversation.runningTasks.one' : 'conversation.runningTasks.other', { count: taskCount })
    : null
  return (
    <section className={styles.card} aria-label={`${t('conversation.todo.label')}, ${t('conversation.todo.progress', { done, total })}`}>
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
  const [unfolded, setUnfolded] = useState(false)
  const folded = unfolded ? 0 : foldedPrefix(todos, FOLD_AFTER)
  const visible = todos.slice(folded)
  const tasksAnchor = (visible.find((todo) => todo.status === 'in_progress') ?? visible.at(-1))?.id

  return (
    <ul className={styles.checklist}>
      {folded > 0 ? (
        <li>
          <button type="button" className={styles.fold} onClick={() => setUnfolded(true)}>
            <span className={styles.mark} data-status="completed"><ChevronRight {...ICON} /></span>
            <span className={styles.text}>{t('conversation.todo.foldedDone', { count: folded })}</span>
          </button>
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
    </ul>
  )
}

function StatusMark({ status, live }: { status: TurnTodo['status']; live: boolean }): JSX.Element {
  return (
    <span className={styles.mark} data-status={status}>
      {status === 'completed' ? <CircleCheck {...ICON} />
        : status === 'cancelled' ? <CircleX {...ICON} />
          : status === 'in_progress' ? (live ? <Spinner size={13} /> : <CircleDot {...ICON} />)
            : <Circle {...ICON} />}
    </span>
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
