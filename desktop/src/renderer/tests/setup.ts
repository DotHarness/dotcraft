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

afterEach(() => {
  cleanup()
  vi.clearAllTimers()
  vi.useRealTimers()
})
