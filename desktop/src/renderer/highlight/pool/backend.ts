import type { Grammar, PreparedDiff, PreparedFile } from '../prepare'
import type { Highlighter } from '../core'
import type {
  HighlightRequestMessage,
  HighlightResultMessage,
  HighlightTask,
  WorkerRequestMessage,
  WorkerResponseMessage
} from './protocol'

type PreparedRequest = PreparedFile | PreparedDiff

export type BackendState = 'waiting' | 'initializing' | 'initialized'

export interface HighlightBackend {
  readonly state: BackendState
  readonly totalSlots: number
  readonly busySlots: number
  readonly freeSlots: number
  readonly workersFailed: boolean
  ready: () => Promise<void>
  run: (task: HighlightTask) => Promise<HighlightResultMessage>
  terminate: () => void
}

export interface BackendOptions {
  poolSize?: number
  workerFactory?: () => Worker
  /** Run everything on the main thread. Used by tests and by jsdom. */
  disableWorkers?: boolean
}

/** Enough concurrency for the surfaces one screen can hold, without a worker per core. */
const DEFAULT_POOL_SIZE = 4

function defaultWorkerFactory(): Worker {
  return new Worker(new URL('./worker.ts', import.meta.url), { type: 'module' })
}

interface Slot {
  worker: Worker
  busy: boolean
  grammars: Set<string>
}

interface Pending {
  resolve: (message: WorkerResponseMessage) => void
  reject: (error: Error) => void
  slot: Slot
}

class PoolBackend implements HighlightBackend {
  private readonly poolSize: number
  private readonly workerFactory: () => Worker
  private readonly useWorkers: boolean
  private readonly slots: Slot[] = []
  private readonly pending = new Map<number, Pending>()
  private readonly waiters: ((slot: Slot | undefined) => void)[] = []
  private mainThread: Promise<Highlighter> | undefined
  private initialization: Promise<void> | undefined
  private boot: Grammar[] = []
  private nextMessageId = 0
  private syncBusy = 0
  private disposed = false

  state: BackendState = 'waiting'
  workersFailed = false

  constructor(options: BackendOptions) {
    this.poolSize = Math.max(1, options.poolSize ?? DEFAULT_POOL_SIZE)
    this.workerFactory = options.workerFactory ?? defaultWorkerFactory
    this.useWorkers = options.disableWorkers !== true && typeof Worker === 'function'
    if (!this.useWorkers) this.workersFailed = true
  }

  get totalSlots(): number {
    // The configured size, not the materialized one: reporting zero before the
    // workers exist would stop the pool ever calling `run`, which is what starts
    // initialization.
    return this.usingWorkers ? this.poolSize : 1
  }

  get busySlots(): number {
    return this.usingWorkers ? this.slots.filter((slot) => slot.busy).length : this.syncBusy
  }

  get freeSlots(): number {
    return Math.max(0, this.totalSlots - this.busySlots)
  }

  private get usingWorkers(): boolean {
    return this.useWorkers && !this.workersFailed
  }

  ready(): Promise<void> {
    this.initialization ??= this.initialize()
    return this.initialization
  }

  private async initialize(): Promise<void> {
    this.state = 'initializing'
    // Imported here, not at module scope: the grammar catalogue is ~250 import
    // thunks that app startup has no use for until a code surface exists.
    const { bootGrammars } = await import('../prepare')
    this.boot = await bootGrammars()

    if (this.useWorkers) {
      try {
        const startups: Promise<unknown>[] = []
        for (let index = 0; index < this.poolSize; index++) {
          const slot: Slot = { worker: this.workerFactory(), busy: true, grammars: new Set() }
          slot.worker.addEventListener('message', (event: MessageEvent<WorkerResponseMessage>) => {
            this.receive(slot, event.data)
          })
          slot.worker.addEventListener('error', () => { this.failWorkers() })
          this.slots.push(slot)
          for (const grammar of this.boot) slot.grammars.add(grammar.id)
          startups.push(this.send(slot, {
            type: 'initialize',
            id: this.nextMessageId++,
            grammars: this.boot
          }))
        }
        await Promise.all(startups)
      } catch {
        this.failWorkers()
      }
    }

    this.state = 'initialized'
  }

  /** In-flight requests are rejected, not retried; the caller's re-run lands on the main thread. */
  private failWorkers(): void {
    if (this.workersFailed) return
    this.workersFailed = true
    for (const [, entry] of this.pending) entry.reject(new Error('highlight worker failed'))
    this.pending.clear()
    this.terminateWorkers()
    this.releaseWaiters()
  }

  private releaseWaiters(): void {
    while (this.waiters.length > 0) this.waiters.shift()?.(undefined)
  }

