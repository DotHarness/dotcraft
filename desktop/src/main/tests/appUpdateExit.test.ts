import { afterEach, describe, expect, it, vi } from 'vitest'
import { createAppUpdateExitWatchdog } from '../appUpdateExit'

afterEach(() => vi.useRealTimers())

describe('update exit watchdog', () => {
  it('starts the exit deadline only after installer acceptance without extending it on duplicate calls', () => {
    vi.useFakeTimers()
    const app = { exit: vi.fn() }
    const watchdog = createAppUpdateExitWatchdog({ app, timeoutMs: 100 })
    vi.advanceTimersByTime(1_000)
    expect(app.exit).not.toHaveBeenCalled()
    watchdog.beginQuit()
    vi.advanceTimersByTime(50)
    watchdog.beginQuit()
    vi.advanceTimersByTime(50)
    expect(app.exit).toHaveBeenCalledExactlyOnceWith(0)
  })

  it('cancels a pending exit and allows a fresh deadline on retry', () => {
    vi.useFakeTimers()
    const app = { exit: vi.fn() }
    const watchdog = createAppUpdateExitWatchdog({ app, timeoutMs: 100 })
    watchdog.beginQuit()
    watchdog.cancel()
    vi.advanceTimersByTime(1_000)
    expect(app.exit).not.toHaveBeenCalled()
    watchdog.beginQuit()
    vi.advanceTimersByTime(100)
    expect(app.exit).toHaveBeenCalledOnce()
  })
})
