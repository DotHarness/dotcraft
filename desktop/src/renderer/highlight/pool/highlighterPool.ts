import { createBackend, type BackendOptions, type BackendState, type HighlightBackend } from './backend'
import { LruMap } from './lru'
import type { HighlightTask } from './protocol'
import type {
  DiffHighlightRequest,
  DiffHighlightResult,
  FileHighlightRequest,
  FileHighlightResult
} from '../types'

export type HighlightSubscriber = object

export interface HighlighterPoolStats {
  managerState: BackendState
  totalWorkers: number
  busyWorkers: number
  workersFailed: boolean
  queuedTasks: number
  activeTasks: number
  fileCacheSize: number
  diffCacheSize: number
}

export interface HighlighterPoolOptions extends BackendOptions {
  cacheSize?: number
  backend?: HighlightBackend
}

const DEFAULT_CACHE_SIZE = 100

type TaskResult = FileHighlightResult | DiffHighlightResult

interface Task {
  id: number
  message: HighlightTask
  highlightKey: string | undefined
  subscribers: Map<HighlightSubscriber, (result: TaskResult) => void>
  /** Exempt from cancellation: a priming task has no subscriber by construction. */
  primeCache: boolean
}

export class HighlighterPool {
  private readonly options: HighlighterPoolOptions
  private backendInstance: HighlightBackend | undefined
  private readonly fileCache: LruMap<string, FileHighlightResult>
  private readonly diffCache: LruMap<string, DiffHighlightResult>
  private readonly queued: Task[] = []
  private readonly active = new Map<number, Task>()
  private readonly taskByKey = new Map<string, Task>()
  private readonly taskBySubscriber = new Map<HighlightSubscriber, Task>()
  private readonly statListeners = new Set<(stats: HighlighterPoolStats) => void>()
  private nextTaskId = 0
  private broadcastHandle: number | undefined

  constructor(options: HighlighterPoolOptions = {}) {
    this.options = options
    const cacheSize = options.cacheSize ?? DEFAULT_CACHE_SIZE
    this.fileCache = new LruMap(cacheSize)
    this.diffCache = new LruMap(cacheSize)
  }

  /**
   * Built lazily so {@link terminate} leaves the pool usable: React runs an
   * effect, its cleanup, and the effect again on every mount in development.
   */
  private get backend(): HighlightBackend {
    this.backendInstance ??= this.options.backend ?? createBackend(this.options)
    return this.backendInstance
  }

  warmUp(): void {
    // Anything queued while the backend was starting has no other dispatch trigger.
    void this.backend.ready().then(() => { this.drain() }).catch(() => undefined)
  }

  requestFile(
    subscriber: HighlightSubscriber,
    request: FileHighlightRequest,
    onResult: (result: FileHighlightResult) => void
  ): FileHighlightResult | undefined {
    const cached = request.cacheKey === undefined ? undefined : this.fileCache.get(request.cacheKey)
    if (cached !== undefined) {
      this.release(subscriber)
      return cached
    }
    this.submit(
      subscriber,
      { type: 'file', id: 0, request },
      keyFor('file', request.cacheKey),
      onResult as (result: TaskResult) => void
    )
    return undefined
  }

  requestDiff(
    subscriber: HighlightSubscriber,
    request: DiffHighlightRequest,
    onResult: (result: DiffHighlightResult) => void
  ): DiffHighlightResult | undefined {
    const cached = request.cacheKey === undefined ? undefined : this.diffCache.get(request.cacheKey)
    if (cached !== undefined) {
      this.release(subscriber)
      return cached
    }
    this.submit(
      subscriber,
      { type: 'diff', id: 0, request },
      keyFor('diff', request.cacheKey),
      onResult as (result: TaskResult) => void
    )
    return undefined
  }

  peekFile(cacheKey: string | undefined): FileHighlightResult | undefined {
    return cacheKey === undefined ? undefined : this.fileCache.get(cacheKey)
  }

  peekDiff(cacheKey: string | undefined): DiffHighlightResult | undefined {
    return cacheKey === undefined ? undefined : this.diffCache.get(cacheKey)
  }

  primeFile(request: FileHighlightRequest): void {
    this.prime({ type: 'file', id: 0, request }, keyFor('file', request.cacheKey), this.fileCache)
  }

  primeDiff(request: DiffHighlightRequest): void {
    this.prime({ type: 'diff', id: 0, request }, keyFor('diff', request.cacheKey), this.diffCache)
  }

  release(subscriber: HighlightSubscriber): void {
    const task = this.taskBySubscriber.get(subscriber)
    if (task === undefined) return
    this.taskBySubscriber.delete(subscriber)
    task.subscribers.delete(subscriber)
    if (task.primeCache || task.subscribers.size > 0) return
    // Only a queued task can still be called off; one already running lands in
    // the cache, which is worth more than the cancellation would have saved.
    const index = this.queued.indexOf(task)
    if (index !== -1) {
      this.queued.splice(index, 1)
      this.forgetKey(task)
    }
    this.scheduleBroadcast()
  }

