import type { ComposerContextRecord } from '../../shared/composerContext'
import { focusComposer } from '../components/conversation/composerFocus'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { sideChatsOf, useSideChatStore, type SideChat, type SideChatPrompt } from '../stores/sideChatStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { isSubAgentThread } from './subAgentThreads'

type Translate = (key: string, vars?: Record<string, string | number>) => string

export function canOpenSideChat(): boolean {
  const { activeThreadId, activeThread } = useThreadStore.getState()
  return useConnectionStore.getState().capabilities?.sideChat === true
    && activeThreadId != null
    && activeThread?.id === activeThreadId
    && activeThread.status !== 'archived'
    && !isSubAgentThread(activeThread)
    && useConversationStore.getState().turns.length > 0
}

export async function openSideChat(
  parentThreadId: string,
  t: Translate,
  options: { prompt?: SideChatPrompt; contexts?: ComposerContextRecord[] } = {}
): Promise<string | null> {
  try {
    return await useSideChatStore.getState().open(parentThreadId, {
      ...options,
      label: (ordinal) => ordinal === 1 ? t('sideChat.tabTitle') : t('sideChat.tabTitleNumbered', { number: ordinal })
    })
  } catch (error) {
    addToast(t('sideChat.openFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
    return null
  }
}

export function openActiveSideChat(t: Translate): boolean {
  const parentThreadId = useThreadStore.getState().activeThreadId
  if (!parentThreadId || !canOpenSideChat()) return false
  void openSideChat(parentThreadId, t)
  return true
}

export function reopenSideChat(chat: SideChat, t: Translate): void {
  useViewerTabStore.getState().closeTab(chat.parentThreadId, chat.tabId)
  void openSideChat(chat.parentThreadId, t)
}

function showSideChat(chat: SideChat): void {
  useUIStore.getState().setActiveViewerTab(chat.tabId)
  useSideChatStore.getState().requestFocus(chat.key)
}

/** Finds the open side chat whose thread (or local key, before the fork exists) is `threadId`. */
export function findSideChat(chats: Map<string, SideChat>, threadId: string | null | undefined): SideChat | null {
  if (!threadId) return null
  for (const chat of chats.values()) {
    if (chat.status !== 'expired' && (chat.threadId === threadId || chat.key === threadId)) return chat
  }
  return null
}

export function showSideChatOfThread(threadId: string): void {
  const chat = findSideChat(useSideChatStore.getState().chats, threadId)
  if (chat) showSideChat(chat)
}

export function showMainThreadOf(chat: SideChat): void {
  useThreadStore.getState().setActiveThreadId(chat.parentThreadId)
  useUIStore.getState().setActiveMainView('conversation')
}

function currentSideChat(parentThreadId: string): SideChat | null {
  const chats = sideChatsOf(useSideChatStore.getState().chats, parentThreadId)
    .filter((chat) => chat.status !== 'expired')
  const active = useUIStore.getState().activeDetailTab
  const shown = active.kind === 'viewer' ? chats.find((chat) => chat.tabId === active.id) : undefined
  return shown ?? chats.sort((a, b) => b.ordinal - a.ordinal)[0] ?? null
}

export function askInSideChat(parentThreadId: string, context: ComposerContextRecord, t: Translate): void {
  const chat = currentSideChat(parentThreadId)
  if (!chat) {
    void openSideChat(parentThreadId, t, { contexts: [context] })
    return
  }
  useComposerContextStore.getState().addContext(chat.key, context)
  showSideChat(chat)
}

export function toggleSideChatFocus(): boolean {
  if (document.activeElement?.closest('[data-side-chat]')) {
    focusComposer()
    return true
  }
  const parentThreadId = useThreadStore.getState().activeThreadId
  const chat = parentThreadId ? currentSideChat(parentThreadId) : null
  if (!chat) return false
  showSideChat(chat)
  return true
}

export function useCanOpenSideChat(): boolean {
  const supported = useConnectionStore((state) => state.capabilities?.sideChat === true)
  const eligible = useThreadStore((state) =>
    state.activeThreadId != null
    && state.activeThread?.id === state.activeThreadId
    && state.activeThread.status !== 'archived'
    && !isSubAgentThread(state.activeThread))
  const started = useConversationStore((state) => state.turns.length > 0)
  return supported && eligible && started
}

const SIDE_COMMAND = /^\/(?:side|btw)(?=\s|$)\s*/i

export function parseSideSlashCommand(text: string): { question: string } | null {
  const trimmed = text.trim()
  const match = SIDE_COMMAND.exec(trimmed)
  return match ? { question: trimmed.slice(match[0].length).trim() } : null
}

export function withoutSideCommand(segments: ComposerDraftSegment[]): ComposerDraftSegment[] {
  const index = segments.findIndex((segment) => segment.type !== 'text' || segment.value.trim().length > 0)
  const first = segments[index]
  if (!first || first.type !== 'text') return segments
  const value = first.value.trimStart().replace(SIDE_COMMAND, '')
  return [...segments.slice(0, index), ...(value ? [{ ...first, value }] : []), ...segments.slice(index + 1)]
}
