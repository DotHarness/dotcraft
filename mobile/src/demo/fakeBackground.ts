export interface FakeTerminal {
  sessionId: string
  threadId: string
  command: string
  status: 'running' | 'completed' | 'failed' | 'killed'
  startedAt: string
  completedAt?: string
  exitCode?: number
  output: string
}

export interface FakeAgent {
  childThreadId: string
  parentThreadId: string
  nickname: string
  role: string
  running: boolean
  closed?: boolean
  createdAt: string
  updatedAt: string
}

export interface FakeWorkflowRun {
  runId: string
  threadId: string
  name: string
  description: string
  status: 'running' | 'paused' | 'stopped' | 'succeeded' | 'failed' | 'interrupted'
  createdAt: string
  completedAt?: string
  agentCount: number
}

export interface FakeBackgroundSeed {
  terminals?: FakeTerminal[]
  agents?: FakeAgent[]
  workflows?: FakeWorkflowRun[]
}

type Outcome = { result?: unknown; error?: { code: number; message: string }; after?: () => void }

export interface FakeBackgroundHost {
  notify(threadId: string, method: string, params: Record<string, unknown>): void
  workflowFinished(run: FakeWorkflowRun): void
}

function stamp(): string {
  return new Date().toISOString()
}

function terminalBody(terminal: FakeTerminal): Record<string, unknown> {
  return {
    ...terminal,
    turnId: null,
    callId: null,
    workingDirectory: '',
    source: 'host',
    exitCode: terminal.exitCode ?? null,
    completedAt: terminal.completedAt ?? null,
    backgroundReason: 'runInBackground',
  }
}

function taskName(agent: FakeAgent): string {
  return agent.nickname.toLowerCase().replace(/[^a-z0-9]+/g, '_')
}

function agentBody(agent: FakeAgent): Record<string, unknown> {
  return {
    edge: {
      parentThreadId: agent.parentThreadId,
      childThreadId: agent.childThreadId,
      depth: 1,
      agentPath: `/root/${taskName(agent)}`,
      taskName: taskName(agent),
      agentNickname: agent.nickname,
      agentRole: agent.role,
      profileName: 'native',
      runtimeType: 'native',
      supportsClose: true,
      supportsSendMessage: true,
      supportsFollowupTask: true,
      status: agent.closed ? 'closed' : 'open',
      createdAt: agent.createdAt,
      updatedAt: agent.updatedAt,
    },
    thread: {
      id: agent.childThreadId,
      displayName: agent.nickname,
      createdAt: agent.createdAt,
      lastActiveAt: agent.updatedAt,
      source: { kind: 'subagent' },
      runtime: { running: agent.running, busy: agent.running, waitingOnApproval: false, waitingOnInput: false },
    },
  }
}

function runBody(run: FakeWorkflowRun): Record<string, unknown> {
  const active = run.status === 'running' || run.status === 'paused'
  return {
    ...run,
    startedAt: run.createdAt,
    totals: {
      agentCount: run.agentCount,
      queuedCount: 0,
      runningCount: active ? 1 : 0,
      completedCount: active ? run.agentCount - 1 : run.agentCount,
      failedCount: 0,
      stoppedCount: 0,
      replayedCount: 0,
      inputTokens: 0,
      outputTokens: 0,
      toolCallCount: 0,
    },
    controls: { canPause: run.status === 'running', canStop: active, canResume: !active },
    phases: [],
    unphasedAgents: [],
  }
}

export class FakeBackground {
  readonly terminals: FakeTerminal[]
  readonly agents: FakeAgent[]
  readonly workflows: FakeWorkflowRun[]

  constructor(
    seed: FakeBackgroundSeed,
    private readonly host: FakeBackgroundHost,
  ) {
    this.terminals = seed.terminals ?? []
    this.agents = seed.agents ?? []
    this.workflows = seed.workflows ?? []
  }

  handle(method: string, params: Record<string, unknown>): Outcome | null {
    switch (method) {
      case 'terminal/list':
        return { result: { terminals: this.terminals.filter((entry) => entry.threadId === params.threadId).map(terminalBody) } }
      case 'terminal/stop': {
        const terminal = this.terminals.find((entry) => entry.sessionId === params.sessionId)
        if (!terminal) return { error: { code: -32602, message: 'Unknown terminal session.' } }
        if (terminal.status === 'running') Object.assign(terminal, { status: 'killed', completedAt: stamp() })
        return { result: { terminal: terminalBody(terminal) }, after: () => this.host.notify(terminal.threadId, 'terminal/completed', { terminal: terminalBody(terminal) }) }
      }
      case 'subagent/children/list':
        return {
          result: {
            data: this.agents.filter((entry) => entry.parentThreadId === params.parentThreadId && (params.includeClosed === true || !entry.closed)).map(agentBody),
          },
        }
      case 'subagent/close': {
        const agent = this.agents.find((entry) => entry.parentThreadId === params.parentThreadId && `/root/${taskName(entry)}` === params.target)
        if (!agent) return { error: { code: -32602, message: 'Unknown agent.' } }
        Object.assign(agent, { running: false, closed: true, updatedAt: stamp() })
        return {
          result: { status: 'closed', supportsSendMessage: true, supportsFollowupTask: true, supportsClose: true },
          after: () => this.host.notify(agent.parentThreadId, 'subagent/graph/changed', { parentThreadId: agent.parentThreadId, childThreadId: agent.childThreadId }),
        }
      }
      case 'workflow/run/list': {
        const runs = this.workflows
          .filter((entry) => entry.threadId === params.threadId)
          .sort((left, right) => right.createdAt.localeCompare(left.createdAt))
        const start = Number(params.cursor ?? 0)
        const end = start + Math.min(Number(params.limit ?? 50), 200)
        return { result: { runs: runs.slice(start, end).map(runBody), ...(end < runs.length ? { nextCursor: String(end) } : {}) } }
      }
      case 'workflow/run/stop': {
        const run = this.workflows.find((entry) => entry.runId === params.runId && entry.threadId === params.threadId)
        if (!run) return { error: { code: -32602, message: 'workflow_run_not_found' } }
        if (run.status === 'running' || run.status === 'paused') Object.assign(run, { status: 'stopped', completedAt: stamp() })
        return { result: { run: runBody(run) }, after: () => this.updated(run, 'control') }
      }
      default:
        return null
    }
  }

  private updated(run: FakeWorkflowRun, reason: string): void {
    this.host.notify(run.threadId, 'workflow/run/updated', { threadId: run.threadId, runId: run.runId, reason })
  }

  finishWorkflow(runId: string): void {
    const run = this.workflows.find((entry) => entry.runId === runId)
    if (!run || run.status !== 'running') return
    Object.assign(run, { status: 'succeeded', completedAt: stamp() })
    this.updated(run, 'terminal')
    this.host.workflowFinished(run)
  }

  finishTerminal(sessionId: string): void {
    const terminal = this.terminals.find((entry) => entry.sessionId === sessionId)
    if (!terminal || terminal.status !== 'running') return
    Object.assign(terminal, { status: 'completed', exitCode: 0, completedAt: stamp() })
    this.host.notify(terminal.threadId, 'terminal/completed', { terminal: terminalBody(terminal) })
  }
}
