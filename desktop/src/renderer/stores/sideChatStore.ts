import { create } from 'zustand'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ConversationTurn, ImageAttachment, InputPart } from '../types/conversation'
import { sendComposerFollowUp } from '../utils/composerFollowUp'
import { buildComposerInputParts } from '../utils/composeInputParts'
import { createOptimisticUserMessage } from '../utils/inputPresentation'
import { parsePendingApproval, parseUserInputRequest } from '../utils/interactiveRequestParsing'
import { useComposerContextStore } from './composerContextStore'
import { useComposerDraftStore } from './composerDraftStore'
import { useConnectionStore } from './connectionStore'
import type { PendingApproval, PendingUserInputRequest } from './conversationStore'
import { isOptimisticTurn, removeRepresentedOptimisticTurns } from './optimisticMessages'
import {
  appendLiveText,
  applyTranscriptEvent,
  isTerminalTurnEvent,
  transcriptEventThreadId,
  type TranscriptLiveText
} from './subAgentTranscriptEvents'
import { useUIStore } from './uiStore'
import { useViewerTabStore } from './viewerTabStore'

export type SideChatStatus = 'starting' | 'ready' | 'expired'

export interface SideChat {
  key: string
  parentThreadId: string
  threadId: string | null
  tabId: string
  ordinal: number
  status: SideChatStatus
  turns: ConversationTurn[]
  live: TranscriptLiveText | null
  unread: boolean
  interruptingTurnId: string | null
  pendingApprovals: PendingApproval[]
  pendingUserInput: PendingUserInputRequest | null
}

export interface SideChatPrompt {
  text: string
  segments?: ComposerDraftSegment[]
  contexts?: ComposerContextRecord[]
  files?: ComposerFileAttachment[]
  images?: ImageAttachment[]
}

export interface SideChatInput {
  inputParts: InputPart[]
  visibleText: string
}

export interface OpenSideChatOptions {
  label: (ordinal: number) => string
  prompt?: SideChatPrompt
  contexts?: ComposerContextRecord[]
}

interface SideChatStore {
  chats: Map<string, SideChat>
  focusRequest: { key: string; nonce: number } | null
  open(parentThreadId: string, options: OpenSideChatOptions): Promise<string | null>
  /** Starts a turn, or guides the running one. */
  send(key: string, input: SideChatInput): Promise<void>
  interrupt(key: string): Promise<void>
  discard(key: string): void
  markRead(key: string): void
  requestFocus(key: string): void
  respondToApproval(key: string, bridgeId: string, decision: string): Promise<void>
  resolveUserInput(key: string): void
  handleServerRequest(method: string, bridgeId: string, params: Record<string, unknown>): boolean
  reset(): void
}

const DELTA_FLUSH_MS = 50

const readyByKey = new Map<string, Promise<string | null>>()
const pendingDeltas = new Map<string, { itemId: string; text: string }[]>()
const deltaTimers = new Map<string, ReturnType<typeof setTimeout>>()
let listening = false
let focusNonce = 0

export function isSideChatRunning(chat: SideChat): boolean {
  const last = chat.turns.at(-1)
  return last?.status === 'running' || last?.status === 'waitingApproval' || last?.status === 'waitingInput'
}

export function sideChatNeedsResponse(chat: SideChat): boolean {
  return chat.pendingApprovals.length > 0 || chat.pendingUserInput != null
}

export function sideChatsOf(chats: Map<string, SideChat>, parentThreadId: string): SideChat[] {
  return [...chats.values()].filter((chat) => chat.parentThreadId === parentThreadId)
}

function chatByThread(threadId: string): SideChat | undefined {
  for (const chat of useSideChatStore.getState().chats.values()) {
    if (chat.threadId === threadId) return chat
  }
  return undefined
}

