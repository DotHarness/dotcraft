import type {
  DesktopPluginLinkedChat,
  DesktopPluginLinkedChatState,
  DesktopPluginOwnedThread,
  DesktopPluginThreads
} from '@dotcraft/plugin'
import type { LinkedChatStatus, LinkedChatSummary } from '../../shared/linkedChats'
import { loadLinkedChats, trackLinkedChats, useLinkedChatsStore } from '../stores/linkedChatsStore'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'

const STATE: Record<LinkedChatStatus, DesktopPluginLinkedChatState> = {
  idle: 'idle',
  running: 'working',
  waitingForInput: 'waiting',
  completed: 'done',
  failed: 'failed',
  stopped: 'stopped'
}

function toPluginChat(chat: LinkedChatSummary): DesktopPluginLinkedChat {
  return Object.freeze({
    threadId: chat.threadId,
    projectId: chat.projectId,
    projectName: chat.projectName,
    workspacePath: chat.workspacePath,
    title: chat.title,
    state: STATE[chat.status],
    createdAt: chat.createdAt
  })
}

export function createDesktopPluginThreads(
  pluginId: string,
  own: (cleanup: () => void) => () => void
): DesktopPluginThreads {
  return {
    async ensureOwned(request) {
      const record = await window.api.threadTasks.ensureOwned({ pluginId, ...request, config: { ...request.config } })
      const store = useOwnedThreadsStore.getState()
      store.setRecords([...store.records.filter((candidate) => candidate.pluginId !== pluginId || candidate.key !== record.key), record])
      return { threadId: record.threadId, workspacePath: record.workspacePath }
    },
    findOwned(key): DesktopPluginOwnedThread | null {
      const record = useOwnedThreadsStore.getState().records
        .find((candidate) => candidate.pluginId === pluginId && candidate.key === key)
      return record ? { threadId: record.threadId, workspacePath: record.workspacePath } : null
    },
    async linked(parentThreadId) {
      return (await loadLinkedChats(parentThreadId)).map(toPluginChat)
    },
    onLinkedChange(parentThreadId, listener) {
      const untrack = trackLinkedChats(parentThreadId)
      const unsubscribe = useLinkedChatsStore.subscribe((state, previous) => {
        const next = state.byParent[parentThreadId]
        if (next && next !== previous.byParent[parentThreadId]) listener(next.map(toPluginChat))
      })
      return own(() => {
        unsubscribe()
        untrack()
      })
    }
  }
}
