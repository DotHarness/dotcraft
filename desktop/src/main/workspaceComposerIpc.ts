import { randomUUID } from 'crypto'
import { Worker } from 'worker_threads'
import { promises as fs } from 'fs'
import * as path from 'path'
import { translate, DEFAULT_LOCALE, type AppLocale } from '../shared/locales'
import { watch as fsWatch, type FSWatcher } from 'fs'
import { resolveBundledRipgrepPath } from './ripgrepRuntime'

const MAX_IMAGE_BYTES = 20 * 1024 * 1024

const MIME_TO_EXT: Record<string, string> = {
  'image/png': '.png',
  'image/jpeg': '.jpg',
  'image/jpg': '.jpg',
  'image/gif': '.gif',
  'image/webp': '.webp',
  'image/bmp': '.bmp'
}

/**
 * Kept intentionally minimal: the workspace's own ignore files are the source of truth
 * for everything else. `.git/` is listed only because ignore-walk, unlike ripgrep, does
 * not skip it on its own.
 */
const BUILTIN_EXCLUDED_PATH_PREFIXES: readonly string[] = ['.git/', '.craft/']
const BUILTIN_EXCLUDED_PATH_EXACT: readonly string[] = ['.git', '.craft']

/**
 * `.gitignore`-syntax globs: `.craft` excludes the directory itself and `.craft/**`
 * everything below it. `.git/` is absent because ripgrep already excludes it.
 */
const RIPGREP_BUILTIN_EXCLUDE_GLOBS: readonly string[] = ['.craft', '.craft/**']

/** Debounce invalidating the in-memory index after fs.watch events (saves full rescans). */
const INDEX_INVALIDATE_DEBOUNCE_MS = 1200
const FILE_INDEX_CACHE_SCHEMA_VERSION = 1
const FILE_INDEX_IGNORE_CONFIG_VERSION = 'rg-builtin-ignore-v1'
const FILE_INDEX_CACHE_RELATIVE_PATH = path.join('.craft', 'cache', 'desktop-file-index-v1.json')
const CACHE_MAX_AGE_MS = 14 * 24 * 60 * 60 * 1000
const CACHE_MAX_FILE_BYTES = 64 * 1024 * 1024

/** Hard cap on entries we keep in memory / on disk. Beyond this, results are flagged truncated. */
const MAX_INDEX_ENTRIES = 200_000
/** Worker emits a progress update every this many entries, to drive UI feedback. */
const INDEX_PROGRESS_BATCH = 5_000

function envInt(name: string, fallback: number): number {
  const raw = process.env[name]
  if (!raw) return fallback
  const parsed = Number.parseInt(raw, 10)
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
}

/**
 * Hard timeout on a single worker run; if it elapses we terminate and start
 * fresh. Read lazily so tests can override `DOTCRAFT_INDEX_BUILD_TIMEOUT_MS`
 * before each case.
 */
function indexBuildTimeoutMs(): number {
  return envInt('DOTCRAFT_INDEX_BUILD_TIMEOUT_MS', 5 * 60 * 1000)
}
/**
 * If ripgrep produces zero output for this long, fall back to ignore-walk.
 * Read lazily so tests can override `DOTCRAFT_RG_QUIET_FALLBACK_MS` per case.
 */
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

/**
 * In an asar-packed build, `@vscode/ripgrep`'s `rgPath` points inside `app.asar` while the
 * binary actually lives in `app.asar.unpacked`, so spawn() needs the swapped path. Tests
 * override the result via `DOTCRAFT_RG_PATH_OVERRIDE` to exercise the fallback.
 */
function resolveRgPath(): string {
  const override = process.env.DOTCRAFT_RG_PATH_OVERRIDE
  if (override !== undefined) return override
  return resolveBundledRipgrepPath()
}

