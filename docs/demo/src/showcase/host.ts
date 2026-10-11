import { useComposerContextStore } from '@renderer/stores/composerContextStore'
import { useToastStore } from '@renderer/stores/toastStore'
import { useUIStore } from '@renderer/stores/uiStore'
import { noteCopy, quote, showNote } from './notes'

type Params = Record<string, unknown>
type RequestHandler = (params: Params) => unknown
type Notification = { method: string; params?: unknown }

interface ShowcaseApi {
  appServer: {
    sendRequest: (method: string, params?: unknown, timeoutMs?: number) => Promise<unknown>
    onNotification: (listener: (payload: Notification) => void) => () => void
  }
  shell: { openExternal: (url: string) => Promise<void> }
  visualization: { copyImage: (rect: unknown) => Promise<void> }
}

const handlers = new Map<string, RequestHandler>()
const listeners = new Set<(payload: Notification) => void>()
let pendingToastNote: string | null = null
let pendingToastTimer: ReturnType<typeof setTimeout> | undefined

export function onRequest(method: string, handler: RequestHandler): void {
  handlers.set(method, handler)
}

export function noteInsteadOfNextToast(text: string): void {
  pendingToastNote = text
  clearTimeout(pendingToastTimer)
  pendingToastTimer = setTimeout(() => {
    pendingToastNote = null
  }, 1000)
}

export function notify(method: string, params: Params): void {
  for (const listener of listeners) listener({ method, params })
}

export function inputText(input: unknown): string {
  return Array.isArray(input) ? input.map((part) => (part as { text?: string }).text ?? '').join('').trim() : ''
}

// Embedded frames may lack the clipboard permission; a selection copy still works during the click.
function copyWithSelection(text: string): Promise<void> {
  const field = document.createElement('textarea')
  field.value = text
  field.setAttribute('readonly', '')
  field.style.cssText = 'position:fixed;top:0;left:0;opacity:0;pointer-events:none'
  document.body.appendChild(field)
  field.select()
  const copied = document.execCommand('copy')
  field.remove()
  return copied ? Promise.resolve() : Promise.reject(new Error('Copy was blocked.'))
}

export function installShowcaseHost(): void {
  const api = window.api as unknown as ShowcaseApi
  const fallback = api.appServer.sendRequest
  api.appServer.sendRequest = async (method, params, timeoutMs) => {
    const handler = handlers.get(method)
    return handler ? handler((params ?? {}) as Params) : fallback(method, params, timeoutMs)
  }
  api.appServer.onNotification = (listener) => {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  }
  api.shell.openExternal = async (url) => {
    window.open(url, '_blank', 'noopener,noreferrer')
  }
  api.visualization = {
    copyImage: async () => noteInsteadOfNextToast(noteCopy.copyImage)
  }

  const clipboard = navigator.clipboard
  if (clipboard) {
    const writeText = clipboard.writeText.bind(clipboard)
    clipboard.writeText = (text) => writeText(text).catch(() => copyWithSelection(text))
  }

  useToastStore.subscribe((state, previous) => {
    const added = state.toasts.filter((toast) => !previous.toasts.includes(toast))
    if (added.length === 0) return
    for (const toast of added) {
      showNote(pendingToastNote ?? toast.message)
      pendingToastNote = null
      state.removeToast(toast.id)
    }
  })
}

export function bridgeComposerActions(): void {
  useUIStore.subscribe((state) => {
    if (!state.composerPrefill) return
    showNote(noteCopy.prefill(quote(state.composerPrefill)))
    useUIStore.setState({ composerPrefill: null })
  })

  useComposerContextStore.subscribe((state) => {
    const threads = Object.keys(state.byThread).filter((threadId) => state.byThread[threadId].length > 0)
    if (threads.length === 0) return
    showNote(noteCopy.addChanges)
    for (const threadId of threads) state.clearContexts(threadId)
  })
}
