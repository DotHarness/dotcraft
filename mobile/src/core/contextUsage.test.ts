import { describe, expect, it } from 'vitest'
import { applyContext, compactCount, contextUsageOf, systemEventUpdate, usageDeltaUpdate, usedShare, type ContextUsage } from './contextUsage'

const seeded: ContextUsage = { tokens: 100_000, contextWindow: 400_000, percentLeft: 0.75 }

describe('context ring', () => {
  it('reads a snapshot and reports the share of the window in use', () => {
    const usage = contextUsageOf({ tokens: 510_000, contextWindow: 830_000, percentLeft: 0.38, autoCompactThreshold: 700_000 })
    expect(usage).toEqual({ tokens: 510_000, contextWindow: 830_000, percentLeft: 0.38 })
    expect(usedShare(usage)).toBeCloseTo(0.614, 3)
    expect(usedShare(null)).toBe(0)
    expect(usedShare({ tokens: 900, contextWindow: 0, percentLeft: 1 })).toBe(0)
    expect(usedShare({ tokens: 900, contextWindow: 600, percentLeft: 0 })).toBe(1)
    expect(contextUsageOf(null)).toBeNull()
  })

  it('replaces the ring from a usage delta snapshot and otherwise moves a seeded ring by the input tokens', () => {
    const snapshot = usageDeltaUpdate({ threadId: 't', contextUsage: { tokens: 200_000, contextWindow: 400_000, percentLeft: 0.5 } })
    expect(applyContext(null, snapshot!)).toEqual({ tokens: 200_000, contextWindow: 400_000, percentLeft: 0.5 })

    const tokens = usageDeltaUpdate({ threadId: 't', inputTokens: 5, totalInputTokens: 300_000 })
    expect(applyContext(seeded, tokens!)).toEqual({ tokens: 300_000, contextWindow: 400_000, percentLeft: 0.25 })
    expect(applyContext(null, tokens!)).toBeNull()
    expect(usageDeltaUpdate({ threadId: 't', inputTokens: 5 })).toBeNull()
  })

  it('follows terminal compaction events and ignores the compacting start', () => {
    expect(systemEventUpdate({ kind: 'compacting', tokenCount: 1, contextUsage: { tokens: 1, contextWindow: 10 } })).toBeNull()
    expect(systemEventUpdate({ kind: 'compactWarning', tokenCount: 1 })).toBeNull()
    const compacted = systemEventUpdate({ kind: 'compacted', contextUsage: { tokens: 40_000, contextWindow: 400_000, percentLeft: 0.9 } })
    expect(applyContext(seeded, compacted!)).toEqual({ tokens: 40_000, contextWindow: 400_000, percentLeft: 0.9 })
    const failed = systemEventUpdate({ kind: 'compactFailed', tokenCount: 380_000, percentLeft: 0.04 })
    expect(applyContext(seeded, failed!)).toEqual({ tokens: 380_000, contextWindow: 400_000, percentLeft: 0.04 })
  })

  it('writes token counts compactly', () => {
    expect([999, 1_000, 12_345, 510_000, 999_960, 1_250_000].map(compactCount)).toEqual(['999', '1K', '12.3K', '510K', '1M', '1.3M'])
  })
})
