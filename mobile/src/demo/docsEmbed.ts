// Web only: lets the docs site embed the demo. The message protocol is described in docs/demo/README.md.
import { router } from 'expo-router'
import { useSyncExternalStore } from 'react'
import { useColorScheme as useDeviceColorScheme } from 'react-native'
import type { ChatState } from '../core/chatState'
import type { MobileSession } from '../core/session'
import { stateOf } from '../core/state'
import { chatHref } from '../ui/screens/HomeScreen'

type Scheme = 'light' | 'dark'

const embedded = typeof window !== 'undefined' && window.parent !== window
const query = new URLSearchParams(globalThis.location?.search ?? '')
const listeners = new Set<() => void>()
let scheme = parseScheme(query.get('theme'))

function parseScheme(value: unknown): Scheme | null {
  return value === 'light' || value === 'dark' ? value : null
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

const getScheme = () => scheme

export function useColorScheme(): ReturnType<typeof useDeviceColorScheme> {
  const device = useDeviceColorScheme()
  return useSyncExternalStore(subscribe, getScheme, getScheme) ?? device
}

// The home screen is usable once the first chats arrive; a full sync takes several simulated round trips.
function ready(session: MobileSession): boolean {
  const state = session.store.getState()
  if (!state.hydrated) return false
  const first = state.order[0]
  const computer = first ? state.computers[first] : undefined
  return !first || Boolean(computer?.syncedAt) || Object.keys(computer?.chats ?? {}).length > 0
}

const CHAPTER_STATES: Record<string, ChatState> = { approval: 'needs-approval', question: 'needs-answer' }

function waitingChat(session: MobileSession, wanted: ChatState): { computerId: string; key: string } | null {
  const state = session.store.getState()
  const computerId = state.selected ?? state.order[0]
  const chats = computerId ? Object.values(state.computers[computerId]?.chats ?? {}) : []
  const chat = chats
    .filter((entry) => stateOf(entry) === wanted)
    .sort((left, right) => Date.parse(right.updatedAt ?? '') - Date.parse(left.updatedAt ?? ''))[0]
  return computerId && chat ? { computerId, key: chat.key } : null
}

let stopWaiting: (() => void) | null = null

function openChapter(session: MobileSession, chapter: string | null): void {
  stopWaiting?.()
  stopWaiting = null
  if (router.canDismiss()) router.dismissAll()
  const wanted = chapter ? CHAPTER_STATES[chapter] : undefined
  if (!wanted) return
  const open = (): boolean => {
    const target = waitingChat(session, wanted)
    if (target) router.push(chatHref(target.computerId, target.key))
    return Boolean(target)
  }
  if (open()) return
  const unsubscribe = session.store.subscribe(() => {
    if (!open()) return
    unsubscribe()
    stopWaiting = null
  })
  stopWaiting = unsubscribe
}

export function embedInDocs(session: MobileSession): void {
  if (!embedded) return
  let mounted = false

  window.addEventListener('message', (event) => {
    if (event.source !== window.parent) return
    const data = event.data as { type?: unknown; theme?: unknown; chapter?: unknown } | null
    if (data?.type === 'dotcraft-showcase:show') {
      if (mounted) openChapter(session, typeof data.chapter === 'string' ? data.chapter : null)
      return
    }
    if (data?.type !== 'dotcraft-demo:set-theme') return
    const next = parseScheme(data.theme)
    if (!next || next === scheme) return
    scheme = next
    listeners.forEach((listener) => listener())
  })

  // Timers, not animation frames: browsers throttle frames for an iframe the page is still covering.
  const frame = (run: () => void) => setTimeout(run, 50)
  const announce = () =>
    frame(() => {
      mounted = true
      openChapter(session, query.get('chapter'))
      frame(() => window.parent.postMessage({ type: 'dotcraft-showcase:ready' }, '*'))
    })
  if (ready(session)) announce()
  else {
    const unsubscribe = session.store.subscribe(() => {
      if (!ready(session)) return
      unsubscribe()
      announce()
    })
  }
}
