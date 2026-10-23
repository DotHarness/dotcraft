import { describe, expect, it } from 'vitest'
import { githubRefShortLabel, parseGithubRefUrl, usesGithubRefLabel } from '../../shared/githubReferences'
import { placeReferenceCard } from '../components/conversation/referenceHoverCard'

describe('parseGithubRefUrl', () => {
  it('accepts exact pull request and issue links', () => {
    expect(parseGithubRefUrl('https://github.com/OctoOrg/sample-app/pull/42'))
      .toEqual({ owner: 'OctoOrg', repo: 'sample-app', kind: 'pull', number: 42 })
    expect(parseGithubRefUrl('https://github.com/OctoOrg/sample-app.js/issues/17/'))
      .toEqual({ owner: 'OctoOrg', repo: 'sample-app.js', kind: 'issue', number: 17 })
  })

  it('leaves sub-pages, other hosts and decorated URLs as ordinary links', () => {
    for (const href of [
      'https://github.com/OctoOrg/sample-app/pull/42/files',
      'https://github.com/OctoOrg/sample-app/pull/42?diff=split',
      'https://github.com/OctoOrg/sample-app/pull/42#discussion_r1',
      'https://github.com/OctoOrg/sample-app/commit/abc123',
      'https://github.com/OctoOrg/sample-app/pulls',
      'https://github.com/OctoOrg/sample-app/pull/0',
      'https://github.com/OctoOrg/sample-app/pull/04',
      'https://github.com/./sample-app/pull/1',
      'https://github.com/OctoOrg/../pull/1',
      'https://github.com:8443/OctoOrg/sample-app/pull/1',
      'https://user@github.com/OctoOrg/sample-app/pull/1',
      'http://github.com/OctoOrg/sample-app/pull/1',
      'https://www.github.com/OctoOrg/sample-app/pull/1',
      'https://gitlab.com/OctoOrg/sample-app/pull/1',
      'https://github.com/Octo%20Org/sample-app/pull/1',
      'github.com/OctoOrg/sample-app/pull/1',
      ''
    ]) {
      expect(parseGithubRefUrl(href), href).toBeNull()
    }
  })
})

describe('github reference labels', () => {
  const href = 'https://github.com/OctoOrg/sample-app/pull/42'
  const target = { owner: 'OctoOrg', repo: 'sample-app', kind: 'pull' as const, number: 42 }

  it('labels a reference only when the author wrote no text of their own, shortened inside the workspace repository', () => {
    expect(usesGithubRefLabel('', href, target)).toBe(true)
    expect(usesGithubRefLabel(href, href, target)).toBe(true)
    expect(usesGithubRefLabel('#42', href, target)).toBe(true)
    expect(usesGithubRefLabel('octoorg/sample-app#42', href, target)).toBe(true)
    expect(usesGithubRefLabel('the one-page PR', href, target)).toBe(false)
    expect(usesGithubRefLabel('#43', href, target)).toBe(false)

    expect(githubRefShortLabel(target, { owner: 'octoorg', repo: 'Sample-App' })).toBe('#42')
    expect(githubRefShortLabel(target, { owner: 'OctoOrg', repo: 'sample-lib' })).toBe('OctoOrg/sample-app#42')
    expect(githubRefShortLabel(target, null)).toBe('OctoOrg/sample-app#42')
  })
})

describe('placeReferenceCard', () => {
  const viewport = { width: 1200, height: 800 }

  it('opens on the preferred side, flips when there is no room and clamps inside the viewport', () => {
    expect(placeReferenceCard({ left: 100, top: 200, bottom: 220 }, 160, viewport, 'below'))
      .toEqual({ left: 100, top: 226, side: 'below' })
    expect(placeReferenceCard({ left: 100, top: 700, bottom: 720 }, 160, viewport, 'below'))
      .toEqual({ left: 100, top: 534, side: 'above' })
    expect(placeReferenceCard({ left: 100, top: 600, bottom: 628 }, 160, viewport, 'above'))
      .toEqual({ left: 100, top: 434, side: 'above' })
    expect(placeReferenceCard({ left: 100, top: 40, bottom: 68 }, 160, viewport, 'above'))
      .toEqual({ left: 100, top: 74, side: 'below' })
    expect(placeReferenceCard({ left: 1000, top: 200, bottom: 220 }, 160, viewport, 'below').left).toBe(832)
    expect(placeReferenceCard({ left: -20, top: 200, bottom: 220 }, 160, viewport, 'below').left).toBe(8)
  })
})
