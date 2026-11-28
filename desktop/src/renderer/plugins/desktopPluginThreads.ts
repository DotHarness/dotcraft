import type {
  DesktopPluginLinkedChat,
  DesktopPluginLinkedChatState,
  DesktopPluginOwnedThread,
  DesktopPluginThreads
} from '@dotcraft/plugin'
import type { LinkedChatStatus, LinkedChatSummary } from '../../shared/linkedChats'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import { loadLinkedChats, trackLinkedChats, useLinkedChatsStore } from '../stores/linkedChatsStore'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { leaveSpace, useSpaceStore } from '../stores/spaceStore'
import { useThreadStore } from '../stores/threadStore'

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

function toOwnedThread(record: OwnedThreadRecord): DesktopPluginOwnedThread {
  return Object.freeze({
    threadId: record.threadId,
    workspacePath: record.workspacePath,
    ...(record.paused ? { paused: true } : {})
  })
}

function sameOwnedThread(a: OwnedThreadRecord | undefined, b: OwnedThreadRecord | undefined): boolean {
  return a?.threadId === b?.threadId
    && a?.workspacePath === b?.workspacePath
    && Boolean(a?.paused) === Boolean(b?.paused)
}

export function createDesktopPluginThreads(
  pluginId: string,
  own: (cleanup: () => void) => () => void
): DesktopPluginThreads {
  const findRecord = (key: string): OwnedThreadRecord | undefined =>
    useOwnedThreadsStore.getState().records.find((candidate) => candidate.pluginId === pluginId && candidate.key === key)

  return {
    async ensureOwned(request) {
      const record = await window.api.threadTasks.ensureOwned({ pluginId, ...request, config: { ...request.config } })
      const store = useOwnedThreadsStore.getState()
      store.setRecords([...store.records.filter((candidate) => candidate.pluginId !== pluginId || candidate.key !== record.key), record])
      return toOwnedThread(record)
    },
    findOwned(key): DesktopPluginOwnedThread | null {
      const record = findRecord(key)
      return record ? toOwnedThread(record) : null
    },
    onOwnedChange(key, listener) {
      const unsubscribe = useOwnedThreadsStore.subscribe((state, previous) => {
        const next = state.records.find((candidate) => candidate.pluginId === pluginId && candidate.key === key)
        const prev = previous.records.find((candidate) => candidate.pluginId === pluginId && candidate.key === key)
        if (!sameOwnedThread(next, prev)) {
          listener(next ? toOwnedThread(next) : null)
        }
      })
      return own(unsubscribe)
    },
    async setOwnedPaused(key, paused) {
      const record = findRecord(key)
      if (!record) return null
      const updated = await window.api.threadTasks.setOwnedPaused(record.threadId, paused)
      if (!updated) return null
      const store = useOwnedThreadsStore.getState()
      store.setRecords(store.records.map((candidate) => (candidate.threadId === updated.threadId ? updated : candidate)))
      return toOwnedThread(updated)
    },
    async archiveOwned(key) {
      const record = findRecord(key)
      if (!record) return false
      const wasViewing = useThreadStore.getState().activeThreadId === record.threadId
      const inSpace = Boolean(useSpaceStore.getState().returnTo) || wasViewing
      const archived = await window.api.threadTasks.archiveOwned(record.threadId)
      if (!archived) return false
      const store = useOwnedThreadsStore.getState()
      store.setRecords(store.records.filter((candidate) => candidate.threadId !== record.threadId))
      if (wasViewing) {
        useThreadStore.getState().setActiveThreadId(null)
        useThreadStore.getState().setActiveThread(null)
      }
      if (inSpace) await leaveSpace()
      return true
    },
    async listRunningThreads(options) {
      return window.api.threadTasks.listRunningThreads(options)
    },
    async startBackgroundCheckIn(request) {
      const record = findRecord(request.key)
      if (!record || record.paused) return { started: false }
      return window.api.threadTasks.startOwnedCheckIn({
        threadId: record.threadId,
        prompt: request.prompt,
        requireExistingTurns: request.requireExistingTurns,
        silentReplyToken: request.silentReplyToken,
        notificationTitle: request.notificationTitle
      })
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
