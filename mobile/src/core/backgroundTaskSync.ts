import type { TerminalLifecycleNotification } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import { agentTask, mergeAgents, terminalTask, upsertTask, workflowTask, type BackgroundTask, type TaskSource } from './backgroundTasks'
import { chatKey, type ComputerAction, type ComputerState } from './state'
import type { Store } from './store'

const WORKFLOW_PAGE = 200

export class BackgroundTaskSync {
  private readonly loads = new Map<string, { dirty: boolean; done: Promise<void> }>()
  private readonly sources: TaskSource[] = []
  private readonly canStopWorkflows: boolean

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
    return Boolean(threadId && this.store.getState().chats[this.key(threadId)])
  }

  private tasksOf(threadId: string, source: TaskSource): BackgroundTask[] | undefined {
    return this.store.getState().tasks[this.key(threadId)]?.[source]
  }

  private set(threadId: string, source: TaskSource, tasks: BackgroundTask[]): void {
    this.store.dispatch({ type: 'tasks', key: this.key(threadId), source, tasks })
  }

  private workflowTask(...args: Parameters<typeof workflowTask>): BackgroundTask {
    const task = workflowTask(...args)
    return this.canStopWorkflows ? task : { ...task, stop: null }
  }

  private register(): void {
    const terminal = ({ terminal }: TerminalLifecycleNotification) => {
      if (!terminal) return
      const task = terminalTask(terminal)
      if (task && this.known(terminal.threadId)) this.set(terminal.threadId, 'shell', upsertTask(this.tasksOf(terminal.threadId, 'shell'), task))
    }
    if (this.sources.includes('shell')) {
      this.client.on('terminal/started', terminal)
      this.client.on('terminal/completed', terminal)
      this.client.on('terminal/cleaned', terminal)
    }
    if (this.sources.includes('agent')) {
      this.client.on('subagent/graph/changed', ({ parentThreadId }) => {
        if (this.known(parentThreadId)) void this.refresh(parentThreadId, 'agent').catch(() => undefined)
      })
    }
    if (this.sources.includes('workflow')) {
      this.client.on('workflow/run/updated', ({ threadId }) => {
        if (this.known(threadId)) void this.refresh(threadId, 'workflow').catch(() => undefined)
      })
    }
  }

  async load(threadId: string): Promise<void> {
    await Promise.all(this.sources.map((source) => this.refresh(threadId, source)))
  }

  reloadKnown(except: string[]): void {
    const prefix = `${this.projectId}:`
    for (const key of Object.keys(this.store.getState().tasks)) {
      const threadId = key.slice(prefix.length)
      if (key.startsWith(prefix) && !except.includes(threadId)) void this.load(threadId).catch(() => undefined)
    }
  }

  childChanged(childThreadId: string): boolean {
    const prefix = `${this.projectId}:`
    for (const [key, sources] of Object.entries(this.store.getState().tasks)) {
      if (!key.startsWith(prefix) || !sources.agent?.some((task) => task.id === childThreadId)) continue
      void this.refresh(key.slice(prefix.length), 'agent').catch(() => undefined)
      return true
    }
    return false
  }

  private refresh(threadId: string, source: TaskSource): Promise<void> {
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
        const { terminals } = await this.client.request('terminal/list', { threadId })
        this.set(threadId, source, (terminals ?? []).flatMap((terminal) => terminalTask(terminal) ?? []))
        return
      }
      case 'agent': {
        const { data } = await this.client.request('subagent/children/list', { parentThreadId: threadId, includeThreads: true })
        this.set(threadId, source, mergeAgents(this.tasksOf(threadId, source), (data ?? []).flatMap((child) => agentTask(child) ?? [])))
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
        this.set(threadId, source, runs.map((run) => this.workflowTask(run)))
      }
    }
  }

  async stop(threadId: string, task: BackgroundTask): Promise<void> {
    if (!task.stop) return
    switch (task.kind) {
      case 'shell': {
        const { terminal } = await this.client.request('terminal/stop', { sessionId: task.stop })
        const stopped = terminal && terminalTask(terminal)
        if (stopped) this.set(threadId, 'shell', upsertTask(this.tasksOf(threadId, 'shell'), stopped))
        return
      }
      case 'agent':
        await this.client.request('subagent/close', { parentThreadId: threadId, target: task.stop })
        this.set(threadId, 'agent', upsertTask(this.tasksOf(threadId, 'agent'), { ...task, status: 'stopped', stop: null, endedAt: new Date().toISOString() }))
        return
      case 'workflow': {
        const { run } = await this.client.request('workflow/run/stop', { threadId, runId: task.stop })
        this.set(threadId, 'workflow', upsertTask(this.tasksOf(threadId, 'workflow'), this.workflowTask(run)))
      }
    }
  }
}
