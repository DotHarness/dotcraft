import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { format, stringsFor } from './i18n'
import { progressOf, showsCard } from './model'
import { useThreadTodos } from './planFeed'
import { TodoChecklist } from './TodoChecklist'

export function TodoCard({ host, context }: DesktopPluginSurfaceProps<'conversation.turn.status'>): JSX.Element | null {
  const strings = stringsFor(host.environment.locale)
  const todos = useThreadTodos(host, context.threadId)
  if (!todos || !showsCard(todos, context.status)) return null

  const { done, total } = progressOf(todos.items)
  return (
    <section className="todo-list-card" aria-label={`${strings.cardLabel}, ${format(strings.count, { done, total })}`}>
      <TodoChecklist todos={todos.items} live={context.status === 'running'} strings={strings} />
    </section>
  )
}
