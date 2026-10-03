import { describe, expect, it } from 'vitest'
import { Reconnector, type Timers } from './backoff'

describe('reconnect backoff', () => {
  it('backs off from 1 to 30 seconds on repeated failures and restarts immediately on demand', () => {
    const delays: number[] = []
    const timers: Timers = { setTimeout: (_callback, ms) => delays.push(ms), clearTimeout: () => undefined }
    let runs = 0
    const reconnector = new Reconnector(() => (runs += 1), timers, () => 1)
    for (let attempt = 0; attempt < 7; attempt += 1) reconnector.schedule()
    expect(delays).toEqual([1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000])
    reconnector.now()
    expect(runs).toBe(1)
    reconnector.schedule()
    expect(delays.at(-1)).toBe(1_000)
  })
})
