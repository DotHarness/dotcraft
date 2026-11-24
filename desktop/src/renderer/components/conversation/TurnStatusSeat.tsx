import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { useMemo, type JSX } from 'react'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { useConversationThread } from './conversationAside/ConversationAside'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
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

export function TurnStatusSeat({ turn, leadingTurns, running, inReply = false }: {
  turn: ConversationTurn
  leadingTurns: readonly ConversationTurn[]
  running: boolean
  inReply?: boolean
}): JSX.Element {
  const workspacePath = useConversationThread()?.workspacePath ?? null
  const { id: turnId, threadId } = turn
  const activeThreadId = useConversationStore.useThreadId()
  const snapshot = useConversationStore((state) => threadId === activeThreadId ? state.plan : null)
  const status = turnEndStatus(running, turn.status)
  const approvalInput = readPlanApprovalInput(threadId)
  const implementsPlan = executesPlan(turn, approvalInput)
  const updatedTodos = wroteTodos(turn)
  const taskCount = useRunningTaskCount(desktopSummaryServices, threadId, status === 'running')
  const backgroundWork = useThreadStore((state) => state.runtimeSnapshots.get(threadId)?.hasBackgroundWork === true)
  const settled = status !== 'running' && !backgroundWork
  const context = useMemo<DesktopPluginTurnStatusContext>(
    () => ({
      workspacePath, threadId, turnId, status, settled,
      plan: snapshot && { title: snapshot.title, todos: snapshot.todos, implementsPlan, updatedTodos },
      runningTasks: { count: taskCount, open: () => setSummaryShown(true), claim: () => claimTurnTasks(turnId) }
    }),
    [workspacePath, threadId, turnId, status, settled, snapshot, implementsPlan, updatedTodos, taskCount]
  )
  const checklist = context.plan && {
    ...context.plan,
    implementsPlan: implementsPlan || leadingTurns.some((member) => executesPlan(member, approvalInput)),
    updatedTodos: updatedTodos || leadingTurns.some(wroteTodos)
  }
  const content = <>
    <TurnTodoCard threadId={threadId} turnId={turnId} status={status}
      waitingOnTasks={status === 'completed' && backgroundWork} plan={checklist} />
    <DesktopPluginSurface name="conversation.turn.status" context={context} />
  </>
  return inReply ? <div className={styles.reply}>{content}</div> : content
}

function executesPlan(turn: ConversationTurn, approvalInput: string | null): boolean {
  const inputId = turn.items.find((item) => item.type === 'userMessage' && item.deliveryMode !== 'guidance')?.clientUserMessageId
  return inputId != null && inputId === approvalInput
}

function wroteTodos(turn: ConversationTurn): boolean {
  return turn.items.some((item) =>
    item.type === 'toolCall' && item.success === true && resolveToolKind(item) === 'todoWrite')
}

function turnEndStatus(running: boolean, status: string): TurnEndStatus {
  if (running) return 'running'
  return status === 'completed' || status === 'failed' || status === 'cancelled' ? status : 'running'
}
