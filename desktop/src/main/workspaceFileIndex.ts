import { createHash } from 'crypto'
import { Worker } from 'worker_threads'
import { promises as fs, watch as fsWatch, type FSWatcher } from 'fs'
import * as path from 'path'
import { resolveBundledRipgrepPath } from './ripgrepRuntime'
import { chatFolderFor } from './chatFolderAccess'
import { resolveDefaultChatWorkspacePath } from './defaultChatWorkspace'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import { FILE_INDEX_WORKER_SOURCE } from './workspaceFileIndexWorker'
import type {
  FileIndexEntry, FileIndexCacheWire, FileIndexStatus, FileMatchWire,
  FileListResultWire, WorkerMessage, BuildProgress
} from './workspaceFileIndexTypes'
export type { FileIndexEntry, FileIndexStatus, FileIndexSource, FileMatchWire, FileListResultWire } from './workspaceFileIndexTypes'

const BUILTIN_EXCLUDED_PATH_PREFIXES: readonly string[] = ['.git/', '.craft/']
const BUILTIN_EXCLUDED_PATH_EXACT: readonly string[] = ['.git', '.craft']

// Ripgrep already excludes .git; both .craft globs are needed for the directory and its descendants.
const RIPGREP_BUILTIN_EXCLUDE_GLOBS: readonly string[] = ['.craft', '.craft/**']

const INDEX_INVALIDATE_DEBOUNCE_MS = 1200
const FILE_INDEX_CACHE_SCHEMA_VERSION = 1
const FILE_INDEX_IGNORE_CONFIG_VERSION = 'rg-builtin-ignore-v1'
const FILE_INDEX_CACHE_RELATIVE_PATH = path.join('.craft', 'cache', 'desktop-file-index-v1.json')
const CACHE_MAX_AGE_MS = 14 * 24 * 60 * 60 * 1000
const CACHE_MAX_FILE_BYTES = 64 * 1024 * 1024

const MAX_INDEX_ENTRIES = 200_000
const INDEX_PROGRESS_BATCH = 5_000

