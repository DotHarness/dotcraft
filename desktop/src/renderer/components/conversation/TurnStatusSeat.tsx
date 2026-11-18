import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { useMemo, type JSX } from 'react'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { useConversationThread } from './conversationAside/ConversationAside'
import { useConversationStore } from '../../stores/conversationStore'
import type { ConversationTurn } from '../../types/conversation'
import { resolveToolKind } from '../../utils/toolKind'
import { readPlanApprovalInput } from '../../utils/planApprovalInput'

type TurnEndStatus = DesktopPluginTurnStatusContext['status']

export function TurnStatusSeat({ turn, running }: { turn: ConversationTurn; running: boolean }): JSX.Element {
  const workspacePath = useConversationThread()?.workspacePath ?? null
  const { id: turnId, threadId } = turn
  const activeThreadId = useConversationStore.useThreadId()
  const snapshot = useConversationStore((state) => threadId === activeThreadId ? state.plan : null)
  const status = turnEndStatus(running, turn.status)
  const inputId = turn.items.find((item) => item.type === 'userMessage' && item.deliveryMode !== 'guidance')?.clientUserMessageId
  const implementsPlan = inputId != null && inputId === readPlanApprovalInput(threadId)
  const updatedTodos = turn.items.some((item) =>
    item.type === 'toolCall' && item.success === true && resolveToolKind(item) === 'todoWrite')
  const context = useMemo<DesktopPluginTurnStatusContext>(
    () => ({
      workspacePath, threadId, turnId, status,
      plan: snapshot && { title: snapshot.title, todos: snapshot.todos, implementsPlan, updatedTodos }
    }),
    [workspacePath, threadId, turnId, status, snapshot, implementsPlan, updatedTodos]
  )
  return <DesktopPluginSurface name="conversation.turn.status" context={context} />
}

function turnEndStatus(running: boolean, status: string): TurnEndStatus {
  if (running) return 'running'
  return status === 'completed' || status === 'failed' || status === 'cancelled' ? status : 'running'
}
