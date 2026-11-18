import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { TodoCard } from './TodoCard'
import './styles/index.css'

export const activate: DesktopPluginActivate = (host) => {
  host.ui.add('conversation.turn.status', TodoCard)

  return {
    toolRenderers: [{ id: 'todo-write', presentationId: 'core.todo', component: () => null }]
  }
}
