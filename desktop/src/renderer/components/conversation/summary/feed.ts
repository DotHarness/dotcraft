import type { DesktopPluginSubAgent } from '@dotcraft/plugin'
import { readHistoryPreview, THREAD_OPENING_PAGE } from '../../../utils/threadHistory'
import { diffTotals, rolledBackTurns, toSummaryItem, type FileChanges, type SummaryItem } from './sources'
import type { SummaryServices } from './summaryServices'
import type { SummaryTerminal } from './terminalFeed'
import { retainThreadTaskFeed } from '../../../stores/threadTaskFeed'

export type { SummaryTerminal } from './terminalFeed'

export interface SummaryFeed {
  stop(): void
  refreshTerminals(): Promise<void>
  stopWorkflow(runId: string): Promise<void>
  stopTerminal(sessionId: string): Promise<void>
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
  readonly turnDiffs: ReadonlyMap<string, FileChanges>
  readonly terminals: readonly SummaryTerminal[]
}

const TURN_PAGE_LIMIT = 100

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
  let pendingLoads = 4
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

  const tasks = retainThreadTaskFeed(services.appServer, threadId)
  const stopTasks = tasks.subscribe(values => publish(values))

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
    tasks.addItems(incoming)
  }

  const settleLoad = (): void => {
    pendingLoads -= 1
    if (pendingLoads === 0) publish({ ready: true })
  }

  const loadHistory = async (): Promise<void> => {
    const loaded: SummaryItem[] = []
    try {
      const page = await services.appServer.request('thread/turns/list', {
        threadId, sortDirection: 'descending', ...THREAD_OPENING_PAGE
      })
      if (disposed) return
      const turns = [...(page.data ?? [])].reverse()
      const itemsByTurn = await Promise.all(turns.map(async (turn) => {
        const items = turn.items ?? []
        if (turn.itemCount == null || turn.itemCount <= items.length) return items
        try {
          const recent = await readHistoryPreview(() => services.appServer.request('thread/items/list', {
            threadId, turnId: turn.id, sortDirection: 'descending', limit: 100
          }), () => !disposed)
          const hydrated = [...(recent.data ?? [])].reverse().map((entry) => entry.item)
          const hydratedIds = new Set(hydrated.map(item => item.id))
          return [...items.filter(item => !hydratedIds.has(item.id)), ...hydrated]
        } catch {
          return items
        }
      }))
      if (disposed) return
      for (const [index, turn] of turns.entries()) {
        for (const raw of itemsByTurn[index]) {
          tasks.observeCommandExecution(raw, turn.id)
          const item = toSummaryItem(raw, turn.id)
          if (item) loaded.push(item)
        }
      }
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
    tasks.observeCommandExecution(params.item, params.turnId)
    const item = toSummaryItem(params.item, params.turnId)
    if (!item) return
    if (historyLoaded) addItems([item])
    else {
      tasks.addItems([item])
      pendingItems.push(item)
    }
  })

  const stopTurnDiff = services.appServer.onNotification('turn/diff/updated', (params) => {
    if (params.threadId !== threadId) return
    const files = diffTotals(params.diff ?? '')
    const next = new Map(state.turnDiffs)
    if (files.size > 0) next.set(params.turnId, files)
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

  const stopTurnStarted = services.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId === threadId) void reconcileRollback()
  })

  const stopRuntime = services.appServer.onNotification('thread/status/changed', (params) => {
    if (params.threadId !== threadId) return
    if (params.runtime?.running === false) void tasks.refresh()
    void reconcileRollback()
  })

  onChange(state)
  for (const load of [loadHistory, loadAutomations, loadThread, tasks.ensure]) void load().finally(settleLoad)

  return {
    refreshTerminals: async () => { await tasks.refresh() },
    stopWorkflow: tasks.stopWorkflow,
    stopTerminal: tasks.stopTerminal,
    stop() {
      disposed = true
      stopSubAgents()
      stopItems()
      stopTurnDiff()
      stopPlan()
      stopAutomations()
      stopTasks()
      stopTurnStarted()
      stopRuntime()
      tasks.release()
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
