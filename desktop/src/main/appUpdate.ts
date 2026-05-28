import { app, net } from 'electron'
import { spawn } from 'child_process'
import { randomUUID } from 'crypto'
import { createWriteStream } from 'fs'
import { mkdir, rename, rm, stat, writeFile } from 'fs/promises'
import { basename, join } from 'path'
import { Readable } from 'stream'

import {
  DOTCRAFT_RELEASES_API_URL,
  isAllowedReleaseDownloadUrl,
  resolveUpdateFromRelease,
  type AppUpdateInfo,
  type AppUpdateProgress,
  type AppUpdateState,
  type GitHubRelease
} from '../shared/appUpdate'

type FetchImpl = (url: string, init?: RequestInit) => Promise<Response>

interface AppUpdateServiceOptions {
  currentVersion?: string
  platform?: NodeJS.Platform
  arch?: string
  cacheRoot?: string
  fetchImpl?: FetchImpl
  onStateChanged?: (state: AppUpdateState) => void
  quitAndInstall?: (installerPath: string) => void
}

const UPDATE_CHECK_TIMEOUT_MS = 20_000
const UPDATE_DOWNLOAD_TIMEOUT_MS = 10 * 60_000

export class AppUpdateService {
  private readonly currentVersion: string
  private readonly platform: NodeJS.Platform
  private readonly arch: string
  private readonly cacheRoot: string
  private readonly fetchImpl: FetchImpl
  private readonly onStateChanged?: (state: AppUpdateState) => void
  private readonly quitAndInstall: (installerPath: string) => void
  private state: AppUpdateState
  private checkPromise: Promise<AppUpdateState> | null = null
  private downloadPromise: Promise<AppUpdateState> | null = null

  constructor(options: AppUpdateServiceOptions = {}) {
    this.currentVersion = options.currentVersion ?? app.getVersion()
    this.platform = options.platform ?? process.platform
    this.arch = options.arch ?? process.arch
    this.cacheRoot = options.cacheRoot ?? defaultUpdateCacheRoot()
    this.fetchImpl = options.fetchImpl ?? net.fetch.bind(net)
    this.onStateChanged = options.onStateChanged
    this.quitAndInstall = options.quitAndInstall ?? quitAndRunInstaller
    this.state = {
      status: 'idle',
      currentVersion: this.currentVersion
    }
  }

  getState(): AppUpdateState {
    return cloneUpdateState(this.state)
  }

  checkForUpdates(): Promise<AppUpdateState> {
    if (this.checkPromise) return this.checkPromise
    if (this.state.status === 'downloading' || this.state.status === 'downloaded') {
      return Promise.resolve(this.getState())
    }

    this.checkPromise = this.runUpdateCheck()
      .finally(() => {
        this.checkPromise = null
      })
    return this.checkPromise
  }

  downloadAndInstall(): Promise<AppUpdateState> {
    if (this.downloadPromise) return this.downloadPromise

    this.downloadPromise = this.runDownloadAndInstall()
      .finally(() => {
        this.downloadPromise = null
      })
    return this.downloadPromise
  }

