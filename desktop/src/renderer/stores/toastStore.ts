import { create } from 'zustand'

export type ToastType = 'info' | 'success' | 'warning' | 'error'

export interface Toast {
  id: string
  message: string
  type: ToastType
  duration: number
  /** When true, message is rendered as Markdown (job results). */
  markdown?: boolean
}

interface ToastState {
  toasts: Toast[]
}

interface ToastActions {
  addToast(
    message: string,
    type?: ToastType,
    duration?: number,
    markdown?: boolean
  ): void
  removeToast(id: string): void
}

type ToastStore = ToastState & ToastActions

const DEFAULT_DURATION_MS = 4000
const JOB_RESULT_DURATION_MS = 10000

export const useToastStore = create<ToastStore>((set) => ({
  toasts: [],

  addToast(message, type = 'info', duration = DEFAULT_DURATION_MS, markdown = false) {
    const id = `toast-${Date.now()}-${Math.random().toString(36).slice(2)}`
    set((s) => ({
      toasts: [...s.toasts, { id, message, type, duration, ...(markdown ? { markdown: true } : {}) }]
    }))
  },

  removeToast(id) {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }))
  }
}))

/** Convenience helpers for non-React callers */
export const addToast = (
  message: string,
  type?: ToastType,
  duration?: number,
  markdown?: boolean
): void => useToastStore.getState().addToast(message, type, duration, markdown)

export const addJobResultToast = (message: string, markdown = true): void =>
  useToastStore.getState().addToast(message, 'info', JOB_RESULT_DURATION_MS, markdown)
