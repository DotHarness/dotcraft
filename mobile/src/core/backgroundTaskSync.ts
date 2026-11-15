import type { SubAgentChild, TerminalLifecycleNotification, ThreadRuntimeState } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import { agentTask, mergeAgents, terminalTask, upsertTask, workflowTask, type BackgroundTask, type TaskSource } from './backgroundTasks'
import { chatKey, type ComputerAction, type ComputerState } from './state'
import type { Store } from './store'
import { TerminalTaskSnapshots } from './terminalTaskSnapshots'

const WORKFLOW_PAGE = 200
const TURN_PAGE = 100

export class BackgroundTaskSync {
  private readonly loads = new Map<string, { dirty: boolean; done: Promise<void> }>()
  private readonly childTurns = new Map<string, { seen: string; status: string | null; tokens: number }>()
  private readonly sources: TaskSource[] = []
  private readonly canStopWorkflows: boolean
  private readonly observed = new Set<string>()
  private readonly runtimes = new Map<string, { version: number; runtime: ThreadRuntimeState | null }>()
  private runtimeVersion = 0
  private readonly terminals = new TerminalTaskSnapshots()
  private active = true

  constructor(
    private readonly client: DotCraftWireClient,
    private readonly projectId: string,
    private readonly store: Store<ComputerState, ComputerAction>,
  ) {
    const capabilities = client.initializeResult?.capabilities
    const workflows = capabilities?.extensions?.dynamicWorkflows
    if (capabilities?.backgroundTerminals) this.sources.push('shell')
    if (capabilities?.subAgentSessions) this.sources.push('agent')
    if (workflows?.list) this.sources.push('workflow')
    this.canStopWorkflows = workflows?.stop === true
    this.register()
  }

  private key(threadId: string): string {
    return chatKey(this.projectId, threadId)
  }

  private known(threadId: string | null | undefined): threadId is string {
    return Boolean(threadId && (this.observed.has(threadId) || this.store.getState().chats[this.key(threadId)]))
  }

  private tasksOf(threadId: string, source: TaskSource): BackgroundTask[] | undefined {
    return this.store.getState().tasks[this.key(threadId)]?.[source]
  }

  dispose(): void {
    this.active = false
  }

  private set(threadId: string, source: TaskSource, tasks: BackgroundTask[]): void {
    if (this.active) this.store.dispatch({ type: 'tasks', key: this.key(threadId), source, tasks })
  }

  private workflowTask(...args: Parameters<typeof workflowTask>): BackgroundTask {
    const task = workflowTask(...args)
    return this.canStopWorkflows ? task : { ...task, stop: null }
  }

  private register(): void {
    this.client.on('thread/status/changed', ({ threadId, runtime }) => {
      if (!this.active || !threadId) return
      this.runtimes.set(threadId, { version: ++this.runtimeVersion, runtime: runtime ?? null })
      if (!this.known(threadId)) return
      this.updateChildRuntime(threadId, runtime ?? null)
      this.childChanged(threadId)
      if (!runtime?.running) void this.load(threadId).catch(() => undefined)
    })
    const terminal = ({ terminal }: TerminalLifecycleNotification) => {
      if (!this.active || !terminal) return
      const current = this.terminals.observe(terminal)
      if (!current?.isBackground || !this.known(current.threadId)) return
      const task = terminalTask(current)
      if (task) this.set(task.ownerThreadId, 'shell', upsertTask(this.tasksOf(task.ownerThreadId, 'shell'), task))
    }
    if (this.sources.includes('shell')) {
      this.client.on('terminal/started', terminal)
      this.client.on('terminal/backgrounded', terminal)
      this.client.on('terminal/completed', terminal)
      this.client.on('terminal/cleaned', terminal)
    }
    if (this.sources.includes('agent')) {
      this.client.on('thread/tokenUsage/updated', ({ threadId, tokenUsage }) => {
        const total = tokenUsage?.total?.totalTokens
        if (this.active && threadId && total !== undefined) this.childUsage(threadId, total)
      })
      this.client.on('subagent/graph/changed', ({ parentThreadId }) => {
        if (this.active && this.known(parentThreadId)) void this.refresh(parentThreadId, 'agent').catch(() => undefined)
      })
    }
    if (this.sources.includes('workflow')) {
      this.client.on('workflow/run/updated', ({ threadId }) => {
        if (this.active && this.known(threadId)) void this.refresh(threadId, 'workflow').catch(() => undefined)
      })
    }
  }

