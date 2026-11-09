import { create } from 'zustand'
import type { ClientRequestMethods, ServerNotificationMethods, WorkflowRunSummary, WorkflowRunView } from '@dotcraft/sdk/contracts'

export interface WorkflowRunClient {
  connectionKey(): string
  onConnectionChange(listener: (connected: boolean) => void): () => void
  request<M extends keyof ClientRequestMethods>(
    method: M, params: ClientRequestMethods[M]['params']
  ): Promise<ClientRequestMethods[M]['result']>
  onNotification<M extends keyof ServerNotificationMethods>(
    method: M, listener: (params: ServerNotificationMethods[M]['params']) => void
  ): () => void
}

export interface WorkflowRunEntry {
  summary: WorkflowRunSummary | null
  run: WorkflowRunView | null
  loading: boolean
  error: string | null
}

export interface WorkflowRunState {
  scope: string
  entries: Map<string, WorkflowRunEntry>
  load(threadId: string, runId: string): Promise<void>
  list(threadId: string): Promise<void>
  refresh(threadId: string): Promise<void>
  stop(threadId: string, runId: string): Promise<void>
  observe(threadId: string, listener: (runs: readonly WorkflowRunSummary[]) => void): () => void
}

const keyOf = (threadId: string, runId: string): string => `${threadId}\u0000${runId}`
const active = (status: string): boolean => status === 'running' || status === 'paused'

