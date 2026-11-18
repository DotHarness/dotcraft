import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { useMemo, type JSX } from 'react'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { useConversationThread } from './conversationAside/ConversationAside'
import { useConversationStore } from '../../stores/conversationStore'
import type { ConversationTurn } from '../../types/conversation'
import { resolveToolKind } from '../../utils/toolKind'
import { readPlanApprovalInput } from '../../utils/planApprovalInput'
import { useRunningTaskCount } from './summary/store'
import { desktopSummaryServices } from './summary/summaryServices'
import { setSummaryShown } from './summary/view'
import { claimTurnTasks } from './turnTaskClaims'
import styles from './TurnStatusSeat.module.css'
import { TurnTodoCard } from './TurnTodoCard'

type TurnEndStatus = DesktopPluginTurnStatusContext['status']

export function TurnStatusSeat({ turn, running, inReply = false }: { turn: ConversationTurn; running: boolean; inReply?: boolean }): JSX.Element {
  const workspacePath = useConversationThread()?.workspacePath ?? null
  const { id: turnId, threadId } = turn
  const activeThreadId = useConversationStore.useThreadId()
  const snapshot = useConversationStore((state) => threadId === activeThreadId ? state.plan : null)
  const status = turnEndStatus(running, turn.status)
  const inputId = turn.items.find((item) => item.type === 'userMessage' && item.deliveryMode !== 'guidance')?.clientUserMessageId
  const implementsPlan = inputId != null && inputId === readPlanApprovalInput(threadId)
  const updatedTodos = turn.items.some((item) =>
    item.type === 'toolCall' && item.success === true && resolveToolKind(item) === 'todoWrite')
  const taskCount = useRunningTaskCount(desktopSummaryServices, threadId)
  const context = useMemo<DesktopPluginTurnStatusContext>(
    () => ({
      workspacePath, threadId, turnId, status,
      plan: snapshot && { title: snapshot.title, todos: snapshot.todos, implementsPlan, updatedTodos },
      runningTasks: { count: taskCount, open: () => setSummaryShown(true), claim: () => claimTurnTasks(turnId) }
    }),
    [workspacePath, threadId, turnId, status, snapshot, implementsPlan, updatedTodos, taskCount]
  )
  const content = <>
    <TurnTodoCard threadId={threadId} turnId={turnId} status={status} plan={context.plan} />
    <DesktopPluginSurface name="conversation.turn.status" context={context} />
  </>
  return inReply ? <div className={styles.reply}>{content}</div> : content
}

function turnEndStatus(running: boolean, status: string): TurnEndStatus {
  if (running) return 'running'
  return status === 'completed' || status === 'failed' || status === 'cancelled' ? status : 'running'
}