function isTabVisible(chat: SideChat): boolean {
  const ui = useUIStore.getState()
  return ui.detailPanelVisible
    && ui.activeDetailTab.kind === 'viewer'
    && ui.activeDetailTab.id === chat.tabId
    && useViewerTabStore.getState().currentThreadId === chat.parentThreadId
}

function update(key: string, change: (chat: SideChat) => SideChat): void {
  useSideChatStore.setState((state) => {
    const current = state.chats.get(key)
    if (!current) return state
    const next = change(current)
    if (next === current) return state
    const chats = new Map(state.chats)
    chats.set(key, next)
    return { chats }
  })
}

function flushDeltas(key: string): void {
  clearTimeout(deltaTimers.get(key))
  deltaTimers.delete(key)
  const queued = pendingDeltas.get(key)
  pendingDeltas.delete(key)
  if (!queued?.length) return
  update(key, (chat) => {
    const timeline = queued.reduce(
      (current, delta) => appendLiveText(current, delta.itemId, delta.text),
      { turns: chat.turns, live: chat.live }
    )
    return timeline.live === chat.live ? chat : { ...chat, live: timeline.live }
  })
}

function queueDelta(key: string, itemId: unknown, delta: unknown): void {
  if (typeof itemId !== 'string' || typeof delta !== 'string') return
  const queued = pendingDeltas.get(key) ?? []
  queued.push({ itemId, text: delta })
  pendingDeltas.set(key, queued)
  if (!deltaTimers.has(key)) deltaTimers.set(key, setTimeout(() => flushDeltas(key), DELTA_FLUSH_MS))
}

function onNotification(method: string, params: Record<string, unknown>): void {
  const threadId = transcriptEventThreadId(params)
  const chat = threadId ? chatByThread(threadId) : undefined
  if (!chat) return
  const key = chat.key

  if (method === 'thread/deleted') {
    closeTab(chat)
    return
  }
  if (method === 'item/agentMessage/delta' || method === 'item/reasoning/delta') {
    queueDelta(key, params.itemId, params.delta)
    return
  }
  flushDeltas(key)
  if (method === 'item/approval/resolved') {
    const requestId = typeof params.requestId === 'string' ? params.requestId : null
    update(key, (current) => ({
      ...current,
      pendingApprovals: current.pendingApprovals.filter((approval) => requestId != null && approval.requestId !== requestId)
    }))
    return
  }
  if (method === 'item/tool/requestUserInput/resolved') {
    update(key, (current) => ({ ...current, pendingUserInput: null }))
    return
  }

  const next = applyTranscriptEvent({ turns: chat.turns, live: chat.live }, method, params)
  if (!next) return
  const terminal = isTerminalTurnEvent(method)
  update(key, (current) => ({
    ...current,
    turns: removeRepresentedOptimisticTurns(next.turns),
    live: next.live,
    ...(terminal
      ? {
          interruptingTurnId: null,
          pendingApprovals: [],
          pendingUserInput: null,
          unread: current.unread || !isTabVisible(current)
        }
      : {})
  }))
}

function closeTab(chat: SideChat): void {
  useViewerTabStore.getState().closeTab(chat.parentThreadId, chat.tabId)
  const ui = useUIStore.getState()
  if (ui.activeDetailTab.kind !== 'viewer' || ui.activeDetailTab.id !== chat.tabId) return
  const viewer = useViewerTabStore.getState()
  if (viewer.currentThreadId !== chat.parentThreadId) return
  const nextTabId = viewer.getThreadState(chat.parentThreadId).activeTabId
  if (nextTabId) ui.setActiveViewerTab(nextTabId)
  else ui.closeViewerTab()
}

function hasTab(chat: SideChat): boolean {
  return useViewerTabStore.getState().getThreadState(chat.parentThreadId).tabs.some((tab) => tab.id === chat.tabId)
}

