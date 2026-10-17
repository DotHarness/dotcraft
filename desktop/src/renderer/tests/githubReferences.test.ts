import { describe, expect, it } from 'vitest'
import { githubRefShortLabel, parseGithubRefUrl, usesGithubRefLabel } from '../../shared/githubReferences'
import { placeGithubRefCard } from '../components/conversation/githubReferences/useGithubRefHoverCard'

describe('parseGithubRefUrl', () => {
  it('accepts exact pull request and issue links', () => {
    expect(parseGithubRefUrl('https://github.com/DotHarness/dotcraft/pull/404'))
      .toEqual({ owner: 'DotHarness', repo: 'dotcraft', kind: 'pull', number: 404 })
    expect(parseGithubRefUrl('https://github.com/DotHarness/dotcraft.js/issues/269/'))
      .toEqual({ owner: 'DotHarness', repo: 'dotcraft.js', kind: 'issue', number: 269 })
  })

  it('leaves sub-pages, other hosts and decorated URLs as ordinary links', () => {
    for (const href of [
      'https://github.com/DotHarness/dotcraft/pull/404/files',
      'https://github.com/DotHarness/dotcraft/pull/404?diff=split',
      'https://github.com/DotHarness/dotcraft/pull/404#discussion_r1',
      'https://github.com/DotHarness/dotcraft/commit/abc123',
      'https://github.com/DotHarness/dotcraft/pulls',
      'https://github.com/DotHarness/dotcraft/pull/0',
      'https://github.com/DotHarness/dotcraft/pull/04',
      'https://github.com/./dotcraft/pull/1',
      'https://github.com/DotHarness/../pull/1',
      'https://github.com:8443/DotHarness/dotcraft/pull/1',
      'https://user@github.com/DotHarness/dotcraft/pull/1',
      'http://github.com/DotHarness/dotcraft/pull/1',
      'https://www.github.com/DotHarness/dotcraft/pull/1',
      'https://gitlab.com/DotHarness/dotcraft/pull/1',
      'https://github.com/Dot%20Harness/dotcraft/pull/1',
      'github.com/DotHarness/dotcraft/pull/1',
      ''
    ]) {
      expect(parseGithubRefUrl(href), href).toBeNull()
    }
  })
})

describe('github reference labels', () => {
  const href = 'https://github.com/DotHarness/dotcraft/pull/404'
  const target = { owner: 'DotHarness', repo: 'dotcraft', kind: 'pull' as const, number: 404 }

  it('labels a reference when the author wrote no text of their own', () => {
    expect(usesGithubRefLabel('', href, target)).toBe(true)
    expect(usesGithubRefLabel(href, href, target)).toBe(true)
    expect(usesGithubRefLabel('#404', href, target)).toBe(true)
    expect(usesGithubRefLabel('dotharness/dotcraft#404', href, target)).toBe(true)
    expect(usesGithubRefLabel('the one-page PR', href, target)).toBe(false)
    expect(usesGithubRefLabel('#405', href, target)).toBe(false)
  })

  it('shortens to the number inside the workspace repository only', () => {
    expect(githubRefShortLabel(target, { owner: 'dotharness', repo: 'DotCraft' })).toBe('#404')
    expect(githubRefShortLabel(target, { owner: 'DotHarness', repo: 'dotcraft-unity' })).toBe('DotHarness/dotcraft#404')
    expect(githubRefShortLabel(target, null)).toBe('DotHarness/dotcraft#404')
  })
})

describe('placeGithubRefCard', () => {
  const viewport = { width: 1200, height: 800 }

  it('opens below the anchor and mirrors above when there is no room', () => {
    expect(placeGithubRefCard({ left: 100, top: 200, bottom: 220 }, 160, viewport, 'below'))
      .toEqual({ left: 100, top: 226, side: 'below' })
    expect(placeGithubRefCard({ left: 100, top: 700, bottom: 720 }, 160, viewport, 'below'))
      .toEqual({ left: 100, top: 534, side: 'above' })
  })

  it('keeps a preferred above side unless only below fits', () => {
    expect(placeGithubRefCard({ left: 100, top: 600, bottom: 628 }, 160, viewport, 'above'))
      .toEqual({ left: 100, top: 434, side: 'above' })
    expect(placeGithubRefCard({ left: 100, top: 40, bottom: 68 }, 160, viewport, 'above'))
      .toEqual({ left: 100, top: 74, side: 'below' })
  })

  it('clamps the card inside the viewport edges', () => {
    expect(placeGithubRefCard({ left: 1000, top: 200, bottom: 220 }, 160, viewport, 'below').left).toBe(832)
    expect(placeGithubRefCard({ left: -20, top: 200, bottom: 220 }, 160, viewport, 'below').left).toBe(8)
  })
})
