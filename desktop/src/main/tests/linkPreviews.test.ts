import { PassThrough } from 'node:stream'
import { gzipSync } from 'node:zlib'
import { describe, expect, it, vi } from 'vitest'
import { isPreviewableUrl } from '../../shared/linkPreviews'
import {
  fetchLinkPreviewResource,
  isPublicAddress,
  LinkPreviewBlockedError,
  readLinkPreviewBody,
  type LinkPreviewResponse
} from '../linkPreviewFetch'
import { createLinkPreviewService } from '../linkPreviews'

vi.mock('electron', () => ({}))

const PNG = Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), Buffer.alloc(16)])

function html(head: string): LinkPreviewResponse {
  return { url: 'https://example.com/post', contentType: 'text/html; charset=utf-8', body: Buffer.from(`<html><head>${head}</head>`) }
}

function fakeFetch(routes: Record<string, LinkPreviewResponse | Error>) {
  return vi.fn(async (url: string) => {
    const route = routes[url]
    if (!route) throw new Error(`HTTP 404 ${url}`)
    if (route instanceof Error) throw route
    return route
  })
}

describe('link preview address policy', () => {
  it('refuses loopback, private, link-local, and mapped addresses', () => {
    for (const address of ['127.0.0.1', '10.1.2.3', '172.20.0.1', '192.168.1.1', '169.254.169.254', '100.64.0.1', '0.0.0.0', '::1', '::', 'fd00::1', 'fe80::1', '::ffff:127.0.0.1', '::ffff:7f00:1']) {
      expect(isPublicAddress(address), address).toBe(false)
    }
    for (const address of ['93.184.216.34', '1.1.1.1', '2606:4700:4700::1111']) {
      expect(isPublicAddress(address), address).toBe(true)
    }
  })

  it('previews only public-looking https URLs without credentials', () => {
    expect(isPreviewableUrl('https://example.com/a')).toBe(true)
    for (const url of ['http://example.com/', 'https://user:pw@example.com/', 'https://localhost:5173/', 'https://printer.local/', 'https://10.0.0.1/', 'https://[::1]/', 'notaurl']) {
      expect(isPreviewableUrl(url), url).toBe(false)
    }
  })

  it('refuses a literal private address before connecting', async () => {
    await expect(fetchLinkPreviewResource('https://127.0.0.1/', { accept: '*/*', maxBytes: 10, timeoutMs: 1000 }))
      .rejects.toBeInstanceOf(LinkPreviewBlockedError)
  })
})

describe('link preview body', () => {
  const page = gzipSync(Buffer.from(`<html><head><title>${'x'.repeat(4000)}</title></head>`))

  it('reads a compressed body up to the end of the head and settles when it is cut off', async () => {
    const source = new PassThrough()
    const reading = readLinkPreviewBody(source, 'gzip', { maxBytes: 64 * 1024, stopAt: '</head' })
    source.end(page)
    expect((await reading).toString()).toContain('</head')

    const failed = new PassThrough()
    const failing = readLinkPreviewBody(failed, 'gzip', { maxBytes: 64 * 1024 })
    failed.write(page.subarray(0, 24))
    failed.destroy(new Error('socket hang up'))
    await expect(failing).rejects.toThrow()

    const closed = new PassThrough()
    const closing = readLinkPreviewBody(closed, 'gzip', { maxBytes: 64 * 1024 })
    closed.write(page.subarray(0, 24))
    closed.destroy()
    await expect(closing).rejects.toThrow()
  })
})

describe('link preview service', () => {
  it('reads the title, preview image, and favicon from the page head and drops non-raster images', async () => {
    const fetch = fakeFetch({
      'https://example.com/post': html(
        '<title>Fallback</title><meta property="og:title" content="Tom &amp; Jerry&#39;s post">' +
          '<meta property="og:image" content="/cover.png"><link rel="icon" href="https://cdn.example.com/icon.png">'
      ),
      'https://example.com/cover.png': { url: '', contentType: 'image/png', body: PNG },
      'https://cdn.example.com/icon.png': { url: '', contentType: 'image/png', body: PNG }
    })
    const result = await createLinkPreviewService(fetch).get('https://example.com/post#section')

    expect(result).toEqual({
      status: 'ready',
      title: "Tom & Jerry's post",
      imageDataUrl: `data:image/png;base64,${PNG.toString('base64')}`,
      faviconDataUrl: `data:image/png;base64,${PNG.toString('base64')}`
    })
    expect(fetch).toHaveBeenCalledWith('https://example.com/post', expect.anything())

    const svgFetch = fakeFetch({
      'https://example.com/post': html('<title>Page</title><meta property="og:image" content="https://example.com/x.svg">'),
      'https://example.com/x.svg': { url: '', contentType: 'image/png', body: Buffer.from('<svg onload="x()"/>') }
    })
    expect(await createLinkPreviewService(svgFetch).get('https://example.com/post')).toEqual({ status: 'ready', title: 'Page' })
  })

  it('reports refused addresses as blocked and other failures as unavailable', async () => {
    const blocked = createLinkPreviewService(fakeFetch({ 'https://example.com/post': new LinkPreviewBlockedError('private') }))
    expect(await blocked.get('https://example.com/post')).toEqual({ status: 'error', error: 'blocked' })
    const failed = createLinkPreviewService(fakeFetch({}))
    expect(await failed.get('https://example.com/post')).toEqual({ status: 'error', error: 'unavailable' })
    expect(await failed.get('http://example.com/post')).toEqual({ status: 'error', error: 'blocked' })
  })

  it('shares concurrent lookups and caches results, retrying failures after a short time', async () => {
    let now = 0
    const fetch = fakeFetch({ 'https://example.com/post': html('<title>Page</title>') })
    const service = createLinkPreviewService(fetch, () => now)

    await Promise.all([service.get('https://example.com/post'), service.get('https://example.com/post#top')])
    await service.get('https://example.com/post')
    const pageCalls = () => fetch.mock.calls.filter(([url]) => url === 'https://example.com/post').length
    expect(pageCalls()).toBe(1)

    const failing = fakeFetch({})
    const flaky = createLinkPreviewService(failing, () => now)
    await flaky.get('https://example.com/post')
    await flaky.get('https://example.com/post')
    expect(failing).toHaveBeenCalledTimes(1)
    now += 31_000
    await flaky.get('https://example.com/post')
    expect(failing).toHaveBeenCalledTimes(2)
  })
})