  getStats(): HighlighterPoolStats {
    return {
      managerState: this.backend.state,
      totalWorkers: this.backend.totalSlots,
      busyWorkers: this.backend.busySlots,
      workersFailed: this.backend.workersFailed,
      queuedTasks: this.queued.length,
      activeTasks: this.active.size,
      fileCacheSize: this.fileCache.size,
      diffCacheSize: this.diffCache.size
    }
  }

  /** Updates are coalesced to one per frame. */
  subscribeToStats(listener: (stats: HighlighterPoolStats) => void): () => void {
    this.statListeners.add(listener)
    listener(this.getStats())
    return () => { this.statListeners.delete(listener) }
  }

  terminate(): void {
    this.queued.length = 0
    this.active.clear()
    this.taskByKey.clear()
    this.taskBySubscriber.clear()
    this.fileCache.clear()
    this.diffCache.clear()
    this.backendInstance?.terminate()
    this.backendInstance = undefined
  }

  private submit(
    subscriber: HighlightSubscriber,
    message: HighlightTask,
    highlightKey: string | undefined,
    onResult: (result: TaskResult) => void
  ): void {
    this.release(subscriber)
    const shared = highlightKey === undefined ? undefined : this.taskByKey.get(highlightKey)
    if (shared !== undefined) {
      shared.subscribers.set(subscriber, onResult)
      this.taskBySubscriber.set(subscriber, shared)
      this.scheduleBroadcast()
      return
    }
    const task: Task = {
      id: this.nextTaskId++,
      message,
      highlightKey,
      subscribers: new Map([[subscriber, onResult]]),
      primeCache: false
    }
    this.taskBySubscriber.set(subscriber, task)
    this.enqueue(task)
  }

  private prime(
    message: HighlightTask,
    highlightKey: string | undefined,
    cache: LruMap<string, TaskResult>
  ): void {
    const cacheKey = message.request.cacheKey
    if (highlightKey === undefined || cacheKey === undefined) return
    if (cache.has(cacheKey)) return
    const existing = this.taskByKey.get(highlightKey)
    if (existing !== undefined) {
      existing.primeCache = true
      return
    }
    this.enqueue({
      id: this.nextTaskId++,
      message,
      highlightKey,
      subscribers: new Map(),
      primeCache: true
    })
  }

  private enqueue(task: Task): void {
    this.queued.push(task)
    if (task.highlightKey !== undefined) this.taskByKey.set(task.highlightKey, task)
    this.scheduleBroadcast()
    this.drain()
    if (this.queued.length > 0) this.warmUp()
  }

  private drain(): void {
    while (this.queued.length > 0 && this.backend.freeSlots > 0) {
      const task = this.queued.shift() as Task
      this.active.set(task.id, task)
      void this.run(task)
    }
    this.scheduleBroadcast()
  }

  private async run(task: Task): Promise<void> {
    try {
      const response = await this.backend.run({ ...task.message, id: task.id })
      this.store(task, response.result)
      for (const [, callback] of task.subscribers) callback(response.result)
    } catch {
      // A failed tokenization leaves the caller on its plain-text fallback.
    } finally {
      this.active.delete(task.id)
      this.forgetKey(task)
      for (const [subscriber] of task.subscribers) this.taskBySubscriber.delete(subscriber)
      task.subscribers.clear()
      this.drain()
    }
  }

  private store(task: Task, result: TaskResult): void {
    const cacheKey = task.message.request.cacheKey
    if (cacheKey === undefined) return
    if (task.message.type === 'file') this.fileCache.set(cacheKey, result as FileHighlightResult)
    else this.diffCache.set(cacheKey, result as DiffHighlightResult)
  }

  private forgetKey(task: Task): void {
    if (task.highlightKey === undefined) return
    if (this.taskByKey.get(task.highlightKey) === task) this.taskByKey.delete(task.highlightKey)
  }

  private scheduleBroadcast(): void {
    if (this.statListeners.size === 0 || this.broadcastHandle !== undefined) return
    this.broadcastHandle = scheduleFrame(() => {
      this.broadcastHandle = undefined
      const stats = this.getStats()
      for (const listener of this.statListeners) listener(stats)
    })
  }
}

function scheduleFrame(callback: () => void): number {
  return typeof requestAnimationFrame === 'function'
    ? requestAnimationFrame(() => callback())
    : (setTimeout(callback, 0) as unknown as number)
}

function keyFor(type: 'file' | 'diff', cacheKey: string | undefined): string | undefined {
  return cacheKey === undefined ? undefined : `${type}:${cacheKey}`
}
