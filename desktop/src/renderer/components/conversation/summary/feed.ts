import type { DesktopPluginSubAgent } from '@dotcraft/plugin'
import { diffTotals, rolledBackTurns, toSummaryItem, type ChangeTotals, type SummaryItem } from './sources'
import type { SummaryServices } from './summaryServices'

export interface SummaryAutomation {
  readonly id: string
  readonly name: string
  readonly nextRunAt: string | null
  readonly paused: boolean
}

export interface SummaryPlan {
  readonly title: string
  readonly done: number
  readonly total: number
}

export interface SummaryTerminal {
  readonly sessionId: string
  readonly command: string
  readonly callId: string | null
  readonly startedAt: string | null
}

export interface SummaryWorkflow {
  readonly runId: string
  readonly name: string
  readonly paused: boolean
  readonly startedAt: string | null
}

export interface SummaryState {
  readonly ready: boolean
  readonly workflows: readonly SummaryWorkflow[]
  readonly subagents: readonly DesktopPluginSubAgent[]
  readonly automations: readonly SummaryAutomation[]
  readonly plan: SummaryPlan | null
  readonly items: readonly SummaryItem[]
  readonly turnDiffs: ReadonlyMap<string, ChangeTotals>
  readonly terminals: readonly SummaryTerminal[]
}

interface TerminalSnapshot {
  readonly sessionId?: string
  readonly threadId?: string
  readonly turnId?: string | null
  readonly callId?: string | null
  readonly command?: string
  readonly status?: string
  readonly startedAt?: string
}

interface WorkflowRunSnapshot {
  readonly runId: string
  readonly threadId: string
  readonly name: string
  readonly status: string
  readonly createdAt?: string
  readonly startedAt?: string
}

const ITEM_PAGE_LIMIT = 500
const TURN_PAGE_LIMIT = 100
const WORKFLOW_PAGE_LIMIT = 50