  private receive(slot: Slot, message: WorkerResponseMessage): void {
    const entry = this.pending.get(message.id)
    if (entry === undefined) return
    this.pending.delete(message.id)
    slot.busy = false
    if (message.type === 'error') entry.reject(new Error(message.error))
    else entry.resolve(message)
    this.drainWaiters()
  }

  private send(slot: Slot, message: WorkerRequestMessage): Promise<WorkerResponseMessage> {
    return new Promise<WorkerResponseMessage>((resolve, reject) => {
      this.pending.set(message.id, { resolve, reject, slot })
      slot.worker.postMessage(message)
    })
  }

  private highlighter(): Promise<Highlighter> {
    // Imported here so the healthy worker path never pulls shiki's engine and
    // themes into the renderer bundle.
    this.mainThread ??= import('../core').then((core) => {
      const instance = core.createHighlighter()
      core.installGrammars(instance, this.boot)
      return instance
    })
    return this.mainThread
  }

  async run(task: HighlightTask): Promise<HighlightResultMessage> {
    if (this.disposed) throw new Error('highlighter backend terminated')
    await this.ready()

    const { prepareDiff, prepareFile } = await import('../prepare')
    const prepared = task.type === 'file'
      ? await prepareFile(task.request)
      : await prepareDiff(task.request)

    if (this.usingWorkers) {
      const slot = await this.acquireSlot()
      if (slot !== undefined) {
        try {
          const missing = prepared.grammars.filter((grammar) => !slot.grammars.has(grammar.id))
          for (const grammar of missing) slot.grammars.add(grammar.id)
          const response = await this.send(slot, this.message(task, prepared, missing))
          if (response.type === 'result') return response
          throw new Error('unexpected highlight worker response')
        } catch (error) {
          // A pool that died mid-request re-runs on the main thread; any other
          // failure belongs to the caller.
          if (!this.workersFailed) throw error
        }
      }
    }

    return this.runOnMainThread(task, prepared)
  }

  private message(
    task: HighlightTask,
    prepared: PreparedRequest,
    grammars: Grammar[]
  ): HighlightRequestMessage {
    const id = this.nextMessageId++
    if (task.type === 'file') {
      return {
        type: 'file',
        id,
        request: task.request,
        grammars,
        lang: 'lang' in prepared ? prepared.lang : undefined
      }
    }
    return {
      type: 'diff',
      id,
      request: task.request,
      grammars,
      deletionLang: 'deletionLang' in prepared ? prepared.deletionLang : undefined,
      additionLang: 'additionLang' in prepared ? prepared.additionLang : undefined
    }
  }

  /** Waits rather than falling back, or a burst would land on the thread the workers protect. */
  private acquireSlot(): Promise<Slot | undefined> {
    const free = this.slots.find((candidate) => !candidate.busy)
    if (free !== undefined) {
      free.busy = true
      return Promise.resolve(free)
    }
    return new Promise<Slot | undefined>((resolve) => { this.waiters.push(resolve) })
  }

  /** The slot is claimed here, not in the waiter, so the next {@link acquireSlot} cannot take it first. */
  private drainWaiters(): void {
    if (this.waiters.length === 0) return
    const slot = this.usingWorkers ? this.slots.find((candidate) => !candidate.busy) : undefined
    const waiter = this.waiters.shift() as (claimed: Slot | undefined) => void
    if (slot !== undefined) slot.busy = true
    waiter(slot)
  }

  private async runOnMainThread(
    task: HighlightTask,
    prepared: PreparedRequest
  ): Promise<HighlightResultMessage> {
    this.syncBusy++
    try {
      const [core, execute, highlighter] = await Promise.all([
        import('../core'),
        import('../execute'),
        this.highlighter()
      ])
      core.installGrammars(highlighter, prepared.grammars)

      if (task.type === 'file') {
        return {
          type: 'result',
          id: task.id,
          requestType: 'file',
          result: execute.executeFile(
            highlighter.core,
            task.request,
            'lang' in prepared ? prepared.lang : undefined
          )
        }
      }
      return {
        type: 'result',
        id: task.id,
        requestType: 'diff',
        result: execute.executeDiff(
          highlighter.core,
          task.request,
          'deletionLang' in prepared ? prepared.deletionLang : undefined,
          'additionLang' in prepared ? prepared.additionLang : undefined
        )
      }
    } finally {
      this.syncBusy--
    }
  }

  private terminateWorkers(): void {
    for (const slot of this.slots) slot.worker.terminate()
    this.slots.length = 0
  }

  terminate(): void {
    this.disposed = true
    for (const [, entry] of this.pending) entry.reject(new Error('highlighter backend terminated'))
    this.pending.clear()
    this.terminateWorkers()
    this.releaseWaiters()
  }
}

export function createBackend(options: BackendOptions = {}): HighlightBackend {
  return new PoolBackend(options)
}
