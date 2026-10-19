import '@testing-library/jest-dom'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

// jsdom has no ResizeObserver, but components construct one unconditionally because
// the renderer is always Chromium. A no-op stand-in keeps them mountable here.
if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  } as unknown as typeof ResizeObserver
}

// jsdom has no idle callbacks either.
if (typeof window !== 'undefined' && typeof window.requestIdleCallback === 'undefined') {
  window.requestIdleCallback = () => 0
  window.cancelIdleCallback = () => {}
}

// jsdom has no editing commands; apply insertHTML and delete to the selection without undo history.
if (typeof document !== 'undefined' && typeof document.execCommand !== 'function') {
  document.execCommand = (command: string, _showUI?: boolean, value?: string): boolean => {
    const selection = window.getSelection()
    if (!selection?.rangeCount || (command !== 'insertHTML' && command !== 'delete')) return false
    const range = selection.getRangeAt(0)
    const start = range.startContainer
    const root = (start instanceof Element ? start : start.parentElement)?.closest('[contenteditable="true"]')
    range.deleteContents()
    if (command === 'insertHTML') range.insertNode(range.createContextualFragment(value ?? ''))
    root?.dispatchEvent(new InputEvent('input', { bubbles: true }))
    return true
  }
}

afterEach(() => {
  cleanup()
  vi.clearAllTimers()
  vi.useRealTimers()
})
