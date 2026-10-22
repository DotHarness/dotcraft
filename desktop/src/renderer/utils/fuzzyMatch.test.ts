import { describe, expect, it } from 'vitest'
import { fuzzyFilter, fuzzyHighlightRanges, fuzzyScore } from './fuzzyMatch'

describe('fuzzyScore', () => {
  it('matches word starts and long mid-word runs but not scattered letters', () => {
    expect(fuzzyScore('code-review', 're')).toBeGreaterThan(0)
    expect(fuzzyScore('pr-review', 'prr')).toBeGreaterThan(0)
    expect(fuzzyScore('createPullRequest', 'cpr')).toBeGreaterThan(0)
    expect(fuzzyScore('review', 'vie')).toBeGreaterThan(0)
    expect(fuzzyScore('review', 'vi')).toBe(0)
    expect(fuzzyScore('review', 'rvw')).toBe(0)
    expect(fuzzyScore('plan', 'compact')).toBe(0)
  })

  it('ranks prefixes above word starts above mid-word matches', () => {
    const prefix = fuzzyScore('release-notes', 'rel')
    const wordStart = fuzzyScore('draft-release', 'rel')
    const middle = fuzzyScore('unreleased', 'rel')
    expect(prefix).toBeGreaterThan(wordStart)
    expect(wordStart).toBeGreaterThan(middle)
    expect(middle).toBeGreaterThan(0)
  })
})

describe('fuzzyFilter', () => {
  it('keeps every item for an empty query and sorts matches best first', () => {
    const items = ['unreleased', 'draft-release', 'release-notes', 'plan']
    expect(fuzzyFilter(items, '', (item) => [item])).toEqual(items)
    expect(fuzzyFilter(items, 'rel', (item) => [item])).toEqual(['release-notes', 'draft-release', 'unreleased'])
  })
})

describe('fuzzyHighlightRanges', () => {
  it('prefers a substring and falls back to an in-order match', () => {
    expect(fuzzyHighlightRanges('code-review', 'rev')).toEqual([{ start: 5, end: 8 }])
    expect(fuzzyHighlightRanges('pr-review', 'prr')).toEqual([{ start: 0, end: 2 }, { start: 3, end: 4 }])
  })
})
