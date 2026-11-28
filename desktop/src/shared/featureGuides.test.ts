import { describe, expect, it } from 'vitest'
import {
  canShowFeatureGuide,
  countRecentlyOpenedProjects,
  normalizeFeatureGuides,
  recordFeatureGuideClosed,
  recordFeatureGuideShown,
  recordSubagentChat,
  settleFeatureGuide,
  spaceFeatureGuideId
} from './featureGuides'

const DAY = 24 * 60 * 60 * 1000
const start = new Date(2026, 10, 24, 10).getTime()

describe('feature guide scheduling', () => {
  it('holds readiness and friction guides during the first session but lets entry guides through', () => {
    const state = normalizeFeatureGuides({ initializedAt: start })
    expect(canShowFeatureGuide(state, 'subagentModels', start, true)).toBe(false)
    expect(canShowFeatureGuide(state, 'findOutWhy', start, true)).toBe(false)
    expect(canShowFeatureGuide(state, 'creator', start, true)).toBe(true)
  })

  it('shows at most one non-entry guide per local day', () => {
    const shown = recordFeatureGuideShown(normalizeFeatureGuides({}), 'subagentModels', start)
    expect(canShowFeatureGuide(shown, 'findOutWhy', start + 60_000, false)).toBe(false)
    expect(canShowFeatureGuide(shown, 'creator', start + 60_000, false)).toBe(true)
    expect(canShowFeatureGuide(shown, 'findOutWhy', start + DAY, false)).toBe(true)
  })

  it('returns a closed guide once after seven days, then never', () => {
    let state = recordFeatureGuideShown(normalizeFeatureGuides({}), 'findOutWhy', start)
    state = recordFeatureGuideClosed(state, 'findOutWhy', start)
    expect(canShowFeatureGuide(state, 'findOutWhy', start + 6 * DAY, false)).toBe(false)
    expect(canShowFeatureGuide(state, 'findOutWhy', start + 7 * DAY, false)).toBe(true)
    state = recordFeatureGuideShown(state, 'findOutWhy', start + 7 * DAY)
    state = recordFeatureGuideClosed(state, 'findOutWhy', start + 7 * DAY)
    expect(canShowFeatureGuide(state, 'findOutWhy', start + 30 * DAY, false)).toBe(false)
  })

  it('never shows a settled guide, even one that never showed', () => {
    const state = settleFeatureGuide(normalizeFeatureGuides({}), 'subagentModels')
    expect(canShowFeatureGuide(state, 'subagentModels', start, false)).toBe(false)
  })

  it('shows nothing when feature tips are off', () => {
    const state = normalizeFeatureGuides({ enabled: false })
    expect(canShowFeatureGuide(state, 'creator', start, false)).toBe(false)
  })

  it('treats a plugin space guide as a readiness guide that survives a settings round trip', () => {
    const id = spaceFeatureGuideId('oratorio:conductor')
    expect(canShowFeatureGuide(normalizeFeatureGuides({}), id, start, true)).toBe(false)
    const shown = recordFeatureGuideShown(normalizeFeatureGuides({}), id, start)
    expect(canShowFeatureGuide(shown, 'subagentModels', start + 60_000, false)).toBe(false)
    const settled = normalizeFeatureGuides(JSON.parse(JSON.stringify(settleFeatureGuide(shown, id))))
    expect(canShowFeatureGuide(settled, id, start + 30 * DAY, false)).toBe(false)
  })

  it('keeps distinct subagent chats through a settings round trip', () => {
    let state = recordSubagentChat(normalizeFeatureGuides({}), 'thread-a')
    state = recordSubagentChat(state, 'thread-a')
    state = recordSubagentChat(state, 'thread-b')
    expect(normalizeFeatureGuides(JSON.parse(JSON.stringify(state))).subagentChats).toEqual(['thread-a', 'thread-b'])
  })
})

describe('Oratorio guide signal', () => {
  it('counts only projects opened within the last seven days', () => {
    const iso = (offset: number): string => new Date(start - offset).toISOString()
    expect(countRecentlyOpenedProjects([iso(0), iso(6 * DAY), iso(8 * DAY), undefined, 'not a date'], start)).toBe(2)
  })
})