const FILE_INDEX_WORKER_SOURCE = String.raw`
const { parentPort, workerData } = require('worker_threads')
const { spawn } = require('child_process')
const path = require('path')
const fs = require('fs/promises')

const root = path.resolve(workerData.workspaceRoot)
const cachePath = workerData.cachePath
const rgPath = workerData.rgPath
const rgGlobs = workerData.rgGlobs || []
const excludedPrefixes = workerData.excludedPrefixes || []
const excludedExact = workerData.excludedExact || []
const maxEntries = workerData.maxEntries || 200000
const progressBatch = workerData.progressBatch || 5000
const quietFallbackMs = workerData.quietFallbackMs || 5000

function isExcluded(rel) {
  if (excludedExact.indexOf(rel) !== -1) return true
  for (let i = 0; i < excludedPrefixes.length; i++) {
    if (rel.indexOf(excludedPrefixes[i]) === 0) return true
  }
  return false
}

function makeEntry(relRaw) {
  const rel = relRaw.replace(/\\/g, '/')
  if (isExcluded(rel)) return null
  const base = path.basename(rel)
  if (base.endsWith('.pyc') || base.endsWith('.min.js')) return null
  const dirRaw = path.dirname(rel)
  const dir = dirRaw === '.' ? '' : dirRaw.replace(/\\/g, '/')
  return { relativePath: rel, name: base, dir: dir }
}

function reportProgress(count, source) {
  try {
    parentPort.postMessage({ type: 'progress', count: count, source: source })
  } catch (_) {
    /* parent went away */
  }
}

async function writeCache(entries, truncated, source) {
  const cache = {
    schemaVersion: workerData.schemaVersion,
    workspaceRoot: root,
    generatedAt: new Date().toISOString(),
    ignoreConfigVersion: workerData.ignoreConfigVersion,
    truncated: truncated,
    source: source,
    entries: entries
  }
  await fs.mkdir(path.dirname(cachePath), { recursive: true })
  const tmp = cachePath + '.tmp'
  await fs.writeFile(tmp, JSON.stringify(cache), 'utf8')
  await fs.rename(tmp, cachePath)
}

function runWithRipgrep() {
  return new Promise((resolve, reject) => {
    if (!rgPath) {
      reject(new Error('ripgrep binary path not configured'))
      return
    }
    // --no-require-git makes ripgrep honour .gitignore / .ignore even when
    // the workspace is not a git repo (matches ignore-walk's behaviour).
    const args = ['--files', '--hidden', '--no-messages', '--null', '--no-require-git']
    for (let i = 0; i < rgGlobs.length; i++) {
      args.push('--glob')
      args.push('!' + rgGlobs[i])
    }
    let child
    try {
      child = spawn(rgPath, args, { cwd: root, windowsHide: true })
    } catch (err) {
      reject(err)
      return
    }

    const entries = []
    let buffer = ''
    let truncated = false
    let sawAnyOutput = false
    let settled = false

    const quietTimer = setTimeout(() => {
      if (!sawAnyOutput && !settled) {
        settled = true
        try { child.kill() } catch (_) { /* ignore */ }
        reject(new Error('ripgrep produced no output within ' + quietFallbackMs + 'ms'))
      }
    }, quietFallbackMs)

    child.stdout.setEncoding('utf8')
    child.stdout.on('data', (chunk) => {
      sawAnyOutput = true
      buffer += chunk
      let nullIdx = buffer.indexOf('\0')
      while (nullIdx !== -1) {
        const piece = buffer.slice(0, nullIdx)
        buffer = buffer.slice(nullIdx + 1)
        if (piece) {
          if (entries.length >= maxEntries) {
            truncated = true
            try { child.kill() } catch (_) { /* ignore */ }
            break
          }
          const entry = makeEntry(piece)
          if (entry) {
            entries.push(entry)
            if (entries.length % progressBatch === 0) {
              reportProgress(entries.length, 'rg')
            }
          }
        }
        nullIdx = buffer.indexOf('\0')
      }
    })

    child.on('error', (err) => {
      if (settled) return
      settled = true
      clearTimeout(quietTimer)
      reject(err)
    })
    child.on('close', (code) => {
      if (settled) return
      settled = true
      clearTimeout(quietTimer)
      if (truncated) {
        resolve({ entries: entries, truncated: true, source: 'rg' })
        return
      }
      if (code === 0 || code === null) {
        // Flush trailing path (unlikely with --null, but safe).
        const tail = buffer.trim()
        if (tail) {
          const entry = makeEntry(tail)
          if (entry && entries.length < maxEntries) entries.push(entry)
        }
        resolve({ entries: entries, truncated: false, source: 'rg' })
      } else {
        reject(new Error('ripgrep exited with code ' + code))
      }
    })
  })
}

async function runWithIgnoreWalk() {
  const walk = require('ignore-walk')
  const paths = await walk({
    path: root,
    ignoreFiles: ['.gitignore', '.ignore'],
    includeEmpty: false
  })
  const entries = []
  let truncated = false
  for (let i = 0; i < paths.length; i++) {
    const entry = makeEntry(paths[i])
    if (!entry) continue
    if (entries.length >= maxEntries) {
      truncated = true
      break
    }
    entries.push(entry)
    if (entries.length % progressBatch === 0) {
      reportProgress(entries.length, 'walk')
    }
  }
  return { entries: entries, truncated: truncated, source: 'walk' }
}

;(async () => {
  let result
  try {
    result = await runWithRipgrep()
  } catch (rgErr) {
    reportProgress(0, 'walk')
    try {
      result = await runWithIgnoreWalk()
      result.rgError = rgErr && rgErr.message ? rgErr.message : String(rgErr)
    } catch (walkErr) {
      parentPort.postMessage({
        type: 'error',
        error: 'ripgrep failed (' + (rgErr && rgErr.message ? rgErr.message : String(rgErr)) +
               ') and ignore-walk failed (' + (walkErr && walkErr.message ? walkErr.message : String(walkErr)) + ')'
      })
      return
    }
  }
  try {
    await writeCache(result.entries, !!result.truncated, result.source)
  } catch (writeErr) {
    parentPort.postMessage({
      type: 'error',
      error: 'failed to write cache: ' + (writeErr && writeErr.message ? writeErr.message : String(writeErr))
    })
    return
  }
  parentPort.postMessage({
    type: 'success',
    entries: result.entries,
    pathCount: result.entries.length,
    truncated: !!result.truncated,
    source: result.source,
    rgError: result.rgError || null
  })
})().catch((error) => {
  parentPort.postMessage({
    type: 'error',
    error: error && error.message ? error.message : String(error)
  })
})
`