export function startSummaryFeed(
  services: SummaryServices,
  threadId: string,
  onChange: (state: SummaryState) => void
): () => void {
  let disposed = false
  let historyLoaded = false
  const pendingItems: SummaryItem[] = []
  let reconcileInFlight = false
  let reconcileQueued = false
  const runningTerminals = new Map<string, { command: string; turnId: string | null; callId: string | null; startedAt: string | null }>()
  const returnedCalls = new Set<string>()
  let pendingLoads = 5
  const endedTerminals = new Set<string>()
  let activeTurnId: string | null = null
  let runtimeReceived = false
  let state: SummaryState = {
    ready: false,
    workflows: [],
    subagents: services.subagents.list(threadId),
    automations: [],
    plan: null,
    items: [],
    turnDiffs: new Map(),
    terminals: []
  }

  const publish = (patch: Partial<SummaryState>): void => {
    if (disposed) return
    state = { ...state, ...patch }
    onChange(state)
  }

  const addItems = (incoming: readonly SummaryItem[]): void => {
    if (incoming.length === 0) return
    let returned = false
    for (const item of incoming) {
      const callId = item.type === 'toolResult' ? item.payload.callId : null
      if (typeof callId === 'string' && !returnedCalls.has(callId)) {
        returnedCalls.add(callId)
        returned = true
      }
    }
    const next = [...state.items]
    const keyOf = (item: SummaryItem): string => `${item.turnId}:${item.id}`
    const positions = new Map(next.map((item, index) => [keyOf(item), index]))
    for (const item of incoming) {
      const position = positions.get(keyOf(item))
      if (position === undefined) {
        positions.set(keyOf(item), next.length)
        next.push(item)
      } else {
        next[position] = item
      }
    }
    publish({ items: next })
    if (returned) publishTerminals()
  }

  const publishTerminals = (): void => publish({
    terminals: [...runningTerminals]
      .filter(([, terminal]) => !activeTurnId
        || terminal.turnId !== activeTurnId
        || (terminal.callId !== null && returnedCalls.has(terminal.callId)))
      .map(([sessionId, terminal]) => ({ sessionId, command: terminal.command, callId: terminal.callId, startedAt: terminal.startedAt }))
  })

  const trackTerminal = (terminal: TerminalSnapshot | undefined, ended = false): boolean => {
    const sessionId = terminal?.sessionId
    if (!terminal || !sessionId || terminal.threadId !== threadId || endedTerminals.has(sessionId)) return false
    if (!ended && terminal.status === 'running') {
      runningTerminals.set(sessionId, {
        command: terminal.command ?? '',
        turnId: terminal.turnId ?? null,
        callId: terminal.callId ?? null,
        startedAt: terminal.startedAt ?? null
      })
    } else {
      endedTerminals.add(sessionId)
      runningTerminals.delete(sessionId)
    }
    return true
  }

  const settleLoad = (): void => {
    pendingLoads -= 1
    if (pendingLoads === 0) publish({ ready: true })
  }

  const upsertWorkflow = (run: WorkflowRunSnapshot): void => {
    const others = state.workflows.filter((workflow) => workflow.runId !== run.runId)
    if (run.status !== 'running' && run.status !== 'paused') {
      if (others.length !== state.workflows.length) publish({ workflows: others })
      return
    }
    const index = state.workflows.findIndex((workflow) => workflow.runId === run.runId)
    const next = [...others]
    next.splice(index < 0 ? next.length : index, 0, {
      runId: run.runId,
      name: run.name,
      paused: run.status === 'paused',
      startedAt: run.startedAt ?? run.createdAt ?? null
    })
    publish({ workflows: next })
  }

  const loadWorkflows = async (): Promise<void> => {
    try {
      const result = await services.appServer.request('workflow/run/list', { threadId, limit: WORKFLOW_PAGE_LIMIT })
      if (disposed) return
      for (const run of [...(result.runs ?? [])].reverse()) upsertWorkflow(run)
    } catch {
      return
    }
  }

  const refreshWorkflow = async (runId: string): Promise<void> => {
    try {
      const result = await services.appServer.request('workflow/run/read', { threadId, runId })
      if (!disposed && result.run) upsertWorkflow(result.run)
    } catch {
      return
    }
  }

  const readItemsPage = (cursor: string | null) => services.appServer.request('thread/items/list', {
    threadId,
    sortDirection: 'ascending',
    limit: ITEM_PAGE_LIMIT,
    cursor
  })

  const loadHistory = async (): Promise<void> => {
    const loaded: SummaryItem[] = []
    let cursor: string | null = null
    try {
      do {
        const page = await readItemsPage(cursor)
        if (disposed) return
        for (const entry of page.data ?? []) {
          const item = toSummaryItem(entry.item, entry.turnId)
          if (item) loaded.push(item)
        }
        cursor = page.nextCursor ?? null
      } while (cursor)
    } catch {
      loaded.length = 0
    } finally {
      if (!disposed) {
        historyLoaded = true
        addItems([...loaded, ...pendingItems.splice(0)])
      }
    }
  }

  const loadAutomations = async (): Promise<void> => {
    try {
      const result = await services.appServer.request('automation/list', {})
      if (disposed) return
      publish({
        automations: (result.automations ?? [])
          .filter((automation) => automation.targetThreadId === threadId)
          .map(toAutomation)
      })
    } catch {
      return
    }
  }

  const loadThread = async (): Promise<void> => {
    try {
      const result = await services.appServer.request('thread/read', { threadId })
      if (disposed) return
      if (!runtimeReceived) activeTurnId = result.thread?.runtime?.activeTurnId ?? null
      publish({ plan: toPlan(result.thread?.plan) })
      publishTerminals()
    } catch {
      return
    }
  }

  const loadTerminals = async (): Promise<void> => {
    try {
      const result = await services.appServer.request('terminal/list', { threadId })
      if (disposed) return
      for (const terminal of result.terminals ?? []) trackTerminal(terminal)
      publishTerminals()
    } catch {
      return
    }
  }

  const reconcileRollback = async (): Promise<void> => {
    if (disposed || !historyLoaded) return
    if (reconcileInFlight) {
      reconcileQueued = true
      return
    }
    reconcileInFlight = true
    const snapshot = state.items
    try {
      const page = await services.appServer.request('thread/turns/list', {
        threadId,
        sortDirection: 'descending',
        limit: TURN_PAGE_LIMIT
      })
      if (disposed) return
      const recent = (page.data ?? []).map((turn) => ({ id: turn.id, startedAt: turn.startedAt }))
      const removed = rolledBackTurns(snapshot, recent, !page.nextCursor)
      if (removed.size > 0) publish({ items: state.items.filter((item) => !removed.has(item.turnId)) })
    } catch {
      return
    } finally {
      reconcileInFlight = false
      if (reconcileQueued && !disposed) {
        reconcileQueued = false
        void reconcileRollback()
      }
    }
  }

  const stopSubAgents = services.subagents.onChange(threadId, (subagents) => publish({ subagents }))

  const stopItems = services.appServer.onNotification('item/completed', (params) => {
    if (params.threadId !== threadId) return
    const item = toSummaryItem(params.item, params.turnId)
    if (!item) return
    if (historyLoaded) addItems([item])
    else pendingItems.push(item)
  })

  const stopTurnDiff = services.appServer.onNotification('turn/diff/updated', (params) => {
    if (params.threadId !== threadId) return
    const totals = diffTotals(params.diff ?? '')
    const next = new Map(state.turnDiffs)
    if (totals) next.set(params.turnId, totals)
    else next.delete(params.turnId)
    publish({ turnDiffs: next })
  })

  const stopPlan = services.appServer.onNotification('plan/updated', (params) => {
    if (params.threadId !== threadId) return
    publish({ plan: toPlan(params) })
  })

  const stopAutomations = services.appServer.onNotification('automation/updated', (params) => {
    const others = state.automations.filter((automation) => automation.id !== params.automationId)
    const automation = params.automation
    if (params.removed || !automation || automation.targetThreadId !== threadId) {
      if (others.length !== state.automations.length) publish({ automations: others })
      return
    }
    const index = state.automations.findIndex((entry) => entry.id === params.automationId)
    const next = [...others]
    next.splice(index < 0 ? next.length : index, 0, toAutomation(automation))
    publish({ automations: next })
  })

  const stopWorkflows = services.appServer.onNotification('workflow/run/updated', (params) => {
    if (params.threadId === threadId) void refreshWorkflow(params.runId)
  })

  const stopTurnStarted = services.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId === threadId) void reconcileRollback()
  })

  const stopRuntime = services.appServer.onNotification('thread/status/changed', (params) => {
    if (params.threadId !== threadId) return
    runtimeReceived = true
    const nextTurnId = params.runtime?.activeTurnId ?? null
    if (nextTurnId !== activeTurnId) {
      activeTurnId = nextTurnId
      publishTerminals()
    }
    void reconcileRollback()
  })

  const onTerminal = (ended: boolean) => (params: { terminal?: TerminalSnapshot }): void => {
    if (trackTerminal(params.terminal, ended)) publishTerminals()
  }
  const stopTerminals = [
    services.appServer.onNotification('terminal/started', onTerminal(false)),
    services.appServer.onNotification('terminal/completed', onTerminal(true)),
    services.appServer.onNotification('terminal/cleaned', onTerminal(true))
  ]

  onChange(state)
  for (const load of [loadHistory, loadAutomations, loadThread, loadTerminals, loadWorkflows]) void load().finally(settleLoad)

  return () => {
    disposed = true
    stopSubAgents()
    stopItems()
    stopTurnDiff()
    stopPlan()
    stopAutomations()
    stopWorkflows()
    stopTurnStarted()
    stopRuntime()
    for (const stop of stopTerminals) stop()
  }
}

function toAutomation(value: {
  id: string
  name: string
  nextRunAt?: string | null
  status: string
}): SummaryAutomation {
  return {
    id: value.id,
    name: value.name,
    nextRunAt: value.nextRunAt ?? null,
    paused: value.status === 'paused'
  }
}

function toPlan(value: { title?: unknown; todos?: unknown } | null | undefined): SummaryPlan | null {
  if (!value) return null
  const title = typeof value.title === 'string' ? value.title.trim() : ''
  const todos = (Array.isArray(value.todos) ? value.todos : []).filter((todo: { content?: unknown; status?: unknown }) =>
    typeof todo?.content === 'string' && todo.content.trim() !== '' && todo.status !== 'cancelled')
  if (!title && todos.length === 0) return null
  return { title, done: todos.filter((todo: { status?: unknown }) => todo.status === 'completed').length, total: todos.length }
}
