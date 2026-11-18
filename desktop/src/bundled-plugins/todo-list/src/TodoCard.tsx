import type { DesktopPluginSurfaceProps } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { format, stringsFor } from './i18n'
import { progressOf, showsCard } from './model'
import { TodoChecklist } from './TodoChecklist'

export function TodoCard({ host, context }: DesktopPluginSurfaceProps<'conversation.turn.status'>): JSX.Element | null {
  const strings = stringsFor(host.environment.locale)
  const { plan, status } = context
  if (!plan || !showsCard(plan, status)) return null

  const { done, total } = progressOf(plan.todos)
  const live = status === 'running' && (plan.implementsPlan || plan.updatedTodos)
  return (
    <section className="todo-list-card" aria-label={`${strings.cardLabel}, ${format(strings.count, { done, total })}`}>
      <TodoChecklist todos={plan.todos} live={live} strings={strings} />
    </section>
  )
}