  private async runUpdateCheck(): Promise<AppUpdateState> {
    this.setState({ status: 'checking', currentVersion: this.currentVersion })

    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), UPDATE_CHECK_TIMEOUT_MS)
    try {
      const response = await this.fetchImpl(DOTCRAFT_RELEASES_API_URL, {
        signal: controller.signal,
        headers: {
          Accept: 'application/vnd.github+json',
          'User-Agent': `DotCraft/${this.currentVersion}`
        }
      })
      if (!response.ok) {
        throw new Error(`GitHub Releases check failed with HTTP ${response.status}.`)
      }

      const release = await response.json() as GitHubRelease
      const update = resolveUpdateFromRelease(
        this.currentVersion,
        release,
        this.platform,
        this.arch
      )
      if (!update) {
        this.setState({ status: 'not-available', currentVersion: this.currentVersion })
        return this.getState()
      }

      this.setState({
        status: 'available',
        currentVersion: this.currentVersion,
        update
      })
      return this.getState()
    } catch (error) {
      this.setState({
        status: 'error',
        currentVersion: this.currentVersion,
        update: this.state.update,
        error: normalizeError(error)
      })
      return this.getState()
    } finally {
      clearTimeout(timeout)
    }
  }

  private async runDownloadAndInstall(): Promise<AppUpdateState> {
    let update = this.state.update
    if (!update) {
      const checked = await this.checkForUpdates()
      update = checked.update
    }

    if (!update) {
      throw new Error('No DotCraft update is available for this platform.')
    }

    try {
      const installerPath = await this.downloadUpdate(update)
      this.setState({
        status: 'downloaded',
        currentVersion: this.currentVersion,
        update,
        progress: {
          transferredBytes: update.sizeBytes,
          totalBytes: update.sizeBytes,
          percent: 100
        }
      })
      setTimeout(() => this.quitAndInstall(installerPath), 600)
      return this.getState()
    } catch (error) {
      this.setState({
        status: 'error',
        currentVersion: this.currentVersion,
        update,
        progress: this.state.progress,
        error: normalizeError(error)
      })
      return this.getState()
    }
  }

  private async downloadUpdate(update: AppUpdateInfo): Promise<string> {
    if (!isAllowedReleaseDownloadUrl(update.downloadUrl)) {
      throw new Error('Release asset URL is not allowed.')
    }

    const releaseDir = join(this.cacheRoot, safeFileName(update.tagName))
    await mkdir(releaseDir, { recursive: true })
    const finalPath = join(releaseDir, safeFileName(update.assetName))
    const cached = await this.resolveCachedInstaller(update, finalPath)
    if (cached) return cached

    const tempPath = join(releaseDir, `${safeFileName(update.assetName)}.${randomUUID()}.tmp`)
    const controller = new AbortController()
    const timeout = setTimeout(() => controller.abort(), UPDATE_DOWNLOAD_TIMEOUT_MS)

    try {
      this.emitProgress(update, 0, update.sizeBytes)
      const response = await this.fetchImpl(update.downloadUrl, {
        signal: controller.signal,
        headers: {
          'User-Agent': `DotCraft/${this.currentVersion}`
        }
      })
      if (!response.ok) {
        throw new Error(`Update download failed with HTTP ${response.status}.`)
      }

      const headerBytes = Number.parseInt(response.headers.get('content-length') ?? '', 10)
      const totalBytes = Number.isFinite(headerBytes) && headerBytes > 0
        ? headerBytes
        : update.sizeBytes
      await writeResponseToFile(response, tempPath, (transferredBytes) => {
        this.emitProgress(update, transferredBytes, totalBytes)
      })

      const info = await stat(tempPath)
      if (update.sizeBytes > 0 && info.size !== update.sizeBytes) {
        throw new Error(`Downloaded size mismatch: expected ${update.sizeBytes}, got ${info.size}.`)
      }

      await rm(finalPath, { force: true }).catch(() => {})
      await rename(tempPath, finalPath)
      this.emitProgress(update, info.size, totalBytes || info.size)
      return finalPath
    } finally {
      clearTimeout(timeout)
      await rm(tempPath, { force: true }).catch(() => {})
    }
  }

  private async resolveCachedInstaller(update: AppUpdateInfo, path: string): Promise<string | null> {
    try {
      const info = await stat(path)
      if (update.sizeBytes > 0 && info.size !== update.sizeBytes) {
        await rm(path, { force: true })
        return null
      }
      this.emitProgress(update, info.size, update.sizeBytes || info.size)
      return path
    } catch {
      return null
    }
  }

  private emitProgress(update: AppUpdateInfo, transferredBytes: number, totalBytes: number): void {
    const progress = normalizeProgress(transferredBytes, totalBytes)
    this.setState({
      status: 'downloading',
      currentVersion: this.currentVersion,
      update,
      progress
    })
  }

  private setState(state: AppUpdateState): void {
    this.state = cloneUpdateState(state)
    this.onStateChanged?.(this.getState())
  }
}

export function defaultUpdateCacheRoot(): string {
  return join(app.getPath('userData'), 'updates')
}

export function quitAndRunInstaller(installerPath: string): void {
  const target = installerPath.trim()
  if (!target) return

  app.once('will-quit', () => {
    launchInstaller(target)
  })
  app.quit()
}

function launchInstaller(installerPath: string): void {
  try {
    const command = process.platform === 'darwin'
      ? 'open'
      : process.platform === 'linux'
        ? 'xdg-open'
        : installerPath
    const args = process.platform === 'win32' ? [] : [installerPath]
    const child = spawn(command, args, {
      detached: true,
      stdio: 'ignore',
      windowsHide: false
    })
    child.unref()
  } catch (error) {
    console.error('[desktop] failed to launch downloaded update installer', error)
  }
}

async function writeResponseToFile(
  response: Response,
  path: string,
  onProgress: (transferredBytes: number) => void
): Promise<void> {
  if (!response.body) {
    const buffer = Buffer.from(await response.arrayBuffer())
    await writeFile(path, buffer)
    onProgress(buffer.byteLength)
    return
  }

  await new Promise<void>((resolve, reject) => {
    let transferredBytes = 0
    const source = Readable.fromWeb(response.body as ReadableStream<Uint8Array>)
    const target = createWriteStream(path)

    source.on('data', (chunk: Buffer | Uint8Array | string) => {
      const bytes = typeof chunk === 'string' ? Buffer.byteLength(chunk) : chunk.byteLength
      transferredBytes += bytes
      onProgress(transferredBytes)
    })
    source.on('error', reject)
    target.on('error', reject)
    target.on('finish', resolve)
    source.pipe(target)
  })
}

function normalizeProgress(transferredBytes: number, totalBytes: number): AppUpdateProgress {
  const safeTransferred = Math.max(0, transferredBytes)
  const safeTotal = Math.max(0, totalBytes)
  const percent = safeTotal > 0
    ? Math.min(100, Math.max(0, Math.round((safeTransferred / safeTotal) * 1000) / 10))
    : 0
  return {
    transferredBytes: safeTransferred,
    totalBytes: safeTotal,
    percent
  }
}

function safeFileName(fileName: string): string {
  return basename(fileName).replace(/[^a-zA-Z0-9._-]/g, '_') || 'DotCraft-update'
}

function cloneUpdateState(state: AppUpdateState): AppUpdateState {
  return {
    status: state.status,
    currentVersion: state.currentVersion,
    update: state.update ? { ...state.update } : undefined,
    progress: state.progress ? { ...state.progress } : undefined,
    error: state.error
  }
}

function normalizeError(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