function ensureListening(): void {
  if (listening) return
  listening = true
  window.api.appServer.onNotification((payload) => {
    if (payload.foreground === false) return
    onNotification(payload.method, (payload.params ?? {}) as Record<string, unknown>)
  })
  useConnectionStore.subscribe((state, previous) => {
    if (state.status === previous.status || state.status === 'connected') return
    useSideChatStore.setState((current) => {
      if (current.chats.size === 0) return current
      const chats = new Map(current.chats)
      for (const [key, chat] of chats) {
        chats.set(key, { ...chat, status: 'expired', live: null, pendingApprovals: [], pendingUserInput: null })
      }
      return { chats }
    })
  })
  useViewerTabStore.subscribe(() => {
    for (const chat of useSideChatStore.getState().chats.values()) {
      if (!hasTab(chat)) useSideChatStore.getState().discard(chat.key)
    }
  })
}

async function forkSideChat(key: string, parentThreadId: string): Promise<string | null> {
  try {
    const result = await window.api.appServer.sendRequest('thread/fork', {
      threadId: parentThreadId,
      ephemeral: true,
      sideChat: true
    }) as { thread?: { id?: string } }
    const threadId = result.thread?.id ?? null
    if (!threadId) throw new Error('thread/fork returned no thread')
    const chat = useSideChatStore.getState().chats.get(key)
    if (!chat) {
      void window.api.appServer.sendRequest('thread/delete', { threadId }).catch(() => {})
      return null
    }
    update(key, (current) => ({
      ...current,
      threadId,
      status: current.status === 'starting' ? 'ready' : current.status,
      turns: current.turns.map((turn) => turn.threadId ? turn : { ...turn, threadId })
    }))
    void window.api.appServer.sendRequest('thread/subscribe', { threadId }).catch(() => {})
    return threadId
  } catch (error) {
    const chat = useSideChatStore.getState().chats.get(key)
    if (chat) closeTab(chat)
    throw error
  }
}

