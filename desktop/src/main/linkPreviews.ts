import type { IpcMainInvokeEvent } from 'electron'
import { isPreviewableUrl, linkPreviewKey, type LinkPreviewResult } from '../shared/linkPreviews'
import { fetchLinkPreviewResource, LinkPreviewBlockedError, type LinkPreviewRequest, type LinkPreviewResponse } from './linkPreviewFetch'

type Fetch = (url: string, options: LinkPreviewRequest) => Promise<LinkPreviewResponse>

const PAGE_REQUEST: LinkPreviewRequest = {
  accept: 'text/html,application/xhtml+xml;q=0.9,*/*;q=0.1',
  maxBytes: 1024 * 1024,
  stopAt: '</head',
  timeoutMs: 8_000
}
const IMAGE_REQUEST: LinkPreviewRequest = { accept: 'image/*', maxBytes: 2 * 1024 * 1024, timeoutMs: 8_000 }
const FAVICON_REQUEST: LinkPreviewRequest = { accept: 'image/*', maxBytes: 100 * 1024, timeoutMs: 5_000 }

const MAX_TITLE_LENGTH = 300
const READY_TTL_MS = 10 * 60_000
const ERROR_TTL_MS = 30_000
const MAX_ENTRIES = 32
const MAX_CACHE_BYTES = 24 * 1024 * 1024
const MAX_CONCURRENT = 4

const NAMED_ENTITIES: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' }

function decodeEntities(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (match, entity: string) => {
    if (entity[0] === '#') {
      const code = entity[1] === 'x' || entity[1] === 'X' ? parseInt(entity.slice(2), 16) : parseInt(entity.slice(1), 10)
      return Number.isFinite(code) && code > 0 && code <= 0x10ffff ? String.fromCodePoint(code) : match
    }
    return NAMED_ENTITIES[entity.toLowerCase()] ?? match
  })
}

function cleanText(text: string | undefined): string | null {
  const cleaned = decodeEntities(text ?? '').replace(/\s+/g, ' ').trim()
  if (!cleaned) return null
  return cleaned.length > MAX_TITLE_LENGTH ? `${cleaned.slice(0, MAX_TITLE_LENGTH - 1).trimEnd()}…` : cleaned
}

function attributes(tag: string): Record<string, string> {
  const result: Record<string, string> = {}
  for (const match of tag.matchAll(/([^\s=/]+)\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/g)) {
    result[match[1].toLowerCase()] = decodeEntities(match[2] ?? match[3] ?? match[4] ?? '')
  }
  return result
}

