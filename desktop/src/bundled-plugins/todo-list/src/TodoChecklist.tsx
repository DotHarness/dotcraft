import { Spinner } from '@dotcraft/plugin'
import { ChevronRight, Circle, CircleCheck, CircleDot, CircleX } from 'lucide-react'
import { useState, type JSX } from 'react'
import { format, type TodoListStrings } from './i18n'
import { foldedPrefix, type TodoItem, type TodoStatus } from './model'

const FOLD_AFTER = 6
const ICON = { size: 15, strokeWidth: 1.8, 'aria-hidden': true } as const

function StatusMark({ status, live }: { status: TodoStatus; live: boolean }): JSX.Element {
  return (
    <span className="todo-list-mark" data-status={status}>
      {status === 'completed' ? <CircleCheck {...ICON} />
        : status === 'cancelled' ? <CircleX {...ICON} />
          : status === 'in_progress' ? (live ? <Spinner size={13} /> : <CircleDot {...ICON} />)
            : <Circle {...ICON} />}
    </span>
  )
}

function statusLabel(status: TodoStatus, live: boolean, strings: TodoListStrings): string {
  switch (status) {
    case 'completed': return strings.statusDone
    case 'cancelled': return strings.statusCancelled
    case 'in_progress': return live ? strings.statusActive : strings.statusStopped
    default: return strings.statusPending
  }
}

export function TodoChecklist({
  todos,
  live,
  strings
}: {
  todos: readonly TodoItem[]
  live: boolean
  strings: TodoListStrings
}): JSX.Element {
  const [unfolded, setUnfolded] = useState(false)
  const folded = unfolded ? 0 : foldedPrefix(todos, FOLD_AFTER)
  const visible = todos.slice(folded)

  return (
    <ul className="todo-list-checklist">
      {folded > 0 ? (
        <li>
          <button type="button" className="todo-list-fold" onClick={() => setUnfolded(true)}>
            <span className="todo-list-mark" data-status="completed"><ChevronRight {...ICON} /></span>
            <span className="todo-list-text">{format(strings.foldedDone, { count: folded })}</span>
          </button>
        </li>
      ) : null}
      {visible.map((todo) => (
        <li key={todo.id} className="todo-list-item" data-status={todo.status}>
          <StatusMark status={todo.status} live={live} />
          <span className="todo-list-text">{todo.content}</span>
          <span className="todo-list-sr">, {statusLabel(todo.status, live, strings)}</span>
        </li>
      ))}
    </ul>
  )
}
