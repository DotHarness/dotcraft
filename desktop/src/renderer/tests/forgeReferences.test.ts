import { describe, expect, it } from 'vitest'
import { forgeChangesUrl, forgeRefShortLabel, forgeRefUrl, parseForgeRefUrl, usesForgeRefLabel } from '../../shared/forgeReferences'
import { placeReferenceCard } from '../components/conversation/referenceHoverCard'

describe('parseForgeRefUrl', () => {
  it('accepts exact GitHub pull request and issue links', () => {
    expect(parseForgeRefUrl('https://github.com/OctoOrg/sample-app/pull/42'))
      .toEqual({ provider: 'github', host: 'github.com', path: 'OctoOrg/sample-app', kind: 'pull', number: 42 })
    expect(parseForgeRefUrl('https://github.com/OctoOrg/sample-app.js/issues/17/'))
      .toEqual({ provider: 'github', host: 'github.com', path: 'OctoOrg/sample-app.js', kind: 'issue', number: 17 })
  })

  it('accepts GitLab merge request and issue links on any host, with nested groups', () => {
    expect(parseForgeRefUrl('https://gitlab.example.net/tools/perf/insight-dashboard/-/merge_requests/21'))
      .toEqual({ provider: 'gitlab', host: 'gitlab.example.net', path: 'tools/perf/insight-dashboard', kind: 'pull', number: 21 })
    expect(parseForgeRefUrl('https://GitLab.com/group/project/-/issues/7/'))
      .toEqual({ provider: 'gitlab', host: 'gitlab.com', path: 'group/project', kind: 'issue', number: 7 })
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
      'https://github.com/group/project/-/merge_requests/1',
      'https://gitlab.com/project/-/merge_requests/1',
      'https://gitlab.com/group/project/-/merge_requests/1/diffs',
      'https://gitlab.com/group/project/-/merge_requests/1#note_9',
      'https://gitlab.com/group/../-/merge_requests/1',
      'https://gitlab.com:8443/group/project/-/merge_requests/1',
      'http://gitlab.com/group/project/-/merge_requests/1',
      'github.com/OctoOrg/sample-app/pull/1',
      ''
    ]) {
      expect(parseForgeRefUrl(href), href).toBeNull()
    }
  })

  it('round-trips through the canonical URL and points change stats at each forge’s diff page', () => {
    for (const href of [
      'https://github.com/OctoOrg/sample-app/pull/42',
      'https://gitlab.example.net/tools/perf/insight-dashboard/-/merge_requests/21',
      'https://gitlab.example.net/tools/perf/insight-dashboard/-/issues/3'
    ]) {
      expect(forgeRefUrl(parseForgeRefUrl(href)!)).toBe(href)
    }
    expect(forgeChangesUrl(parseForgeRefUrl('https://github.com/OctoOrg/sample-app/pull/42')!))
      .toBe('https://github.com/OctoOrg/sample-app/pull/42/files')
    expect(forgeChangesUrl(parseForgeRefUrl('https://gitlab.example.net/tools/dashboard/-/merge_requests/21')!))
      .toBe('https://gitlab.example.net/tools/dashboard/-/merge_requests/21/diffs')
  })
})

describe('forge reference labels', () => {
  const href = 'https://github.com/OctoOrg/sample-app/pull/42'
  const target = { provider: 'github' as const, host: 'github.com', path: 'OctoOrg/sample-app', kind: 'pull' as const, number: 42 }

  it('labels a reference only when the author wrote no text of their own, shortened inside the workspace repository', () => {
    expect(usesForgeRefLabel('', href, target)).toBe(true)
    expect(usesForgeRefLabel(href, href, target)).toBe(true)
    expect(usesForgeRefLabel('#42', href, target)).toBe(true)
    expect(usesForgeRefLabel('octoorg/sample-app#42', href, target)).toBe(true)
    expect(usesForgeRefLabel('the one-page PR', href, target)).toBe(false)
    expect(usesForgeRefLabel('#43', href, target)).toBe(false)

    expect(forgeRefShortLabel(target, { provider: 'github', host: 'github.com', path: 'octoorg/Sample-App' })).toBe('#42')
    expect(forgeRefShortLabel(target, { provider: 'github', host: 'github.com', path: 'OctoOrg/sample-lib' })).toBe('OctoOrg/sample-app#42')
    expect(forgeRefShortLabel(target, null)).toBe('OctoOrg/sample-app#42')
  })

  it('uses GitLab’s own sigils: !N for merge requests and #N for issues', () => {
    const mrHref = 'https://gitlab.example.net/tools/dashboard/-/merge_requests/21'
    const mr = { provider: 'gitlab' as const, host: 'gitlab.example.net', path: 'tools/dashboard', kind: 'pull' as const, number: 21 }
    const workspace = { provider: 'gitlab' as const, host: 'gitlab.example.net', path: 'Tools/Dashboard' }
    expect(usesForgeRefLabel('!21', mrHref, mr)).toBe(true)
    expect(usesForgeRefLabel('tools/dashboard!21', mrHref, mr)).toBe(true)
    expect(usesForgeRefLabel('#21', mrHref, mr)).toBe(false)
    expect(forgeRefShortLabel(mr, workspace)).toBe('!21')
    expect(forgeRefShortLabel(mr, { ...workspace, host: 'gitlab.com' })).toBe('tools/dashboard!21')
    expect(forgeRefShortLabel({ ...mr, kind: 'issue' }, workspace)).toBe('#21')
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