function httpsUrl(value: string | undefined, base: string): string | null {
  if (!value?.trim()) return null
  try {
    const url = new URL(value.trim(), base)
    return url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}

export interface PageMetadata {
  title: string | null
  imageUrl: string | null
  faviconUrl: string | null
}

export function parsePageMetadata(html: string, pageUrl: string): PageMetadata {
  const meta: Record<string, string> = {}
  const icons: Array<{ rel: string; href: string }> = []
  for (const match of html.matchAll(/<(meta|link)\b([^>]*)>/gi)) {
    const attrs = attributes(match[2])
    if (match[1].toLowerCase() === 'meta') {
      const key = (attrs.property ?? attrs.name ?? '').toLowerCase()
      if (key && attrs.content !== undefined && meta[key] === undefined) meta[key] = attrs.content
    } else {
      const rel = (attrs.rel ?? '').toLowerCase().split(/\s+/)
      if (attrs.href && rel.some((value) => value === 'icon' || value === 'apple-touch-icon') && !/\.svg(?:[?#]|$)/i.test(attrs.href)) {
        icons.push({ rel: rel.includes('icon') ? 'icon' : 'apple-touch-icon', href: attrs.href })
      }
    }
  }
  const titleTag = /<title[^>]*>([\s\S]*?)<\/title>/i.exec(html)?.[1]
  const image = ['og:image:secure_url', 'og:image', 'og:image:url', 'twitter:image', 'twitter:image:src']
    .map((key) => httpsUrl(meta[key], pageUrl))
    .find(Boolean)
  const icon = icons.find((entry) => entry.rel === 'icon') ?? icons[0]
  return {
    title: cleanText(meta['og:title'] ?? meta['twitter:title'] ?? titleTag),
    imageUrl: image ?? null,
    faviconUrl: httpsUrl(icon?.href ?? '/favicon.ico', pageUrl)
  }
}

function decodeHtml(body: Buffer, contentType: string): string {
  const declared =
    /charset=["']?([\w-]+)/i.exec(contentType)?.[1] ??
    /<meta[^>]+charset=["']?([\w-]+)/i.exec(body.subarray(0, 2048).toString('latin1'))?.[1]
  try {
    return new TextDecoder(declared ?? 'utf-8').decode(body)
  } catch {
    return new TextDecoder('utf-8').decode(body)
  }
}

// The server's content type is ignored; only the bytes decide the image type.
function sniffImageType(bytes: Buffer, allowIcon: boolean): string | null {
  if (bytes.length >= 8 && bytes.readUInt32BE(0) === 0x89504e47) return 'image/png'
  if (bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff) return 'image/jpeg'
  if (bytes.length >= 6 && bytes.subarray(0, 4).toString('latin1') === 'GIF8') return 'image/gif'
  if (bytes.length >= 12 && bytes.subarray(0, 4).toString('latin1') === 'RIFF' && bytes.subarray(8, 12).toString('latin1') === 'WEBP') {
    return 'image/webp'
  }
  if (allowIcon && bytes.length >= 4 && bytes.readUInt32BE(0) === 0x00000100) return 'image/x-icon'
  return null
}

async function imageDataUrl(fetch: Fetch, url: string | null, options: LinkPreviewRequest, allowIcon: boolean): Promise<string | undefined> {
  if (!url) return undefined
  try {
    const { body } = await fetch(url, options)
    const type = sniffImageType(body, allowIcon)
    return type ? `data:${type};base64,${body.toString('base64')}` : undefined
  } catch {
    return undefined
  }
}

interface CacheEntry {
  result: LinkPreviewResult
  expiresAt: number
  bytes: number
}

function resultBytes(result: LinkPreviewResult): number {
  return result.status === 'ready' ? (result.imageDataUrl?.length ?? 0) + (result.faviconDataUrl?.length ?? 0) : 0
}

export function createLinkPreviewService(fetch: Fetch = fetchLinkPreviewResource, now: () => number = Date.now) {
  const cache = new Map<string, CacheEntry>()
  const inFlight = new Map<string, Promise<LinkPreviewResult>>()
  const waiting: Array<() => void> = []
  let active = 0
  let cachedBytes = 0

  function remember(key: string, result: LinkPreviewResult): void {
    const previous = cache.get(key)
    if (previous) {
      cachedBytes -= previous.bytes
      cache.delete(key)
    }
    const bytes = resultBytes(result)
    cache.set(key, { result, bytes, expiresAt: now() + (result.status === 'ready' ? READY_TTL_MS : ERROR_TTL_MS) })
    cachedBytes += bytes
    for (const [oldest, entry] of cache) {
      if (cache.size <= MAX_ENTRIES && cachedBytes <= MAX_CACHE_BYTES) break
      cache.delete(oldest)
      cachedBytes -= entry.bytes
    }
  }

  function recall(key: string): LinkPreviewResult | null {
    const entry = cache.get(key)
    if (!entry) return null
    cache.delete(key)
    if (entry.expiresAt <= now()) {
      cachedBytes -= entry.bytes
      return null
    }
    cache.set(key, entry)
    return entry.result
  }

  async function withSlot<T>(task: () => Promise<T>): Promise<T> {
    if (active >= MAX_CONCURRENT) await new Promise<void>((resolve) => waiting.push(resolve))
    active++
    try {
      return await task()
    } finally {
      active--
      waiting.shift()?.()
    }
  }

  async function lookup(url: string): Promise<LinkPreviewResult> {
    try {
      const page = await fetch(url, PAGE_REQUEST)
      if (!/html/i.test(page.contentType)) return { status: 'ready', title: null }
      const metadata = parsePageMetadata(decodeHtml(page.body, page.contentType), page.url)
      const [image, favicon] = await Promise.all([
        imageDataUrl(fetch, metadata.imageUrl, IMAGE_REQUEST, false),
        imageDataUrl(fetch, metadata.faviconUrl, FAVICON_REQUEST, true)
      ])
      return {
        status: 'ready',
        title: metadata.title,
        ...(image ? { imageDataUrl: image } : {}),
        ...(favicon ? { faviconDataUrl: favicon } : {})
      }
    } catch (error) {
      return { status: 'error', error: error instanceof LinkPreviewBlockedError ? 'blocked' : 'unavailable' }
    }
  }

  return {
    get(rawUrl: string): Promise<LinkPreviewResult> {
      if (!isPreviewableUrl(rawUrl)) return Promise.resolve({ status: 'error', error: 'blocked' })
      const key = linkPreviewKey(rawUrl)
      const cached = recall(key)
      if (cached) return Promise.resolve(cached)
      const pending = inFlight.get(key)
      if (pending) return pending
      const request = withSlot(() => lookup(key))
        .then((result) => {
          remember(key, result)
          return result
        })
        .finally(() => inFlight.delete(key))
      inFlight.set(key, request)
      return request
    }
  }
}

export const linkPreviews = createLinkPreviewService()

type HandleSafe = (channel: string, listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown) => void

export const LINK_PREVIEW_CHANNELS = ['linkPreview:get'] as const

export function registerLinkPreviewHandlers(handleSafe: HandleSafe): void {
  handleSafe('linkPreview:get', (_event, url) => linkPreviews.get(typeof url === 'string' ? url : ''))
}