  observes(threadId: string): boolean {
    return this.observed.has(threadId)
  }

  async load(threadId: string): Promise<void> {
    if (!this.active) return
    if (!this.observed.has(threadId)) {
      const cached = this.terminals.ofThread(threadId).filter((terminal) => terminal.isBackground)
      if (cached.length) this.set(threadId, 'shell', cached.flatMap((terminal) => terminalTask(terminal) ?? []))
    }
    this.observed.add(threadId)
    await Promise.all(this.sources.map((source) => this.refresh(threadId, source)))
    this.settleChild(threadId)
  }

  reloadKnown(except: string[]): void {
    const prefix = `${this.projectId}:`
    for (const key of Object.keys(this.store.getState().tasks)) {
      const threadId = key.slice(prefix.length)
      if (key.startsWith(prefix) && this.store.getState().chats[key] && !except.includes(threadId)) void this.load(threadId).catch(() => undefined)
    }
  }

  private updateChildRuntime(childThreadId: string, runtime: ThreadRuntimeState | null): void {
    this.forParents(childThreadId, (task) => ({
      ...task,
      status: runtime?.running ? 'running' : task.status === 'running' ? 'completed' : task.status,
      activeTurnId: runtime?.activeTurnId ?? null,
      stop: runtime?.activeTurnId ? childThreadId : null,
      reconciling: !runtime?.running && (task.status === 'running' || task.reconciling === true),
    }))
  }

  private settleChild(childThreadId: string): void {
    this.forParents(childThreadId, (task) => task.reconciling ? { ...task, reconciling: false } : task)
  }

  private forParents(childThreadId: string, update: (task: Extract<BackgroundTask, { kind: 'agent' }>) => BackgroundTask): void {
    const prefix = `${this.projectId}:`
    for (const [key, sources] of Object.entries(this.store.getState().tasks)) {
      if (!key.startsWith(prefix)) continue
      const task = sources.agent?.find((entry) => entry.id === childThreadId)
      if (task?.kind !== 'agent' || !task.open) continue
      const next = update(task)
      if (next !== task) this.set(key.slice(prefix.length), 'agent', upsertTask(sources.agent, next))
    }
  }

  private childChanged(childThreadId: string): void {
    const prefix = `${this.projectId}:`
    for (const [key, sources] of Object.entries(this.store.getState().tasks)) {
      if (!key.startsWith(prefix) || !sources.agent?.some((task) => task.kind === 'agent' && task.open && task.id === childThreadId)) continue
      void this.refresh(key.slice(prefix.length), 'agent').catch(() => undefined)
      return
    }
  }

  private refresh(threadId: string, source: TaskSource): Promise<void> {
    if (!this.active) return Promise.resolve()
    const id = `${source}:${threadId}`
    const running = this.loads.get(id)
    if (running) {
      running.dirty = true
      return running.done
    }
    const entry = { dirty: false, done: Promise.resolve() }
    this.loads.set(id, entry)
    entry.done = (async () => {
      try {
        do {
          entry.dirty = false
          await this.fetch(threadId, source)
        } while (entry.dirty)
      } finally {
        this.loads.delete(id)
      }
    })()
    return entry.done
  }

