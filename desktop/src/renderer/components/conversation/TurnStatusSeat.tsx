import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { useMemo, type JSX } from 'react'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { useConversationThread } from './conversationAside/ConversationAside'

type TurnEndStatus = DesktopPluginTurnStatusContext['status']

export function TurnStatusSeat({ threadId, turnId, status }: { threadId: string; turnId: string; status: TurnEndStatus }): JSX.Element {
  const workspacePath = useConversationThread()?.workspacePath ?? null
  const context = useMemo<DesktopPluginTurnStatusContext>(
    () => ({ workspacePath, threadId, turnId, status }),
    [workspacePath, threadId, turnId, status]
  )
  return <DesktopPluginSurface name="conversation.turn.status" context={context} />
}

/** A turn waiting on approval or input is still running. */
export function turnEndStatus(running: boolean, status: string): TurnEndStatus {
  if (running) return 'running'
  return status === 'completed' || status === 'failed' || status === 'cancelled' ? status : 'running'
}
