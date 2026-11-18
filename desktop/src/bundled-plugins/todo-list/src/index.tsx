import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { startPlanFeed } from './planFeed'
import { TodoCard } from './TodoCard'
import './styles/index.css'

/** The card is the list's one place in the conversation, so TodoWrite calls leave no row. */
function HiddenTodoRow(): null {
  return null
}

export const activate: DesktopPluginActivate = (host) => {
  host.effect(() => startPlanFeed(host))
  host.ui.add('conversation.turn.status', TodoCard)

  return {
    toolRenderers: [{ id: 'todo-write', presentationId: 'core.todo', component: HiddenTodoRow }]
  }
}
