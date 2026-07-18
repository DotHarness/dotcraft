import { describe, expect, it } from 'vitest'
import {
  compareAppVersions,
  getLatestWhatsNewVersion,
  getUnseenWhatsNewReleases,
  getWhatsNewReleasesUpTo,
  parseWhatsNewRelease,
  sortWhatsNewReleasesNewestFirst,
  WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT,
  WHATS_NEW_REMOTE_MEDIA_BASE_URL,
  type WhatsNewRelease
} from '../../shared/whatsNew'

function release(version: string): WhatsNewRelease {
  return {
    version,
    cards: [
      {
        id: `release-${version}`,
        title: { en: `Release ${version}` },
        summary: { en: `Summary ${version}` }
      }
    ]
  }
}

const RELEASES = sortWhatsNewReleasesNewestFirst([
  release('0.1.6'),
  release('0.1.10')
])
const LATEST_VERSION = '0.1.10'

describe('whatsNew release filtering', () => {
  it('treats missing last-seen state as unseen for the current release', () => {
    expect(
      getUnseenWhatsNewReleases(RELEASES, LATEST_VERSION, undefined)
        .map((item) => item.version)
    ).toContain(LATEST_VERSION)
  })

  it('hides releases at or below the last seen version', () => {
    expect(getUnseenWhatsNewReleases(RELEASES, LATEST_VERSION, LATEST_VERSION)).toEqual([])
  })

  it('does not show future releases', () => {
    expect(getWhatsNewReleasesUpTo(RELEASES, '0.0.1')).toEqual([])
  })

  it('sorts and compares semver-like app versions numerically', () => {
    expect(compareAppVersions('0.1.10', '0.1.6')).toBeGreaterThan(0)
    expect(getLatestWhatsNewVersion(RELEASES)).toBe(LATEST_VERSION)
  })

  it('accepts valid media metadata and normalizes the hash', () => {
    const parsed = parseWhatsNewRelease({
      version: '1.0.0',
      cards: [
        {
          id: 'demo',
          title: { en: 'Demo' },
          summary: { en: 'Demo summary.' },
          media: {
            fileName: 'demo.gif',
            url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}demo.gif`,
            sizeBytes: WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT,
            sha256: 'a'.repeat(64)
          }
        }
      ]
    })

    expect(parsed?.cards[0].media).toMatchObject({
      fileName: 'demo.gif',
      sizeBytes: WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT,
      sha256: 'A'.repeat(64)
    })
  })

  it('rejects media outside the size and URL contracts', () => {
    const makeValue = (media: Record<string, unknown>) => ({
      version: '1.0.0',
      cards: [
        {
          id: 'demo',
          title: { en: 'Demo' },
          summary: { en: 'Demo summary.' },
          media: {
            fileName: 'demo.gif',
            url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}demo.gif`,
            sizeBytes: 1,
            sha256: 'A'.repeat(64),
            ...media
          }
        }
      ]
    })

    expect(parseWhatsNewRelease(makeValue({
      sizeBytes: WHATS_NEW_MEDIA_BYTES_PER_CARD_LIMIT + 1
    }))).toBeNull()
    expect(parseWhatsNewRelease(makeValue({
      url: 'https://example.com/demo.gif'
    }))).toBeNull()
  })
})
