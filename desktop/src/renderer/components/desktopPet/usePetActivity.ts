import { useEffect, useMemo, useRef } from 'react'
import { normalizeLocale, translate } from '../../../shared/locales'
import type { PetStatusInfo } from '../../../shared/desktopPet'
import { useConversationStore } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { derivePetActivity } from './petActivity'

export interface PetActivityHandle {
  /** Latest derived status; computed on demand while nobody is subscribed. */
  read(): PetStatusInfo | null
  /** Store-driven updates, throttled; the stores are only watched while someone listens. */
  subscribe(listener: () => void): () => void
  refresh(): void
  markRead(turnId: string): void
  /** Treat the current last turn as seen, so a thread switch or a detach never surfaces a stale Ready. */
  seedRead(): void
}

interface PetActivityController extends PetActivityHandle {
  dispose(): void
}

const THROTTLE_MS = 120
let warned = false

function createController(threadId: () => string | null): PetActivityController {
  let readTurnId: string | null = null
  let info: PetStatusInfo | null = null
  let serialized = ''
  let timer: ReturnType<typeof setTimeout> | undefined
  let unwatch: (() => void) | null = null
  const listeners = new Set<() => void>()

  const compute = (): PetStatusInfo => {
    const locale = normalizeLocale(document.documentElement.lang)
    const conversation = useConversationStore.getState()
    const thread = useThreadStore.getState().activeThread
    return derivePetActivity({
      locale,
      threadId: threadId(),
      threadTitle: thread?.displayName?.trim() || translate(locale, 'sidebar.newConversation'),
      turns: conversation.turns,
      turnStatus: conversation.turnStatus,
      activeTurnId: conversation.activeTurnId,
      interruptingTurnId: conversation.interruptingTurnId,
      approval: conversation.pendingApproval ?? conversation.genericApproval,
      pendingUserInput: conversation.pendingUserInput,
      streamingMessage: conversation.streamingMessage,
      streamingReasoning: conversation.streamingReasoning,
      changedFiles: conversation.changedFiles,
      readTurnId
    })
  }
  // A throw inside a store subscription would freeze the companion on its last state, so keep that state instead.
  const refresh = (): void => {
    let next: PetStatusInfo
    try {
      next = compute()
    } catch (error) {
      if (!warned) { warned = true; console.error('[desktop-pet] activity derivation failed', error) }
      return
    }
    const json = JSON.stringify(next)
    if (json === serialized) return
    serialized = json
    info = next
    for (const listener of listeners) listener()
  }
  const schedule = (): void => {
    if (timer) return
    timer = setTimeout(() => { timer = undefined; refresh() }, THROTTLE_MS)
  }
  const stopWatching = (): void => {
    unwatch?.()
    unwatch = null
    clearTimeout(timer)
    timer = undefined
  }
  return {
    read: () => { if (!unwatch) refresh(); return info },
    subscribe: (listener) => {
      listeners.add(listener)
      if (!unwatch) {
        const offConversation = useConversationStore.subscribe(schedule)
        const offThread = useThreadStore.subscribe(schedule)
        unwatch = () => { offConversation(); offThread() }
        refresh()
      }
      return () => {
        listeners.delete(listener)
        if (listeners.size === 0) stopWatching()
      }
    },
    refresh,
    markRead: (turnId) => { readTurnId = turnId; refresh() },
    seedRead: () => {
      const turns = useConversationStore.getState().turns
      const last = turns[turns.length - 1]
      readTurnId = last && last.status !== 'running' ? last.id : null
      refresh()
    },
    dispose: () => { stopWatching(); listeners.clear() }
  }
}

// Reads the stores imperatively and notifies through the handle, so the composer that hosts
// the hook never re-renders for the companion's sake.
export function usePetActivity(threadId: string | null): PetActivityHandle {
  const latest = useRef(threadId)
  latest.current = threadId
  const controller = useMemo(() => createController(() => latest.current), [])
  useEffect(() => { controller.seedRead() }, [controller, threadId])
  useEffect(() => () => controller.dispose(), [controller])
  return controller
}
