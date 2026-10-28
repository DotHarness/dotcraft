import type { DesktopPluginSubAgent } from '@dotcraft/plugin'
import { diffTotals, rolledBackTurns, toSummaryItem, type ChangeTotals, type SummaryItem } from './sources'
import type { SummaryServices } from './summaryServices'
import { createTerminalFeed, type SummaryTerminal } from './terminalFeed'

export type { SummaryTerminal } from './terminalFeed'

export interface SummaryFeed {
  stop(): void
  refreshTerminals(): Promise<void>
  stopTerminals(sessionIds: readonly string[]): Promise<void>
}

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

interface WorkflowRunSnapshot {
  readonly runId: string
  readonly threadId: string
  readonly name: string
  readonly status: string
  readonly createdAt?: string
  readonly startedAt?: string
}

const ITEM_PAGE_LIMIT = 100
const TURN_PAGE_LIMIT = 100
const WORKFLOW_PAGE_LIMIT = 50

export function startSummaryFeed(
  services: SummaryServices,
  threadId: string,
  onChange: (state: SummaryState) => void
): SummaryFeed {
  let disposed = false
  let historyLoaded = false
  const pendingItems: SummaryItem[] = []
  let reconcileInFlight = false
  let reconcileQueued = false
  let pendingLoads = 5
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

  const terminals = createTerminalFeed(services, threadId, (terminals) => publish({ terminals }))

  const addItems = (incoming: readonly SummaryItem[]): void => {
    if (incoming.length === 0) return
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
    terminals.addItems(incoming)
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
          terminals.observeCommandExecution(entry.item, entry.turnId)
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
      if (!runtimeReceived) terminals.setActiveTurn(result.thread?.runtime?.activeTurnId ?? null)
      publish({ plan: toPlan(result.thread?.plan) })
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
    terminals.observeCommandExecution(params.item, params.turnId)
    const item = toSummaryItem(params.item, params.turnId)
    if (!item) return
    if (historyLoaded) addItems([item])
    else {
      terminals.addItems([item])
      pendingItems.push(item)
    }
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
    terminals.setActiveTurn(params.runtime?.activeTurnId ?? null)
    void reconcileRollback()
  })

  onChange(state)
  for (const load of [loadHistory, loadAutomations, loadThread, terminals.refresh, loadWorkflows]) void load().finally(settleLoad)

  return {
    refreshTerminals: terminals.refresh,
    stopTerminals: terminals.stopMany,
    stop() {
      disposed = true
      stopSubAgents()
      stopItems()
      stopTurnDiff()
      stopPlan()
      stopAutomations()
      stopWorkflows()
      stopTurnStarted()
      stopRuntime()
      terminals.dispose()
    }
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
