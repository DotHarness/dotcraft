import { app, net } from 'electron'
import { createHash, randomUUID } from 'crypto'
import { createReadStream } from 'fs'
import { mkdir, rename, rm, stat, writeFile } from 'fs/promises'
import { basename, join } from 'path'
import { pathToFileURL } from 'url'
import {
  getWhatsNewMediaStateKey,
  getWhatsNewReleasesByVersions,
  WHATS_NEW_REMOTE_MEDIA_BASE_URL,
  type WhatsNewCard,
  type WhatsNewRelease,
  type WhatsNewMediaState
} from '../shared/whatsNew'

export interface WhatsNewMediaAsset {
  releaseVersion: string
  cardId: string
  fileName: string
  url: string
  sizeBytes: number
  sha256: string
}

type WhatsNewMediaFetch = (url: string, init?: RequestInit) => Promise<Response>
type WhatsNewMediaAssetResolver = (
  releaseVersions: string[]
) => WhatsNewMediaAsset[] | Promise<WhatsNewMediaAsset[]>

interface WhatsNewMediaCacheOptions {
  cacheRoot?: string
  fetchImpl?: WhatsNewMediaFetch
  assetResolver?: WhatsNewMediaAssetResolver
  timeoutMs?: number
  onStateChanged?: (state: WhatsNewMediaState) => void
}

const DEFAULT_TIMEOUT_MS = 45_000

export function defaultWhatsNewCacheRoot(): string {
  return join(app.getPath('userData'), 'whats-new-cache')
}

export function isAllowedWhatsNewMediaUrl(url: string): boolean {
  return url.startsWith(WHATS_NEW_REMOTE_MEDIA_BASE_URL)
}

function toAsset(card: WhatsNewCard, releaseVersion: string): WhatsNewMediaAsset | null {
  if (!card.media) return null
  return {
    releaseVersion,
    cardId: card.id,
    fileName: card.media.fileName,
    url: card.media.url,
    sizeBytes: card.media.sizeBytes,
    sha256: card.media.sha256.toUpperCase()
  }
}

export function resolveWhatsNewMediaAssets(
  releases: WhatsNewRelease[],
  releaseVersions: string[]
): WhatsNewMediaAsset[] {
  return getWhatsNewReleasesByVersions(releases, releaseVersions).flatMap((release) =>
    release.cards
      .map((card) => toAsset(card, release.version))
      .filter((asset): asset is WhatsNewMediaAsset => asset != null)
  )
}

function safeFileName(fileName: string): string {
  const name = basename(fileName).replace(/[^a-zA-Z0-9._-]/g, '_')
  return name || 'media.gif'
}

function assetKey(asset: WhatsNewMediaAsset): string {
  return `${asset.sha256}:${asset.url}`
}

async function sha256File(path: string): Promise<string> {
  const hash = createHash('sha256')
  await new Promise<void>((resolve, reject) => {
    const stream = createReadStream(path)
    stream.on('data', (chunk) => hash.update(chunk))
    stream.on('error', reject)
    stream.on('end', resolve)
  })
  return hash.digest('hex').toUpperCase()
}

export class WhatsNewMediaCache {
  private readonly cacheRoot: string
  private readonly fetchImpl: WhatsNewMediaFetch
  private readonly assetResolver: WhatsNewMediaAssetResolver
  private readonly timeoutMs: number
  private readonly onStateChanged?: (state: WhatsNewMediaState) => void
  private readonly inFlight = new Map<string, Promise<string>>()
  private readonly failures = new Map<string, string>()

  constructor(options: WhatsNewMediaCacheOptions = {}) {
    this.cacheRoot = options.cacheRoot ?? defaultWhatsNewCacheRoot()
    this.fetchImpl = options.fetchImpl ?? net.fetch.bind(net)
    this.assetResolver = options.assetResolver ?? (() => [])
    this.timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS
    this.onStateChanged = options.onStateChanged
  }

  async getMediaStates(releaseVersions: string[]): Promise<WhatsNewMediaState[]> {
    const assets = await this.assetResolver(releaseVersions)
    return await Promise.all(assets.map((asset) => this.getAssetState(asset)))
  }

  async prefetchMedia(releaseVersions: string[]): Promise<WhatsNewMediaState[]> {
    const assets = await this.assetResolver(releaseVersions)
    return await Promise.all(assets.map((asset) => this.prefetchAsset(asset)))
  }

  private cachePath(asset: WhatsNewMediaAsset): string {
    return join(
      this.cacheRoot,
      asset.releaseVersion,
      `${asset.sha256.slice(0, 12).toLowerCase()}-${safeFileName(asset.fileName)}`
    )
  }

  private state(
    asset: WhatsNewMediaAsset,
    status: WhatsNewMediaState['status'],
    options: { cachedUrl?: string; error?: string } = {}
  ): WhatsNewMediaState {
    return {
      releaseVersion: asset.releaseVersion,
      cardId: asset.cardId,
      status,
      ...options
    }
  }

