// Global test setup
// Extend expect with jest-dom matchers for DOM assertions
import '@testing-library/jest-dom'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  vi.clearAllTimers()
  vi.useRealTimers()
})
