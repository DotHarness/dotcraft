import type { MouseEvent } from 'react'
import { useConversationStore } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { openConversationLink } from '../../../utils/conversationDeepLink'

export async function openForgeLink(
  url: string,
  event: MouseEvent<HTMLElement>,
  t: (key: string) => string
): Promise<void> {
  const forceNew = event.ctrlKey || event.metaKey
  const threadId = useThreadStore.getState().activeThreadId
  const workspacePath = useConversationStore.getState().workspacePath
  if (threadId && workspacePath) {
    await openConversationLink({ target: url, workspacePath, threadId, forceNew, t })
    return
  }
  await window.api.shell.openExternal(url).catch(() => undefined)
}