  private emit(state: WhatsNewMediaState): void {
    this.onStateChanged?.(state)
  }

  private async getAssetState(asset: WhatsNewMediaAsset): Promise<WhatsNewMediaState> {
    if (!this.validateAsset(asset)) {
      return this.state(asset, 'failed', { error: 'Invalid media URL or file name.' })
    }
    const key = assetKey(asset)
    if (this.inFlight.has(key)) {
      return this.state(asset, 'downloading')
    }
    const cachedUrl = await this.resolveCachedUrl(asset)
    if (cachedUrl) {
      return this.state(asset, 'ready', { cachedUrl })
    }
    const failure = this.failures.get(key)
    if (failure) {
      return this.state(asset, 'failed', { error: failure })
    }
    return this.state(asset, 'missing')
  }

  private async prefetchAsset(asset: WhatsNewMediaAsset): Promise<WhatsNewMediaState> {
    if (!this.validateAsset(asset)) {
      const state = this.state(asset, 'failed', { error: 'Invalid media URL or file name.' })
      this.emit(state)
      return state
    }

    const key = assetKey(asset)
    const existing = this.inFlight.get(key)
    if (existing) {
      return await this.awaitDownload(asset, key, existing)
    }

    const cachedUrl = await this.resolveCachedUrl(asset)
    if (cachedUrl) {
      const state = this.state(asset, 'ready', { cachedUrl })
      this.emit(state)
      return state
    }

    const lateExisting = this.inFlight.get(key)
    if (lateExisting) {
      return await this.awaitDownload(asset, key, lateExisting)
    }

    this.failures.delete(key)
    const promise = this.downloadAsset(asset)
      .finally(() => {
        this.inFlight.delete(key)
      })

    this.inFlight.set(key, promise)
    return await this.awaitDownload(asset, key, promise)
  }

  private async awaitDownload(
    asset: WhatsNewMediaAsset,
    key: string,
    promise: Promise<string>
  ): Promise<WhatsNewMediaState> {
    const downloading = this.state(asset, 'downloading')
    this.emit(downloading)

    const settled = await promise
      .then((cachedUrl) => this.state(asset, 'ready', { cachedUrl }))
      .catch((error: unknown) => {
        const message = error instanceof Error ? error.message : String(error)
        this.failures.set(key, message)
        return this.state(asset, 'failed', { error: message })
      })
    this.emit(settled)
    return settled
  }

  private validateAsset(asset: WhatsNewMediaAsset): boolean {
    if (!isAllowedWhatsNewMediaUrl(asset.url)) return false
    if (safeFileName(asset.fileName) !== asset.fileName) return false
    if (asset.url !== `${WHATS_NEW_REMOTE_MEDIA_BASE_URL}${asset.fileName}`) return false
    return asset.sizeBytes > 0 && /^[0-9a-fA-F]{64}$/.test(asset.sha256)
  }

  private async resolveCachedUrl(asset: WhatsNewMediaAsset): Promise<string | null> {
    const path = this.cachePath(asset)
    try {
      const info = await stat(path)
      if (info.size !== asset.sizeBytes) {
        await rm(path, { force: true })
        return null
      }
      const hash = await sha256File(path)
      if (hash !== asset.sha256) {
        await rm(path, { force: true })
        return null
      }
      return pathToFileURL(path).href
    } catch {
      return null
    }
  }

  private async downloadAsset(asset: WhatsNewMediaAsset): Promise<string> {
    const finalPath = this.cachePath(asset)
    const dir = join(this.cacheRoot, asset.releaseVersion)
    await mkdir(dir, { recursive: true })
    const tempPath = join(dir, `${safeFileName(asset.fileName)}.${randomUUID()}.tmp`)

    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), this.timeoutMs)
    try {
      const response = await this.fetchImpl(asset.url, { signal: controller.signal })
      if (!response.ok) {
        throw new Error(`Download failed with HTTP ${response.status}.`)
      }
      const buffer = Buffer.from(await response.arrayBuffer())
      await writeFile(tempPath, buffer)

      const info = await stat(tempPath)
      if (info.size !== asset.sizeBytes) {
        throw new Error(`Downloaded size mismatch: expected ${asset.sizeBytes}, got ${info.size}.`)
      }
      const hash = await sha256File(tempPath)
      if (hash !== asset.sha256) {
        throw new Error('Downloaded SHA-256 mismatch.')
      }

      try {
        await rename(tempPath, finalPath)
      } catch {
        const existing = await this.resolveCachedUrl(asset)
        if (!existing) throw new Error('Failed to promote downloaded media into cache.')
        await rm(tempPath, { force: true })
        return existing
      }

      return pathToFileURL(finalPath).href
    } finally {
      clearTimeout(timeout)
      await rm(tempPath, { force: true }).catch(() => {})
    }
  }
}

export function whatsNewMediaStateKey(state: Pick<WhatsNewMediaState, 'releaseVersion' | 'cardId'>): string {
  return getWhatsNewMediaStateKey(state.releaseVersion, state.cardId)
}
