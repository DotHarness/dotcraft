import { createHash } from 'node:crypto'
import { mkdtemp, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'

const electronMock = vi.hoisted(() => ({
  app: {
    getPath: vi.fn(() => tmpdir())
  },
  net: {
    fetch: vi.fn()
  }
}))

vi.mock('electron', () => electronMock)

import {
  WhatsNewMediaCache,
  isAllowedWhatsNewMediaUrl,
  resolveWhatsNewMediaAssets,
  type WhatsNewMediaAsset
} from '../whatsNewMediaCache'
import {
  WHATS_NEW_REMOTE_MEDIA_BASE_URL,
  type WhatsNewRelease
} from '../../shared/whatsNew'

const tempDirs: string[] = []

afterEach(async () => {
  for (const dir of tempDirs.splice(0)) {
    await rm(dir, { recursive: true, force: true })
  }
  vi.clearAllMocks()
})

async function createTempDir(): Promise<string> {
  const dir = await mkdtemp(join(tmpdir(), 'dotcraft-whats-new-media-'))
  tempDirs.push(dir)
  return dir
}

function sha256(buffer: Buffer): string {
  return createHash('sha256').update(buffer).digest('hex').toUpperCase()
}

function makeAsset(buffer: Buffer, overrides: Partial<WhatsNewMediaAsset> = {}): WhatsNewMediaAsset {
  return {
    releaseVersion: '9.9.9',
    cardId: 'demo',
    fileName: 'demo.gif',
    url: `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}demo.gif`,
    sizeBytes: buffer.byteLength,
    sha256: sha256(buffer),
    ...overrides
  }
}

function makeFetch(buffer: Buffer): typeof fetch {
  return vi.fn(async () => new Response(new Uint8Array(buffer))) as unknown as typeof fetch
}

describe('WhatsNewMediaCache', () => {
  it('allowlists only the resources-repo whats-new media path', () => {
    expect(isAllowedWhatsNewMediaUrl(`${WHATS_NEW_REMOTE_MEDIA_BASE_URL}demo.gif`)).toBe(true)
    expect(isAllowedWhatsNewMediaUrl('https://example.com/demo.gif')).toBe(false)
  })

  it('downloads, verifies, and reuses cached media without refetching', async () => {
    const cacheRoot = await createTempDir()
    const body = Buffer.from('verified gif bytes')
    const asset = makeAsset(body)
    const fetchImpl = makeFetch(body)
    const cache = new WhatsNewMediaCache({
      cacheRoot,
      fetchImpl,
      assetResolver: () => [asset]
    })

    const first = await cache.prefetchMedia([asset.releaseVersion])
    expect(first).toMatchObject([{ status: 'ready', cachedUrl: expect.stringMatching(/^file:/) }])

    const state = await cache.getMediaStates([asset.releaseVersion])
    expect(state).toMatchObject([{ status: 'ready', cachedUrl: first[0].cachedUrl }])

    const second = await cache.prefetchMedia([asset.releaseVersion])
    expect(second).toMatchObject([{ status: 'ready', cachedUrl: first[0].cachedUrl }])
    expect(fetchImpl).toHaveBeenCalledTimes(1)
  })

  it('can resolve media assets from catalog releases', () => {
    const body = Buffer.from('catalog bytes')
    const asset = makeAsset(body)
    const release: WhatsNewRelease = {
      version: asset.releaseVersion,
      cards: [
        {
          id: asset.cardId,
          title: {
            en: 'Demo',
            'zh-Hans': 'Demo'
          },
          summary: {
            en: 'Demo summary.',
            'zh-Hans': 'Demo summary.'
          },
          media: {
            fileName: asset.fileName,
            url: asset.url,
            sizeBytes: asset.sizeBytes,
            sha256: asset.sha256
          }
        }
      ]
    }

    expect(resolveWhatsNewMediaAssets([release], [asset.releaseVersion])).toEqual([asset])
  })

  it('fails closed when downloaded size or hash verification fails', async () => {
    const cacheRoot = await createTempDir()
    const body = Buffer.from('actual bytes')
    const asset = makeAsset(body, { sizeBytes: body.byteLength + 1 })
    const events: string[] = []
    const cache = new WhatsNewMediaCache({
      cacheRoot,
      fetchImpl: makeFetch(body),
      assetResolver: () => [asset],
      onStateChanged: (state) => events.push(state.status)
    })

    const result = await cache.prefetchMedia([asset.releaseVersion])

    expect(result).toMatchObject([{ status: 'failed' }])
    expect(events).toEqual(['downloading', 'failed'])
  })

  it('deduplicates concurrent downloads for the same URL and hash', async () => {
    const cacheRoot = await createTempDir()
    const body = Buffer.from('shared bytes')
    const asset = makeAsset(body)
    let resolveFetch: ((response: Response) => void) | null = null
    const fetchMock = vi.fn(() => new Promise<Response>((resolve) => {
      resolveFetch = resolve
    }))
    const fetchImpl = fetchMock as unknown as typeof fetch
    const cache = new WhatsNewMediaCache({
      cacheRoot,
      fetchImpl,
      assetResolver: () => [asset]
    })
    const completeFetch = (response: Response): void => {
      if (!resolveFetch) throw new Error('Fetch did not start.')
      resolveFetch(response)
    }

    const first = cache.prefetchMedia([asset.releaseVersion])
    const second = cache.prefetchMedia([asset.releaseVersion])
    for (let i = 0; i < 20 && fetchMock.mock.calls.length === 0; i++) {
      await new Promise((resolve) => setTimeout(resolve, 5))
    }
    expect(fetchMock).toHaveBeenCalledTimes(1)

    completeFetch(new Response(new Uint8Array(body)))
    await expect(Promise.all([first, second])).resolves.toEqual([
      [expect.objectContaining({ status: 'ready' })],
      [expect.objectContaining({ status: 'ready' })]
    ])
  })
})
