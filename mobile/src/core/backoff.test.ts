import { describe, expect, it } from 'vitest'
import { Reconnector, type Timers } from './backoff'

describe('reconnect backoff', () => {
  it('backs off from 1 to 30 seconds on repeated failures and starts over after a reset', () => {
    const delays: number[] = []
    const timers: Timers = { setTimeout: (_callback, ms) => delays.push(ms), clearTimeout: () => undefined }
    const reconnector = new Reconnector(() => undefined, timers, () => 1)
    for (let attempt = 0; attempt < 7; attempt += 1) reconnector.schedule()
    expect(delays).toEqual([1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000])
    reconnector.reset()
    reconnector.schedule()
    expect(delays.at(-1)).toBe(1_000)
  })
})