function envInt(name: string, fallback: number): number {
  const raw = process.env[name]
  if (!raw) return fallback
  const parsed = Number.parseInt(raw, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

function indexBuildTimeoutMs(): number {
  return envInt('DOTCRAFT_INDEX_BUILD_TIMEOUT_MS', 5 * 60 * 1000)
}
function ripgrepQuietFallbackMs(): number {
  return envInt('DOTCRAFT_RG_QUIET_FALLBACK_MS', 5_000)
}

function isBrokenStdIoError(error: unknown): boolean {
  const code = (error as NodeJS.ErrnoException | undefined)?.code
  if (code === 'EIO' || code === 'EPIPE') return true
  const message = error instanceof Error ? error.message : String(error ?? '')
  return /write EIO|EPIPE/i.test(message)
}

function safeConsoleWrite(method: 'log' | 'warn', ...args: unknown[]): void {
  try {
    console[method](...args)
  } catch (error) {
    if (!isBrokenStdIoError(error)) {
      throw error
    }
  }
}

function resolveRgPath(): string {
  const override = process.env.DOTCRAFT_RG_PATH_OVERRIDE
  if (override !== undefined) return override
  return resolveBundledRipgrepPath()
}

export function shouldInvalidateFileIndexForWatchEvent(
  filename: string | Buffer | null | undefined
): boolean {
  if (filename == null) return true
  const relativePath = filename.toString().replace(/\\/g, '/').replace(/^\.\/+/, '')
  return relativePath !== '.craft' && !relativePath.startsWith('.craft/')
}

function isValidCacheEntry(value: unknown): value is FileIndexEntry {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return false
  const candidate = value as Record<string, unknown>
  return (
    typeof candidate.relativePath === 'string' &&
    typeof candidate.name === 'string' &&
    typeof candidate.dir === 'string'
  )
}

function parseFileIndexCache(raw: string, resolvedRoot: string): FileIndexEntry[] | null {
  const parsed = JSON.parse(raw) as Partial<FileIndexCacheWire>
  if (parsed.schemaVersion !== FILE_INDEX_CACHE_SCHEMA_VERSION) return null
  if (parsed.ignoreConfigVersion !== FILE_INDEX_IGNORE_CONFIG_VERSION) return null
  if (path.resolve(parsed.workspaceRoot ?? '') !== resolvedRoot) return null
  if (!Array.isArray(parsed.entries)) return null
  if (!parsed.entries.every(isValidCacheEntry)) return null
  return parsed.entries
}

export async function cleanupWorkspaceCache(workspaceRoot: string): Promise<void> {
  const resolved = path.resolve(workspaceRoot)
  const cacheDir = path.join(resolved, '.craft', 'cache')
  let entries: Array<import('fs').Dirent>
  try {
    entries = await fs.readdir(cacheDir, { withFileTypes: true })
  } catch {
    return
  }

  const now = Date.now()
  await Promise.all(entries
    .filter((entry) => entry.isFile())
    .map(async (entry) => {
      const filePath = path.join(cacheDir, entry.name)
      try {
        const stats = await fs.stat(filePath)
        const expired = now - stats.mtimeMs > CACHE_MAX_AGE_MS
        const oversized = stats.size > CACHE_MAX_FILE_BYTES
        const invalid = await isInvalidKnownCache(filePath, entry.name, resolved)
        if (expired || oversized || invalid || entry.name.endsWith('.tmp')) {
          await fs.rm(filePath, { force: true })
        }
      } catch {}
    }))
}

async function isInvalidKnownCache(filePath: string, fileName: string, resolvedRoot: string): Promise<boolean> {
  if (fileName === path.basename(FILE_INDEX_CACHE_RELATIVE_PATH)) {
    try {
      const raw = await fs.readFile(filePath, 'utf8')
      return parseFileIndexCache(raw, resolvedRoot) == null
    } catch {
      return true
    }
  }

  return false
}

function scoreMatch(name: string, qLower: string): number {
  const nLower = name.toLowerCase()
  if (nLower === qLower) return 0
  if (nLower.startsWith(qLower)) return 1
  const idx = nLower.indexOf(qLower)
  if (idx >= 0) return 2 + idx
  return 1000
}

class WorkspaceFileIndex {
  private disposed = false
  private readonly cacheFilePath: string
  private fileIndex: FileIndexEntry[] | null = null
  private fileIndexWorkspace: string | null = null
  private activeIndexWorkspace: string | null = null
  private fileIndexWatcher: FSWatcher | null = null
  private indexInvalidateDebounce: ReturnType<typeof setTimeout> | null = null
  private fileIndexStale = false

  /** Bumped on explicit invalidate or debounced watch invalidation so in-flight builds do not commit stale data. */
  private fileIndexEpoch = 0

  private indexBuildPending: Promise<FileIndexEntry[]> | null = null
  private indexBuildPendingRoot: string | null = null
  private indexCacheLoadPending: Promise<FileIndexEntry[] | null> | null = null
  private indexCacheLoadPendingRoot: string | null = null
  private buildWorker: Worker | null = null

  private readonly fileIndexBuildProgress = new Map<string, BuildProgress>()

  constructor(root: string, ownerId: number) {
    const chatsWorkspace = resolveDefaultChatWorkspacePath()
    const chatFolder = chatFolderFor(chatsWorkspace, root, ownerId)
    const key = createHash('sha256').update(normalizeWorkspaceProjectKey(root)).digest('hex').slice(0, 24)
    this.cacheFilePath = chatFolder && sameWorkspaceProjectKey(chatFolder, root)
      ? path.join(chatsWorkspace, '.craft', 'cache', `desktop-file-index-v1-${key}.json`)
      : path.join(root, FILE_INDEX_CACHE_RELATIVE_PATH)
  }

  dispose(): void {
    this.disposed = true
    this.invalidateFileIndex()
    this.activeIndexWorkspace = null
  }

  private scheduleDebouncedIndexInvalidate(): void {
    if (this.indexInvalidateDebounce) {
      clearTimeout(this.indexInvalidateDebounce)
    }
    this.indexInvalidateDebounce = setTimeout(() => {
      this.indexInvalidateDebounce = null
      this.fileIndexEpoch++
      this.fileIndexStale = true
      if (this.activeIndexWorkspace) {
        void this.startBackgroundIndexBuild(this.activeIndexWorkspace, 'fs-watch-stale').catch(() => {})
      }
    }, INDEX_INVALIDATE_DEBOUNCE_MS)
  }

  private ensureFsWatchForWorkspace(resolvedRoot: string): void {
    if (this.fileIndexWatcher) {
      return
    }
    try {
      this.fileIndexWatcher = fsWatch(resolvedRoot, { recursive: true }, (_eventType, filename) => {
        // `.craft` is excluded from the index, and the index worker writes its cache there.
        // Rebuilding for those events creates a self-sustaining watch/write loop.
        if (!shouldInvalidateFileIndexForWatchEvent(filename)) return
        this.scheduleDebouncedIndexInvalidate()
      })
    } catch {
      /* recursive watch unsupported or failed — index refreshes on next cold miss */
    }
  }

  private async loadCacheForWorkspace(resolvedRoot: string): Promise<FileIndexEntry[] | null> {
    if (this.fileIndex && this.fileIndexWorkspace === resolvedRoot) return this.fileIndex
    if (this.indexCacheLoadPending && this.indexCacheLoadPendingRoot === resolvedRoot) {
      return this.indexCacheLoadPending
    }
    const p = (async () => {
      try {
        const raw = await fs.readFile(this.cacheFilePath, 'utf8')
        const entries = parseFileIndexCache(raw, resolvedRoot)
        if (!entries) {
          return null
        }
        if (this.activeIndexWorkspace === resolvedRoot) {
          this.fileIndex = entries
          this.fileIndexWorkspace = resolvedRoot
          this.fileIndexStale = true
          this.ensureFsWatchForWorkspace(resolvedRoot)
        }
        return entries
      } catch {
        return null
      }
    })().finally(() => {
      this.indexCacheLoadPending = null
      this.indexCacheLoadPendingRoot = null
    })
    this.indexCacheLoadPending = p
    this.indexCacheLoadPendingRoot = resolvedRoot
    return p
  }

  private startBackgroundIndexBuild(resolvedRoot: string, reason: string): Promise<FileIndexEntry[]> {
    if (this.disposed) return Promise.reject(new Error('File index owner closed.'))
    if (this.indexBuildPending && this.indexBuildPendingRoot === resolvedRoot) {
      return this.indexBuildPending
    }
    if (this.buildWorker) {
      this.buildWorker.terminate().catch(() => {})
      this.buildWorker = null
    }

    const snapshotEpoch = this.fileIndexEpoch
    this.fileIndexBuildProgress.set(resolvedRoot, { count: 0 })

    const worker = new Worker(FILE_INDEX_WORKER_SOURCE, {
      eval: true,
      workerData: {
        workspaceRoot: resolvedRoot,
        cachePath: this.cacheFilePath,
        schemaVersion: FILE_INDEX_CACHE_SCHEMA_VERSION,
        ignoreConfigVersion: FILE_INDEX_IGNORE_CONFIG_VERSION,
        rgPath: resolveRgPath(),
        rgGlobs: [...RIPGREP_BUILTIN_EXCLUDE_GLOBS],
        excludedPrefixes: [...BUILTIN_EXCLUDED_PATH_PREFIXES],
        excludedExact: [...BUILTIN_EXCLUDED_PATH_EXACT],
        maxEntries: MAX_INDEX_ENTRIES,
        progressBatch: INDEX_PROGRESS_BATCH,
        quietFallbackMs: ripgrepQuietFallbackMs()
      }
    })
    this.buildWorker = worker

    let settled = false
    let timeoutHandle: ReturnType<typeof setTimeout> | null = null
    let resolveBuild!: (entries: FileIndexEntry[]) => void
    let rejectBuild!: (err: Error) => void

    const cleanupTimers = (): void => {
      if (timeoutHandle) {
        clearTimeout(timeoutHandle)
        timeoutHandle = null
      }
    }

    const settleResolve = (entries: FileIndexEntry[]): void => {
      if (settled) return
      settled = true
      cleanupTimers()
      resolveBuild(entries)
    }
    const settleReject = (err: Error): void => {
      if (settled) return
      settled = true
      cleanupTimers()
      rejectBuild(err)
    }

    const p = new Promise<FileIndexEntry[]>((resolve, reject) => {
      resolveBuild = resolve
      rejectBuild = reject
    }).then(
      (entries) => entries,
      (err) => {
        if (!this.disposed) safeConsoleWrite('warn', `[fileIndex] build failed (reason=${reason}):`, err)
        throw err
      }
    ).finally(() => {
      cleanupTimers()
      if (this.buildWorker === worker) this.buildWorker = null
      if (this.indexBuildPending === p) {
        this.indexBuildPending = null
        this.indexBuildPendingRoot = null
      }
    })

    const timeoutMs = indexBuildTimeoutMs()
    timeoutHandle = setTimeout(() => {
      worker.terminate().catch(() => {})
      settleReject(new Error(`file index build timed out after ${timeoutMs}ms`))
    }, timeoutMs)

    worker.on('message', (message: WorkerMessage) => {
      if (message.type === 'progress') {
        if (this.activeIndexWorkspace === resolvedRoot) {
          this.fileIndexBuildProgress.set(resolvedRoot, {
            count: message.count,
            ...(message.source ? { source: message.source } : {})
          })
        }
        return
      }
      if (message.type === 'error') {
        settleReject(new Error(message.error))
        return
      }
      const entries = message.entries
      if (this.activeIndexWorkspace === resolvedRoot) {
        this.fileIndex = entries
        this.fileIndexWorkspace = resolvedRoot
        this.fileIndexStale = snapshotEpoch !== this.fileIndexEpoch
        this.fileIndexBuildProgress.set(resolvedRoot, {
          count: entries.length,
          ...(message.source ? { source: message.source } : {})
        })
        this.ensureFsWatchForWorkspace(resolvedRoot)
      }
      const sourceLabel = message.source ?? 'unknown'
      const truncatedNote = message.truncated ? ' (truncated)' : ''
      const fallbackNote = message.rgError ? ` (rg fallback: ${message.rgError})` : ''
      safeConsoleWrite(
        'log',
        `[fileIndex] built via ${sourceLabel} count=${entries.length}${truncatedNote}${fallbackNote}`
      )
      settleResolve(entries)
    })
    worker.once('error', (error) => {
      settleReject(error instanceof Error ? error : new Error(String(error)))
    })
    worker.once('exit', (code) => {
      if (this.buildWorker === worker) this.buildWorker = null
      if (this.indexBuildPending === p) {
        this.indexBuildPending = null
        this.indexBuildPendingRoot = null
      }
      if (!settled) {
        settleReject(new Error(`file index worker exited with code ${code} before producing a result`))
      }
    })

    this.indexBuildPending = p
    this.indexBuildPendingRoot = resolvedRoot
    return p
  }

  activateFileIndexWorkspace(workspaceRoot: string): void {
    const trimmed = workspaceRoot.trim()
    if (!trimmed) {
      this.invalidateFileIndex()
      this.activeIndexWorkspace = null
      return
    }
    const resolved = path.resolve(trimmed)
    if (this.activeIndexWorkspace === resolved) return
    this.invalidateFileIndex()
    this.activeIndexWorkspace = resolved
    void cleanupWorkspaceCache(resolved).catch(() => {})
  }

  private async getAvailableIndex(
    workspaceRoot: string,
    reason: string
  ): Promise<{ entries: FileIndexEntry[]; status: FileIndexStatus; stale: boolean }> {
    if (this.disposed) throw new Error('File index owner closed.')
    const resolved = path.resolve(workspaceRoot)
    this.activateFileIndexWorkspace(resolved)
    if (this.fileIndex && this.fileIndexWorkspace === resolved) {
      if (this.fileIndexStale) {
        void this.startBackgroundIndexBuild(resolved, `${reason}-stale-revalidate`).catch(() => {})
        return { entries: this.fileIndex, status: 'building', stale: true }
      }
      return { entries: this.fileIndex, status: 'ready', stale: false }
    }

    const cached = await this.loadCacheForWorkspace(resolved)
    if (cached) {
      void this.startBackgroundIndexBuild(resolved, `${reason}-cache-revalidate`).catch(() => {})
      return { entries: cached, status: 'building', stale: true }
    }

    void this.startBackgroundIndexBuild(resolved, reason).catch(() => {})
    return { entries: [], status: 'building', stale: true }
  }

  async ensureFileIndex(workspaceRoot: string): Promise<FileIndexEntry[]> {
    const resolved = path.resolve(workspaceRoot)
    const available = await this.getAvailableIndex(resolved, 'ensure')
    if (available.entries.length > 0) return available.entries
    if (this.indexBuildPending && this.indexBuildPendingRoot === resolved) {
      return this.indexBuildPending
    }
    return this.startBackgroundIndexBuild(resolved, 'ensure-wait')
  }

  warmFileSearchIndex(workspaceRoot: string): void {
    if (!workspaceRoot.trim()) return
    void cleanupWorkspaceCache(workspaceRoot).catch(() => {})
    void this.getAvailableIndex(workspaceRoot, 'warm').catch(() => {})
  }

  async searchWorkspaceFiles(
    workspaceRoot: string,
    query: string,
    limit: number
  ): Promise<FileMatchWire[]> {
    const q = query.trim()
    if (!q) {
      return []
    }
    const { entries: index } = await this.getAvailableIndex(workspaceRoot, 'search')
    const qLower = q.toLowerCase()
    const scored = index
      .map((e) => ({
        e,
        score: scoreMatch(e.name, qLower)
      }))
      .filter((x) => x.score < 1000)
      .sort((a, b) => {
        if (a.score !== b.score) return a.score - b.score
        return a.e.relativePath.localeCompare(b.e.relativePath)
      })
      .slice(0, limit)
      .map((x) => ({
        name: x.e.name,
        relativePath: x.e.relativePath,
        dir: x.e.dir
      }))
    return scored
  }

  async listWorkspaceFiles(
    workspaceRoot: string,
    query: string,
    limit: number
  ): Promise<FileListResultWire> {
    if (!workspaceRoot.trim()) {
      return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
    }
    const resolved = path.resolve(workspaceRoot)
    const { entries, status, stale } = await this.getAvailableIndex(resolved, 'list')
    const q = query.trim()
    const files = q
      ? await this.searchWorkspaceFiles(resolved, q, limit)
      : [...entries]
          .sort((a, b) => a.relativePath.localeCompare(b.relativePath))
          .slice(0, limit)
          .map((e) => ({ name: e.name, relativePath: e.relativePath, dir: e.dir }))
    const liveProgress = status === 'building'
      ? this.fileIndexBuildProgress.get(resolved)?.count ?? 0
      : 0
    const indexedCount = status === 'building'
      ? Math.max(entries.length, liveProgress)
      : entries.length
    return {
      files,
      indexStatus: status,
      indexedCount,
      stale
    }
  }

  invalidateFileIndex(): void {
    if (this.indexInvalidateDebounce) {
      clearTimeout(this.indexInvalidateDebounce)
      this.indexInvalidateDebounce = null
    }
    this.fileIndexEpoch++
    this.fileIndex = null
    this.fileIndexWorkspace = null
    this.fileIndexStale = false
    this.indexBuildPending = null
    this.indexBuildPendingRoot = null
    this.indexCacheLoadPending = null
    this.indexCacheLoadPendingRoot = null
    this.fileIndexBuildProgress.clear()
    if (this.buildWorker) {
      this.buildWorker.terminate().catch(() => {})
      this.buildWorker = null
    }
    if (this.fileIndexWatcher) {
      this.fileIndexWatcher.close()
      this.fileIndexWatcher = null
    }
  }
}

interface SharedIndex {
  index: WorkspaceFileIndex
  owners: Set<number>
}

const indexes = new Map<string, SharedIndex>()
const ownerRoots = new Map<number, string>()

function indexFor(root: string, ownerId: number): WorkspaceFileIndex {
  const resolved = path.resolve(root)
  const key = normalizeWorkspaceProjectKey(resolved)
  if (ownerRoots.get(ownerId) !== key) releaseFileIndex(ownerId)
  let entry = indexes.get(key)
  if (!entry) {
    entry = { index: new WorkspaceFileIndex(resolved, ownerId), owners: new Set() }
    indexes.set(key, entry)
  }
  entry.owners.add(ownerId)
  ownerRoots.set(ownerId, key)
  return entry.index
}

export function activateFileIndexWorkspace(root: string, ownerId: number): void {
  if (!root.trim()) { releaseFileIndex(ownerId); return }
  indexFor(root, ownerId).activateFileIndexWorkspace(root)
}
export function ensureFileIndex(root: string, ownerId: number): Promise<FileIndexEntry[]> {
  return indexFor(root, ownerId).ensureFileIndex(root)
}
export function warmFileSearchIndex(root: string, ownerId: number): void {
  if (root.trim()) indexFor(root, ownerId).warmFileSearchIndex(root)
}
export function searchWorkspaceFiles(root: string, query: string, limit: number, ownerId: number): Promise<FileMatchWire[]> {
  return indexFor(root, ownerId).searchWorkspaceFiles(root, query, limit)
}
export function listWorkspaceFiles(root: string, query: string, limit: number, ownerId: number): Promise<FileListResultWire> {
  if (!root.trim()) return Promise.resolve({ files: [], indexStatus: 'empty', indexedCount: 0, stale: false })
  return indexFor(root, ownerId).listWorkspaceFiles(root, query, limit)
}
export function invalidateFileIndex(ownerId: number): void {
  const key = ownerRoots.get(ownerId)
  if (key) indexes.get(key)?.index.invalidateFileIndex()
}
export function releaseFileIndex(ownerId: number): void {
  const key = ownerRoots.get(ownerId)
  if (!key) return
  ownerRoots.delete(ownerId)
  const entry = indexes.get(key)
  if (!entry || !entry.owners.delete(ownerId) || entry.owners.size > 0) return
  indexes.delete(key)
  entry.index.dispose()
}