export const useSideChatStore = create<SideChatStore>((set, get) => ({
  chats: new Map(),
  focusRequest: null,

  async open(parentThreadId, { label, prompt, contexts }) {
    ensureListening()
    const key = crypto.randomUUID()
    const ordinal = Math.max(0, ...sideChatsOf(get().chats, parentThreadId).map((chat) => chat.ordinal)) + 1
    const tabId = useViewerTabStore.getState().openSideChat({ threadId: parentThreadId, sideChatKey: key, label: label(ordinal) })
    if (useViewerTabStore.getState().currentThreadId === parentThreadId) {
      useUIStore.getState().setActiveViewerTab(tabId)
    }
    set((state) => {
      const chats = new Map(state.chats)
      chats.set(key, {
        key,
        parentThreadId,
        threadId: null,
        tabId,
        ordinal,
        status: 'starting',
        turns: [],
        live: null,
        unread: false,
        interruptingTurnId: null,
        pendingApprovals: [],
        pendingUserInput: null
      })
      return { chats }
    })
    if (contexts?.length) useComposerContextStore.getState().setContexts(key, contexts)
    const ready = forkSideChat(key, parentThreadId)
    readyByKey.set(key, ready)
    get().requestFocus(key)
    if (prompt) await get().send(key, buildComposerInputParts({ ...prompt, threadId: parentThreadId }))
    await ready
    return key
  },

  async send(key, { inputParts, visibleText }) {
    const chat = get().chats.get(key)
    if (!chat || chat.status === 'expired' || inputParts.length === 0) return
    const clientUserMessageId = crypto.randomUUID()
    const runningTurn = isSideChatRunning(chat) ? chat.turns.at(-1) : undefined
    if (runningTurn && chat.threadId) {
      await sendComposerFollowUp({
        clientUserMessageId,
        mode: 'steer',
        threadId: chat.threadId,
        activeTurnId: runningTurn.id,
        input: inputParts
      })
      return
    }
    const optimisticTurnId = `local-turn-${clientUserMessageId}`
    update(key, (current) => ({
      ...current,
      turns: [...current.turns, {
        id: optimisticTurnId,
        threadId: current.threadId ?? '',
        status: 'running',
        items: [createOptimisticUserMessage(inputParts, visibleText, clientUserMessageId)],
        startedAt: new Date().toISOString()
      }]
    }))
    try {
      const threadId = await (readyByKey.get(key) ?? Promise.resolve(chat.threadId))
      if (!threadId) throw new Error('The side chat is not ready.')
      await window.api.appServer.sendRequest('turn/start', { threadId, input: inputParts, clientUserMessageId })
    } catch (error) {
      update(key, (current) => ({
        ...current,
        turns: current.turns.filter((turn) => turn.id !== optimisticTurnId)
      }))
      throw error
    }
  },

  async interrupt(key) {
    const chat = get().chats.get(key)
    const turn = chat?.turns.at(-1)
    if (!chat?.threadId || !turn || isOptimisticTurn(turn) || !isSideChatRunning(chat)) return
    if (chat.interruptingTurnId === turn.id) return
    update(key, (current) => ({ ...current, interruptingTurnId: turn.id }))
    try {
      await window.api.appServer.sendRequest('turn/interrupt', { threadId: chat.threadId, turnId: turn.id })
    } catch (error) {
      update(key, (current) => ({ ...current, interruptingTurnId: null }))
      throw error
    }
  },

  discard(key) {
    const chat = get().chats.get(key)
    if (!chat) return
    set((state) => {
      const chats = new Map(state.chats)
      chats.delete(key)
      return { chats }
    })
    clearTimeout(deltaTimers.get(key))
    deltaTimers.delete(key)
    pendingDeltas.delete(key)
    useComposerContextStore.getState().clearContexts(key)
    useComposerDraftStore.getState().clearDraft(key)
    for (const approval of chat.pendingApprovals) {
      void window.api.appServer.sendServerResponse(approval.bridgeId, { decision: 'cancel' }).catch(() => {})
    }
    const ready = readyByKey.get(key) ?? Promise.resolve(chat.threadId)
    readyByKey.delete(key)
    void ready.then((threadId) => {
      if (threadId) void window.api.appServer.sendRequest('thread/delete', { threadId }).catch(() => {})
    }, () => {})
  },

  markRead(key) {
    update(key, (chat) => chat.unread ? { ...chat, unread: false } : chat)
  },

  requestFocus(key) {
    set({ focusRequest: { key, nonce: ++focusNonce } })
  },

  async respondToApproval(key, bridgeId, decision) {
    await window.api.appServer.sendServerResponse(bridgeId, { decision })
    update(key, (chat) => ({
      ...chat,
      pendingApprovals: chat.pendingApprovals.filter((approval) => approval.bridgeId !== bridgeId)
    }))
  },

  resolveUserInput(key) {
    update(key, (chat) => chat.pendingUserInput ? { ...chat, pendingUserInput: null } : chat)
  },

  handleServerRequest(method, bridgeId, params) {
    const threadId = typeof params.threadId === 'string' ? params.threadId : null
    const chat = threadId ? chatByThread(threadId) : undefined
    if (!chat) return false
    if (method === 'item/approval/request') {
      const approval = parsePendingApproval(bridgeId, params, chat.turns.at(-1)?.id ?? null)
      update(chat.key, (current) => ({
        ...current,
        pendingApprovals: [...current.pendingApprovals.filter((entry) => entry.requestId !== approval.requestId), approval]
      }))
      return true
    }
    if (method === 'item/tool/requestUserInput') {
      const request = parseUserInputRequest(bridgeId, params)
      if (!request) return false
      update(chat.key, (current) => ({ ...current, pendingUserInput: request }))
      return true
    }
    return false
  },

  reset() {
    for (const timer of deltaTimers.values()) clearTimeout(timer)
    deltaTimers.clear()
    pendingDeltas.clear()
    readyByKey.clear()
    set({ chats: new Map(), focusRequest: null })
  }
}))