export interface FileMatchWire {
  name: string
  relativePath: string
  dir: string
}

export interface FileIndexEntry {
  relativePath: string
  name: string
  dir: string
}

export type FileIndexStatus = 'empty' | 'building' | 'ready'
export type FileIndexSource = 'rg' | 'walk'

export interface FileListResultWire {
  files: FileMatchWire[]
  indexStatus: FileIndexStatus
  indexedCount: number
  stale: boolean
}

interface FileIndexCacheWire {
  schemaVersion: number
  workspaceRoot: string
  generatedAt: string
  ignoreConfigVersion: string
  entries: FileIndexEntry[]
}

interface WorkerSuccessMessage {
  type: 'success'
  entries: FileIndexEntry[]
  pathCount: number
  truncated?: boolean
  source?: FileIndexSource
  rgError?: string | null
}

interface WorkerProgressMessage {
  type: 'progress'
  count: number
  source?: FileIndexSource
}

interface WorkerErrorMessage {
  type: 'error'
  error: string
}

type WorkerMessage = WorkerSuccessMessage | WorkerProgressMessage | WorkerErrorMessage

interface BuildProgress {
  count: number
  source?: FileIndexSource
}

let fileIndex: FileIndexEntry[] | null = null
let fileIndexWorkspace: string | null = null
let activeIndexWorkspace: string | null = null
let fileIndexWatcher: FSWatcher | null = null
let indexInvalidateDebounce: ReturnType<typeof setTimeout> | null = null
let fileIndexStale = false

/** Bumped on explicit invalidate or debounced watch invalidation so in-flight builds do not commit stale data. */
let fileIndexEpoch = 0

