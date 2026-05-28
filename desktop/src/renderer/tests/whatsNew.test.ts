import { describe, expect, it } from 'vitest'
import { readdirSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import {
  compareAppVersions,
  getLatestWhatsNewVersion,
  getUnseenWhatsNewReleases,
  getWhatsNewReleasesUpTo,
  parseWhatsNewRelease,
  sortWhatsNewReleasesNewestFirst,
  WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT,
  WHATS_NEW_REMOTE_MEDIA_BASE_URL
} from '../../shared/whatsNew'

const releasesDir = resolve(__dirname, '../../../resources/whats-new/releases')
const bundledReleases = sortWhatsNewReleasesNewestFirst(
  readdirSync(releasesDir)
    .filter((name) => name.toLowerCase().endsWith('.json'))
    .map((name) => {
      const release = parseWhatsNewRelease(JSON.parse(readFileSync(resolve(releasesDir, name), 'utf8')))
      if (!release) {
        throw new Error(`Bundled What's New release fixture is invalid: ${name}`)
      }
      return release
    })
)
const WHATS_NEW_RELEASES = bundledReleases
const LATEST_BUNDLED_VERSION = getLatestWhatsNewVersion(WHATS_NEW_RELEASES) ?? ''

describe('whatsNew release filtering', () => {
  it('loads the bundled JSON release configs', () => {
    const versions = WHATS_NEW_RELEASES.map((release) => release.version)
    expect(versions).toContain('0.1.6')
    expect(versions).toContain('0.1.7')
  })

  it('treats missing last-seen state as unseen for the current release', () => {
    expect(
      getUnseenWhatsNewReleases(WHATS_NEW_RELEASES, LATEST_BUNDLED_VERSION, undefined)
        .map((release) => release.version)
    ).toContain(LATEST_BUNDLED_VERSION)
  })

  it('hides releases at or below the last seen version', () => {
    expect(getUnseenWhatsNewReleases(WHATS_NEW_RELEASES, LATEST_BUNDLED_VERSION, LATEST_BUNDLED_VERSION)).toEqual([])
  })

  it('does not show future releases', () => {
    expect(getWhatsNewReleasesUpTo(WHATS_NEW_RELEASES, '0.0.1')).toEqual([])
  })

  it('sorts and compares semver-like app versions numerically', () => {
    expect(compareAppVersions('0.1.10', '0.1.6')).toBeGreaterThan(0)
    expect(getLatestWhatsNewVersion(WHATS_NEW_RELEASES)).toBe(LATEST_BUNDLED_VERSION)
  })

  it('keeps each bundled media entry inside the per-card UX budget', () => {
    const media = WHATS_NEW_RELEASES.flatMap((release) =>
      release.cards.map((card) => card.media).filter((entry) => entry != null)
    )
    expect(media.length).toBeGreaterThan(0)
    for (const entry of media) {
      expect(entry.sizeBytes).toBeLessThanOrEqual(WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT)
    }
  })

  it('uses allowlisted resources-repo GIF metadata', () => {
    const media = WHATS_NEW_RELEASES.flatMap((release) =>
      release.cards.map((card) => card.media).filter((entry) => entry != null)
    )

    expect(media.length).toBeGreaterThan(0)
    for (const entry of media) {
      expect(entry.url).toBe(`${WHATS_NEW_REMOTE_MEDIA_BASE_URL}${entry.fileName}`)
      expect(entry.fileName).toMatch(/^[a-z0-9._-]+\.gif$/)
      expect(entry.sizeBytes).toBeGreaterThan(0)
      expect(entry.sha256).toMatch(/^[0-9A-F]{64}$/)
    }
  })
})
