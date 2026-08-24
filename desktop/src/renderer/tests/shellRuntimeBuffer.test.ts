import { describe, expect, it, vi } from 'vitest'
import {
  createShellRuntimeBuffer,
  limitShellRuntimeOutput,
  mergeShellRuntimeUpdates,
  SHELL_RUNTIME_MAX_CHARS,
  SHELL_RUNTIME_TRUNCATION_MARKER
} from '../stores/shellRuntimeBuffer'

describe('shell runtime buffer limits', () => {
  it('keeps the most recent live output within the renderer budget', () => {
    const source = `${'old'.repeat(10_000)}LATEST`
    const limited = limitShellRuntimeOutput(source)

    expect(limited).toHaveLength(SHELL_RUNTIME_MAX_CHARS)
    expect(limited.startsWith(SHELL_RUNTIME_TRUNCATION_MARKER)).toBe(true)
    expect(limited.endsWith('LATEST')).toBe(true)
  })

  it('leaves shared shell buffering unchanged unless a limiter is supplied', () => {
    const output = 'x'.repeat(SHELL_RUNTIME_MAX_CHARS + 1)
    const updates = new Map([['call-1', {
      source: 'terminal' as const,
      output,
      replace: true
    }]])

    expect(mergeShellRuntimeUpdates(new Map(), updates).get('call-1')?.output).toBe(output)
  })

  it('bounds both pending batches and committed output when explicitly enabled', () => {
    vi.useFakeTimers()
    let state = new Map()
    const buffer = createShellRuntimeBuffer((updates) => {
      state = mergeShellRuntimeUpdates(state, updates, limitShellRuntimeOutput)
    }, { transformOutput: limitShellRuntimeOutput })

    buffer.queue('call-1', 'terminal', 'a'.repeat(SHELL_RUNTIME_MAX_CHARS), true)
    buffer.queue('call-1', 'terminal', 'tail', false)
    vi.advanceTimersByTime(50)

    const output = state.get('call-1')?.output ?? ''
    expect(output).toHaveLength(SHELL_RUNTIME_MAX_CHARS)
    expect(output.startsWith(SHELL_RUNTIME_TRUNCATION_MARKER)).toBe(true)
    expect(output.endsWith('tail')).toBe(true)
    vi.useRealTimers()
  })
})