  private async fetch(threadId: string, source: TaskSource): Promise<void> {
    switch (source) {
      case 'shell': {
        const version = this.terminals.version
        const { terminals } = await this.client.request('terminal/list', { threadId })
        const background = this.terminals.reconcile(threadId, terminals ?? [], version).filter((terminal) => terminal.isBackground)
        this.set(threadId, source, background.flatMap((terminal) => terminalTask(terminal) ?? []))
        return
      }
      case 'agent': {
        const version = this.runtimeVersion
        const { data } = await this.client.request('subagent/children/list', { parentThreadId: threadId, includeThreads: true })
        if (!this.active || this.loads.get(`agent:${threadId}`)?.dirty) return
        const children = (data ?? []).filter((child) => child.edge?.status !== 'closed').map((child) => {
          const id = child.edge?.childThreadId
          const latest = id ? this.runtimes.get(id) : undefined
          return latest && latest.version > version && latest.runtime && child.thread
            ? { ...child, thread: { ...child.thread, runtime: latest.runtime } } : child
        })
        const turns = await Promise.all(children.map((child) => this.childTurnsOf(child)))
        if (!this.active || this.loads.get(`agent:${threadId}`)?.dirty) return
        const listed = this.tasksOf(threadId, source)
        const tasks = children.flatMap((child, index) => {
          const current = listed?.find((task) => task.id === child.edge?.childThreadId)
          const live = current?.kind === 'agent' ? current.tokens : null
          const tokens = turns[index] ? Math.max(turns[index].tokens, live ?? 0) : live
          const task = agentTask(child, turns[index]?.status ?? null, tokens)
          if (!task || task.kind !== 'agent') return []
          return { ...task, reconciling: current?.kind === 'agent' && (current.reconciling || (current.status === 'running' && task.status !== 'running')) }
        })
        const discoveries = children.flatMap((child) => {
          const id = child.edge?.childThreadId
          return id && id !== threadId ? [this.load(id)] : []
        })
        this.set(threadId, source, mergeAgents(this.tasksOf(threadId, source), tasks))
        await Promise.all(discoveries)
        return
      }
      case 'workflow': {
        const runs = []
        let cursor: string | undefined
        do {
          const page = await this.client.request('workflow/run/list', { threadId, limit: WORKFLOW_PAGE, ...(cursor ? { cursor } : {}) })
          runs.push(...page.runs)
          cursor = page.nextCursor ?? undefined
        } while (cursor)
        if (!this.loads.get(`workflow:${threadId}`)?.dirty) this.set(threadId, source, runs.map((run) => this.workflowTask(run)))
      }
    }
  }

  private childUsage(childThreadId: string, tokens: number): void {
    const prefix = `${this.projectId}:`
    for (const [key, sources] of Object.entries(this.store.getState().tasks)) {
      const task = sources.agent?.find((entry) => entry.id === childThreadId)
      if (!key.startsWith(prefix) || task?.kind !== 'agent') continue
      this.set(key.slice(prefix.length), 'agent', upsertTask(sources.agent, { ...task, tokens: Math.max(task.tokens ?? 0, tokens) }))
    }
  }

  private async childTurnsOf({ edge, thread }: SubAgentChild): Promise<{ status: string | null; tokens: number } | null> {
    const id = edge?.childThreadId
    if (!id || edge.status === 'closed') return null
    const seen = `${thread?.lastActiveAt ?? edge.updatedAt ?? ''}:${thread?.runtime?.running === true}`
    const cached = this.childTurns.get(id)
    if (cached?.seen === seen) return cached
    try {
      let status: string | null = null
      let tokens = 0
      let cursor: string | undefined
      do {
        const page = await this.client.request('thread/turns/list', { threadId: id, limit: TURN_PAGE, sortDirection: 'descending', ...(cursor ? { cursor } : {}) })
        status ??= page.data?.[0]?.status ?? null
        for (const turn of page.data ?? []) tokens += turn.tokenUsage?.totalTokens ?? 0
        cursor = page.nextCursor ?? undefined
      } while (cursor)
      const entry = { seen, status: thread?.runtime?.running === true ? null : status, tokens }
      this.childTurns.set(id, entry)
      return entry
    } catch {
      return null
    }
  }

  async stop(task: BackgroundTask): Promise<void> {
    if (!task.stop) return
    const threadId = task.ownerThreadId
    switch (task.kind) {
      case 'shell': {
        const { terminal } = await this.client.request('terminal/stop', { sessionId: task.stop })
        const snapshot = terminal && this.terminals.observe(terminal)
        const stopped = snapshot && terminalTask(snapshot)
        if (stopped) this.set(threadId, 'shell', upsertTask(this.tasksOf(threadId, 'shell'), stopped))
        return
      }
      case 'agent': {
        const turnId = task.activeTurnId ?? (await this.client.request('thread/read', { threadId: task.stop })).thread.runtime?.activeTurnId
        if (turnId) await this.client.request('turn/interrupt', { threadId: task.stop, turnId })
        this.set(threadId, 'agent', upsertTask(this.tasksOf(threadId, 'agent'), {
          ...task, status: 'stopped', stop: null, activeTurnId: null, endedAt: new Date().toISOString(),
        }))
        return
      }
      case 'workflow': {
        const { run } = await this.client.request('workflow/run/stop', { threadId, runId: task.stop })
        this.set(threadId, 'workflow', upsertTask(this.tasksOf(threadId, 'workflow'), this.workflowTask(run)))
      }
    }
  }
}
