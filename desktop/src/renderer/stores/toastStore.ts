import { create } from 'zustand'

export type ToastType = 'info' | 'success' | 'warning' | 'error'

export interface ToastAction {
  label: string
  onClick: () => void
  /** Optional host-resolved glyph name (currently 'undo'); unknown/omitted names render no icon. */
  icon?: string
}

export interface Toast {
  id: string
  message: string
  type: ToastType
  duration: number
  /** Showing another toast with the same key replaces this one instead of stacking. */
  key?: string
  /** When true, message is rendered as Markdown (job results). */
  markdown?: boolean
  /** Optional inline action button (e.g. Undo). */
  action?: ToastAction
  /** Fired once if the toast goes away without the action being taken (timeout, close, or replacement). */
  onExpire?: () => void
}

interface ToastState {
  toasts: Toast[]
}

interface ToastActions {
  addToast(message: string, type?: ToastType, duration?: number, markdown?: boolean): string
  /** Low-level: push a fully-specified toast (without id). Returns the new id. */
  showToast(input: Omit<Toast, 'id'>): string
  /** Resolves an interactive toast exactly once; later calls for the same id are no-ops. */
  settleToast(id: string, via: 'action' | 'expire'): void
  /** Dismiss without the action; an unsettled toast commits via onExpire first. */
  removeToast(id: string): void
}

type ToastStore = ToastState & ToastActions

const DEFAULT_DURATION_MS = 5000
/** A toast that offers an action needs time to be read and reached. */
const ACTION_DURATION_MS = 8000
const JOB_RESULT_DURATION_MS = 10000

let toastCounter = 0
function nextToastId(): string {
  toastCounter += 1
  return `toast-${Date.now()}-${toastCounter}`
}

const settledIds = new Set<string>()

function settle(toast: Toast, via: 'action' | 'expire'): void {
  if (settledIds.has(toast.id)) return
  settledIds.add(toast.id)
  if (via === 'action') toast.action?.onClick()
  else toast.onExpire?.()
}

function isSameNotice(existing: Toast, next: Omit<Toast, 'id'>): boolean {
  return (
    existing.key == null &&
    existing.action == null &&
    existing.onExpire == null &&
    next.action == null &&
    next.onExpire == null &&
    existing.markdown === next.markdown &&
    existing.type === next.type &&
    existing.message === next.message
  )
}

export const useToastStore = create<ToastStore>((set, get) => ({
  toasts: [],

  addToast(message, type = 'info', duration = DEFAULT_DURATION_MS, markdown = false) {
    return get().showToast({ message, type, duration, ...(markdown ? { markdown: true } : {}) })
  },

  showToast(input) {
    const id = nextToastId()
    const replaced = get().toasts.filter((t) =>
      input.key != null ? t.key === input.key : isSameNotice(t, input)
    )
    for (const toast of replaced) {
      settle(toast, 'expire')
      settledIds.delete(toast.id)
    }
    const replacedIds = new Set(replaced.map((t) => t.id))
    set((s) => ({ toasts: [...s.toasts.filter((t) => !replacedIds.has(t.id)), { ...input, id }] }))
    return id
  },

  settleToast(id, via) {
    const toast = get().toasts.find((t) => t.id === id)
    if (toast) settle(toast, via)
  },

  removeToast(id) {
    const toast = get().toasts.find((t) => t.id === id)
    if (!toast) return
    settle(toast, 'expire')
    settledIds.delete(id)
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
  }
}))

/** Options for an interactive toast (action button and/or commit-on-expire callback). */
export interface ShowToastOptions {
  message: string
  type?: ToastType
  durationMs?: number
  key?: string
  markdown?: boolean
  action?: ToastAction
  onExpire?: () => void
}

/** Convenience helpers for non-React callers */
export const addToast = (
  message: string,
  type?: ToastType,
  duration?: number,
  markdown?: boolean
): string => useToastStore.getState().addToast(message, type, duration, markdown)

export const addJobResultToast = (message: string, markdown = true): string =>
  useToastStore.getState().addToast(message, 'info', JOB_RESULT_DURATION_MS, markdown)

export const showToast = (options: ShowToastOptions): string =>
  useToastStore.getState().showToast({
    message: options.message,
    type: options.type ?? 'info',
    duration: options.durationMs ?? (options.action ? ACTION_DURATION_MS : DEFAULT_DURATION_MS),
    ...(options.key ? { key: options.key } : {}),
    ...(options.markdown ? { markdown: true } : {}),
    ...(options.action ? { action: options.action } : {}),
    ...(options.onExpire ? { onExpire: options.onExpire } : {})
  })

export const removeToast = (id: string): void => useToastStore.getState().removeToast(id)