let indexBuildPending: Promise<FileIndexEntry[]> | null = null
let indexBuildPendingRoot: string | null = null
let indexCacheLoadPending: Promise<FileIndexEntry[] | null> | null = null
let indexCacheLoadPendingRoot: string | null = null
let buildWorker: Worker | null = null

const fileIndexBuildProgress = new Map<string, BuildProgress>()

function scheduleDebouncedIndexInvalidate(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
  }
  indexInvalidateDebounce = setTimeout(() => {
    indexInvalidateDebounce = null
    fileIndexEpoch++
    fileIndexStale = true
    if (activeIndexWorkspace) {
      void startBackgroundIndexBuild(activeIndexWorkspace, 'fs-watch-stale').catch(() => {})
    }
  }, INDEX_INVALIDATE_DEBOUNCE_MS)
}

function ensureFsWatchForWorkspace(resolvedRoot: string): void {
  if (fileIndexWatcher) {
    return
  }
  try {
    fileIndexWatcher = fsWatch(resolvedRoot, { recursive: true }, () => {
      scheduleDebouncedIndexInvalidate()
    })
  } catch {
    /* recursive watch unsupported or failed — index refreshes on next cold miss */
  }
}

function cachePathForWorkspace(resolvedRoot: string): string {
  return path.join(resolvedRoot, FILE_INDEX_CACHE_RELATIVE_PATH)
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

async function loadCacheForWorkspace(resolvedRoot: string): Promise<FileIndexEntry[] | null> {
  if (fileIndex && fileIndexWorkspace === resolvedRoot) return fileIndex
  if (indexCacheLoadPending && indexCacheLoadPendingRoot === resolvedRoot) {
    return indexCacheLoadPending
  }
  const p = (async () => {
    try {
      const raw = await fs.readFile(cachePathForWorkspace(resolvedRoot), 'utf8')
      const entries = parseFileIndexCache(raw, resolvedRoot)
      if (!entries) {
        return null
      }
      if (activeIndexWorkspace === resolvedRoot) {
        fileIndex = entries
        fileIndexWorkspace = resolvedRoot
        fileIndexStale = true
        ensureFsWatchForWorkspace(resolvedRoot)
      }
      return entries
    } catch {
      return null
    }
  })().finally(() => {
    indexCacheLoadPending = null
    indexCacheLoadPendingRoot = null
  })
  indexCacheLoadPending = p
  indexCacheLoadPendingRoot = resolvedRoot
  return p
}

function startBackgroundIndexBuild(resolvedRoot: string, reason: string): Promise<FileIndexEntry[]> {
  if (indexBuildPending && indexBuildPendingRoot === resolvedRoot) {
    return indexBuildPending
  }
  if (buildWorker) {
    buildWorker.terminate().catch(() => {})
    buildWorker = null
  }

  const snapshotEpoch = fileIndexEpoch
  fileIndexBuildProgress.set(resolvedRoot, { count: 0 })

  const worker = new Worker(FILE_INDEX_WORKER_SOURCE, {
    eval: true,
    workerData: {
      workspaceRoot: resolvedRoot,
      cachePath: cachePathForWorkspace(resolvedRoot),
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
  buildWorker = worker

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
      safeConsoleWrite('warn', `[fileIndex] build failed (reason=${reason}):`, err)
      throw err
    }
  ).finally(() => {
    cleanupTimers()
    if (buildWorker === worker) buildWorker = null
    if (indexBuildPending === p) {
      indexBuildPending = null
      indexBuildPendingRoot = null
    }
  })

  const timeoutMs = indexBuildTimeoutMs()
  timeoutHandle = setTimeout(() => {
    worker.terminate().catch(() => {})
    settleReject(new Error(`file index build timed out after ${timeoutMs}ms`))
  }, timeoutMs)

  worker.on('message', (message: WorkerMessage) => {
    if (message.type === 'progress') {
      if (activeIndexWorkspace === resolvedRoot) {
        fileIndexBuildProgress.set(resolvedRoot, {
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
    if (activeIndexWorkspace === resolvedRoot) {
      fileIndex = entries
      fileIndexWorkspace = resolvedRoot
      fileIndexStale = snapshotEpoch !== fileIndexEpoch
      fileIndexBuildProgress.set(resolvedRoot, {
        count: entries.length,
        ...(message.source ? { source: message.source } : {})
      })
      ensureFsWatchForWorkspace(resolvedRoot)
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
    if (buildWorker === worker) buildWorker = null
    if (indexBuildPending === p) {
      indexBuildPending = null
      indexBuildPendingRoot = null
    }
    if (!settled) {
      // Worker died without producing a success/error message; surface it so
      // the next call retries instead of leaving the promise dangling.
      settleReject(new Error(`file index worker exited with code ${code} before producing a result`))
    }
  })

  indexBuildPending = p
  indexBuildPendingRoot = resolvedRoot
  return p
}

export function activateFileIndexWorkspace(workspaceRoot: string): void {
  const trimmed = workspaceRoot.trim()
  if (!trimmed) {
    invalidateFileIndex()
    activeIndexWorkspace = null
    return
  }
  const resolved = path.resolve(trimmed)
  if (activeIndexWorkspace === resolved) return
  invalidateFileIndex()
  activeIndexWorkspace = resolved
  void cleanupWorkspaceCache(resolved).catch(() => {})
}

async function getAvailableIndex(
  workspaceRoot: string,
  reason: string
): Promise<{ entries: FileIndexEntry[]; status: FileIndexStatus; stale: boolean }> {
  const resolved = path.resolve(workspaceRoot)
  activateFileIndexWorkspace(resolved)
  if (fileIndex && fileIndexWorkspace === resolved) {
    if (fileIndexStale) {
      void startBackgroundIndexBuild(resolved, `${reason}-stale-revalidate`).catch(() => {})
      return { entries: fileIndex, status: 'building', stale: true }
    }
    return { entries: fileIndex, status: 'ready', stale: false }
  }

  const cached = await loadCacheForWorkspace(resolved)
  if (cached) {
    void startBackgroundIndexBuild(resolved, `${reason}-cache-revalidate`).catch(() => {})
    return { entries: cached, status: 'building', stale: true }
  }

  void startBackgroundIndexBuild(resolved, reason).catch(() => {})
  return { entries: [], status: 'building', stale: true }
}

export async function ensureFileIndex(workspaceRoot: string): Promise<FileIndexEntry[]> {
  const resolved = path.resolve(workspaceRoot)
  const available = await getAvailableIndex(resolved, 'ensure')
  if (available.entries.length > 0) return available.entries
  if (indexBuildPending && indexBuildPendingRoot === resolved) {
    return indexBuildPending
  }
  return startBackgroundIndexBuild(resolved, 'ensure-wait')
}

export function warmFileSearchIndex(workspaceRoot: string): void {
  if (!workspaceRoot.trim()) return
  void cleanupWorkspaceCache(workspaceRoot).catch(() => {})
  void getAvailableIndex(workspaceRoot, 'warm').catch(() => {
    /* ignore — next search will retry */
  })
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
      } catch {
        // Cache cleanup is best-effort and must never block composer startup.
      }
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

export async function searchWorkspaceFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileMatchWire[]> {
  const q = query.trim()
  if (!q) {
    return []
  }
  const { entries: index } = await getAvailableIndex(workspaceRoot, 'search')
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

export async function listWorkspaceFiles(
  workspaceRoot: string,
  query: string,
  limit: number
): Promise<FileListResultWire> {
  if (!workspaceRoot.trim()) {
    return { files: [], indexStatus: 'empty', indexedCount: 0, stale: false }
  }
  const resolved = path.resolve(workspaceRoot)
  const { entries, status, stale } = await getAvailableIndex(resolved, 'list')
  const q = query.trim()
  const files = q
    ? await searchWorkspaceFiles(resolved, q, limit)
    : [...entries]
        .sort((a, b) => a.relativePath.localeCompare(b.relativePath))
        .slice(0, limit)
        .map((e) => ({ name: e.name, relativePath: e.relativePath, dir: e.dir }))
  // While the worker is still walking, prefer the live progress count over the
  // (possibly stale) cache size so the UI shows a growing indexed-file count.
  const liveProgress = status === 'building'
    ? fileIndexBuildProgress.get(resolved)?.count ?? 0
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

export function invalidateFileIndex(): void {
  if (indexInvalidateDebounce) {
    clearTimeout(indexInvalidateDebounce)
    indexInvalidateDebounce = null
  }
  fileIndexEpoch++
  fileIndex = null
  fileIndexWorkspace = null
  fileIndexStale = false
  indexBuildPending = null
  indexBuildPendingRoot = null
  indexCacheLoadPending = null
  indexCacheLoadPendingRoot = null
  fileIndexBuildProgress.clear()
  if (buildWorker) {
    buildWorker.terminate().catch(() => {})
    buildWorker = null
  }
  if (fileIndexWatcher) {
    fileIndexWatcher.close()
    fileIndexWatcher = null
  }
}

function isPathWithin(parent: string, target: string): boolean {
  const resolvedParent = path.resolve(parent)
  const resolvedTarget = path.resolve(target)
  return resolvedTarget === resolvedParent || resolvedTarget.startsWith(`${resolvedParent}${path.sep}`)
}

function inferMimeTypeFromPath(absPath: string): string {
  const ext = path.extname(absPath).toLowerCase()
  return (
    Object.entries(MIME_TO_EXT).find(([, mappedExt]) => mappedExt === ext)?.[0]
    ?? 'image/png'
  )
}

/** Writes to `.craft/attachments/images/<uuid>.<ext>` under the workspace, not an OS temp dir. */
export async function saveImageDataUrlToTemp(
  workspaceRoot: string,
  dataUrl: string,
  suggestedFileName?: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolved = path.resolve(workspaceRoot)
  const match = /^data:([^;]+);base64,(.+)$/s.exec(dataUrl.trim())
  if (!match) {
    throw new Error(translate(locale, 'ipc.invalidImageDataUrl'))
  }
  const mime = match[1].trim().toLowerCase()
  const b64 = match[2].replace(/\s/g, '')
  const buf = Buffer.from(b64, 'base64')
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  if (!mime.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  const ext = MIME_TO_EXT[mime] ?? (path.extname(suggestedFileName ?? '') || '.png')
  const dir = path.join(resolved, '.craft', 'attachments', 'images')
  await fs.mkdir(dir, { recursive: true })
  const fileName = `${randomUUID()}${ext.startsWith('.') ? ext : `.${ext}`}`
  const absPath = path.join(dir, fileName)
  await fs.writeFile(absPath, buf)
  return absPath
}

/** Also accepts the legacy `.craft/tmp/images` location, not just `.craft/attachments/images`. */
export async function readImageAsDataUrl(
  workspaceRoot: string,
  absPath: string,
  locale: AppLocale = DEFAULT_LOCALE
): Promise<string> {
  const resolvedRoot = path.resolve(workspaceRoot)
  const resolvedPath = path.resolve(absPath)
  const attachmentsDir = path.join(resolvedRoot, '.craft', 'attachments', 'images')
  const legacyTmpDir = path.join(resolvedRoot, '.craft', 'tmp', 'images')
  const allowed =
    isPathWithin(attachmentsDir, resolvedPath) ||
    isPathWithin(legacyTmpDir, resolvedPath)
  if (!allowed) {
    throw new Error(
      translate(locale, 'ipc.pathOutsideWorkspace', { path: absPath })
    )
  }
  const buf = await fs.readFile(resolvedPath)
  if (buf.length > MAX_IMAGE_BYTES) {
    throw new Error(
      translate(locale, 'ipc.imageTooLarge', { bytes: buf.length, max: MAX_IMAGE_BYTES })
    )
  }
  const mimeType = inferMimeTypeFromPath(resolvedPath)
  if (!mimeType.startsWith('image/')) {
    throw new Error(translate(locale, 'ipc.clipboardNotImage'))
  }
  return `data:${mimeType};base64,${buf.toString('base64')}`
}
