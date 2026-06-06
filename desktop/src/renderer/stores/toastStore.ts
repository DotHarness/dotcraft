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
  /** When true, message is rendered as Markdown (job results). */
  markdown?: boolean
  /** Optional inline action button (e.g. Undo). */
  action?: ToastAction
  /**
   * Fired once if the toast is dismissed by timeout or close WITHOUT the action
   * being taken — e.g. an undo window elapsing should commit the pending change.
   */
  onExpire?: () => void
}

interface ToastState {
  toasts: Toast[]
}

interface ToastActions {
  addToast(message: string, type?: ToastType, duration?: number, markdown?: boolean): string
  /** Low-level: push a fully-specified toast (without id). Returns the new id. */
  showToast(input: Omit<Toast, 'id'>): string
  removeToast(id: string): void
}

type ToastStore = ToastState & ToastActions

const DEFAULT_DURATION_MS = 4000
const JOB_RESULT_DURATION_MS = 10000

let toastCounter = 0
function nextToastId(): string {
  toastCounter += 1
  return `toast-${Date.now()}-${toastCounter}`
}

export const useToastStore = create<ToastStore>((set, get) => ({
  toasts: [],

  addToast(message, type = 'info', duration = DEFAULT_DURATION_MS, markdown = false) {
    return get().showToast({ message, type, duration, ...(markdown ? { markdown: true } : {}) })
  },

  showToast(input) {
    const id = nextToastId()
    set((s) => ({ toasts: [...s.toasts, { ...input, id }] }))
    return id
  },

  removeToast(id) {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
  }
}))

/** Options for an interactive toast (action button and/or commit-on-expire callback). */
export interface ShowToastOptions {
  message: string
  type?: ToastType
  durationMs?: number
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
    duration: options.durationMs ?? DEFAULT_DURATION_MS,
    ...(options.markdown ? { markdown: true } : {}),
    ...(options.action ? { action: options.action } : {}),
    ...(options.onExpire ? { onExpire: options.onExpire } : {})
  })

export const removeToast = (id: string): void => useToastStore.getState().removeToast(id)
