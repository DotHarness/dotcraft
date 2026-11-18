import type { DesktopPluginTurnStatusContext } from '@dotcraft/plugin'
import { useMemo, type JSX } from 'react'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { useConversationThread } from './conversationAside/ConversationAside'

export function TurnStatusSeat({ threadId, turnId, running }: { threadId: string; turnId: string; running: boolean }): JSX.Element {
  const workspacePath = useConversationThread()?.workspacePath ?? null
  const context = useMemo<DesktopPluginTurnStatusContext>(
    () => ({ workspacePath, threadId, turnId, running }),
    [workspacePath, threadId, turnId, running]
  )
  return <DesktopPluginSurface name="conversation.turn.status" context={context} />
}
