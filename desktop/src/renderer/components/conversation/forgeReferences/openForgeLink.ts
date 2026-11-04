import type { MouseEvent } from 'react'
import { useConversationStore } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { openConversationLink } from '../../../utils/conversationDeepLink'
import { linkClickDestination, openWebLink } from '../../../utils/webLinks'

export async function openForgeLink(
  url: string,
  event: MouseEvent<HTMLElement>,
  t: (key: string) => string
): Promise<void> {
  const threadId = useThreadStore.getState().activeThreadId
  const workspacePath = useConversationStore.getState().workspacePath
  const destination = linkClickDestination(event)
  if (threadId && workspacePath) {
    await openConversationLink({ target: url, workspacePath, threadId, destination, t })
    return
  }
  await openWebLink({ url, destination: 'systemBrowser', threadId: null, t })
}
