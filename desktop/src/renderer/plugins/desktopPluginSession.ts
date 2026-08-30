import type { DesktopPluginSessionSnapshot } from '@dotcraft/plugin'

import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'

type SessionListener = (session: DesktopPluginSessionSnapshot) => void

const listeners = new Set<SessionListener>()
let stopWatching: (() => void) | null = null
let current = readDesktopPluginSession()

/** Assembled field for field like `components/layout/ConversationPanel.tsx`, so the two agree. */
export function readDesktopPluginSession(): DesktopPluginSessionSnapshot {
  const { turnStatus, threadMode } = useConversationStore.getState()
  return {
    workspacePath: useWorkspaceProjectsStore.getState().foregroundWorkspacePath || null,
    threadId: useThreadStore.getState().activeThread?.id ?? null,
    mode: threadMode,
    busy: turnStatus === 'running' || turnStatus === 'waitingInput'
  }
}

/** One Host-owned watcher serves every plugin, so no plugin subscribes to a renderer store. */
export function onDesktopPluginSessionChange(listener: SessionListener): () => void {
  listeners.add(listener)
  startWatching()
  return () => {
    if (!listeners.delete(listener)) return
    if (listeners.size === 0) {
      stopWatching?.()
      stopWatching = null
    }
  }
}

function startWatching(): void {
  if (stopWatching) return
  current = readDesktopPluginSession()
  const publish = (): void => {
    const next = readDesktopPluginSession()
    if (
      next.workspacePath === current.workspacePath
      && next.threadId === current.threadId
      && next.mode === current.mode
      && next.busy === current.busy
    ) return
    current = next
    for (const listener of [...listeners]) {
      try {
        listener(next)
      } catch (error) {
        console.error('Desktop Plugin session listener failed:', error)
      }
    }
  }

  const stops = [
    useWorkspaceProjectsStore.subscribe(publish),
    useThreadStore.subscribe(publish),
    useConversationStore.subscribe(publish)
  ]
  stopWatching = () => {
    for (const stop of stops) stop()
  }
}