function createWorkflowRunCache(client: WorkflowRunClient) {
  let epoch = 0
  let revision = 0
  let subscribed = false
  const versions = new Map<string, number>()
  const loads = new Map<string, Promise<void>>()
  const lists = new Map<string, Promise<void>>()
  const dirty = new Set<string>()
  const observers = new Map<string, number>()

  const store = create<WorkflowRunState>(() => ({
    scope: client.connectionKey(),
    entries: new Map(),
    load,
    list,
    refresh,
    stop,
    observe
  }))

  const current = (started: number): boolean =>
    started === epoch && client.connectionKey() === store.getState().scope

  function resetConnection(): void {
    epoch += 1
    loads.clear()
    lists.clear()
    dirty.clear()
    versions.clear()
    if (store.getState().scope !== client.connectionKey()) {
      store.setState({ scope: client.connectionKey(), entries: new Map() })
    }
  }

  function ensureConnection(): void {
    if (store.getState().scope !== client.connectionKey()) resetConnection()
    if (subscribed) return
    subscribed = true
    client.onNotification('workflow/run/updated', ({ threadId, runId }) => {
      ensureConnection()
      const key = keyOf(threadId, runId)
      if (!observers.has(threadId) && !store.getState().entries.has(key)) return
      if (loads.has(key)) dirty.add(key)
      void load(threadId, runId)
    })
    client.onConnectionChange((connected) => {
      resetConnection()
      if (!connected) return
      for (const threadId of observers.keys()) void list(threadId)
      for (const key of store.getState().entries.keys()) {
        const [threadId, runId] = key.split('\u0000')
        void load(threadId, runId)
      }
    })
  }

  function version(key: string): number {
    const next = ++revision
    versions.set(key, next)
    return next
  }

  function update(key: string, patch: Partial<WorkflowRunEntry>): void {
    store.setState(({ entries }) => {
      const next = new Map(entries)
      next.set(key, { ...(entries.get(key) ?? { summary: null, run: null, loading: false, error: null }), ...patch })
      return { entries: next }
    })
  }

  function applyRun(key: string, run: WorkflowRunView): void {
    version(key)
    update(key, { run, summary: run, loading: false, error: null })
  }

  function load(threadId: string, runId: string): Promise<void> {
    ensureConnection()
    const key = keyOf(threadId, runId)
    const existing = loads.get(key)
    if (existing) return existing
    const started = epoch
    const work = async (): Promise<void> => {
      do {
        dirty.delete(key)
        const generation = version(key)
        update(key, { loading: true, error: null })
        try {
          const result = await client.request('workflow/run/read', { threadId, runId })
          if (!current(started)) return
          if (versions.get(key) === generation) applyRun(key, result.run)
        } catch (error) {
          if (!current(started)) return
          if (versions.get(key) === generation) update(key, { loading: false, error: normalizeReadError(error) })
        }
      } while (current(started) && dirty.has(key))
    }
    const pending = work().finally(() => {
      if (loads.get(key) === pending) loads.delete(key)
    })
    loads.set(key, pending)
    return pending
  }

  function list(threadId: string): Promise<void> {
    ensureConnection()
    const existing = lists.get(threadId)
    if (existing) return existing
    const started = epoch
    const before = revision
    const work = async (): Promise<void> => {
      let cursor: string | undefined
      do {
        let page: ClientRequestMethods['workflow/run/list']['result']
        try {
          page = await client.request('workflow/run/list', { threadId, limit: 50, ...(cursor ? { cursor } : {}) })
        } catch {
          return
        }
        if (!current(started)) return
        for (const summary of [...(page.runs ?? [])].reverse()) {
          if (summary.threadId !== threadId) continue
          const key = keyOf(threadId, summary.runId)
          const entry = store.getState().entries.get(key)
          if ((versions.get(key) ?? 0) > before) continue
          version(key)
          update(key, {
            summary,
            run: entry?.run ? { ...entry.run, ...summary } : null,
            loading: false
          })
          if (loads.has(key)) dirty.add(key)
          else if (entry?.run && entry.run.status !== summary.status) void load(threadId, summary.runId)
        }
        cursor = page.nextCursor
      } while (cursor)
    }
    const pending = work().finally(() => {
      if (lists.get(threadId) === pending) lists.delete(threadId)
    })
    lists.set(threadId, pending)
    return pending
  }

  async function refresh(threadId: string): Promise<void> {
    ensureConnection()
    const known = [...store.getState().entries.values()]
      .filter((entry) => entry.summary?.threadId === threadId && (active(entry.summary.status) || entry.error))
    await Promise.all([list(threadId), ...known.map((entry) => load(threadId, entry.summary!.runId))])
  }

  async function stop(threadId: string, runId: string): Promise<void> {
    ensureConnection()
    const started = epoch
    const key = keyOf(threadId, runId)
    version(key)
    const result = await client.request('workflow/run/stop', { threadId, runId })
    if (!current(started)) return
    dirty.delete(key)
    applyRun(key, result.run)
  }

  function observe(threadId: string, listener: (runs: readonly WorkflowRunSummary[]) => void): () => void {
    ensureConnection()
    const scope = store.getState().scope
    observers.set(threadId, (observers.get(threadId) ?? 0) + 1)
    const publish = (): void => {
      const { entries } = store.getState()
      listener(scope !== client.connectionKey() ? [] : [...entries.values()]
        .flatMap((entry) => entry.summary?.threadId === threadId && active(entry.summary.status) ? [entry.summary] : [])
        .sort((a, b) => (a.createdAt ?? '').localeCompare(b.createdAt ?? '')))
    }
    const unsubscribe = store.subscribe(publish)
    publish()
    return () => {
      unsubscribe()
      const count = (observers.get(threadId) ?? 1) - 1
      if (count > 0) observers.set(threadId, count)
      else observers.delete(threadId)
    }
  }

  return store
}

function normalizeReadError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error)
  const marker = message.lastIndexOf('Error: ')
  return marker >= 0 ? message.slice(marker + 'Error: '.length).trim() : message
}

const caches = new WeakMap<WorkflowRunClient, ReturnType<typeof createWorkflowRunCache>>()

export function workflowRunsFor(client: WorkflowRunClient) {
  let cache = caches.get(client)
  if (!cache) {
    cache = createWorkflowRunCache(client)
    caches.set(client, cache)
  }
  return cache
}

export function selectWorkflowRunEntry(
  entries: Map<string, WorkflowRunEntry>, threadId: string, runId: string
): WorkflowRunEntry | undefined {
  return entries.get(keyOf(threadId, runId))
}
