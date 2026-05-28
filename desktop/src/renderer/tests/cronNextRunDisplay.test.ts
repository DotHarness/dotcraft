import { describe, it, expect } from 'vitest'
import { formatNextRun } from '../utils/cronNextRunDisplay'

const en = 'en' as const

describe('formatNextRun', () => {
  it('shows Not scheduled when null', () => {
    const r = formatNextRun(null, true, en)
    expect(r.absolute).toBe('Not scheduled')
    expect(r.relative).toBe(null)
  })

  it('includes relative when enabled and future', () => {
    const future = Date.now() + 120_000
    const r = formatNextRun(future, true, en)
    expect(r.absolute).not.toBe('Not scheduled')
    expect(r.relative).toMatch(/^in /)
  })

  it('omits relative when disabled', () => {
    const future = Date.now() + 120_000
    const r = formatNextRun(future, false, en)
    expect(r.relative).toBe(null)
  })
})
