import type { DesktopPluginHost, DesktopPluginSubAgent } from '@dotcraft/plugin'
import { rolledBackTurns, toSummaryItem, type SummaryItem } from './sources'

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

export interface SummaryState {
  readonly subagents: readonly DesktopPluginSubAgent[]
  readonly automations: readonly SummaryAutomation[]
  readonly plan: SummaryPlan | null
  readonly items: readonly SummaryItem[]
}

const ITEM_PAGE_LIMIT = 500
const TURN_PAGE_LIMIT = 100

export function startSummaryFeed(
  host: DesktopPluginHost,
  threadId: string,
  onChange: (state: SummaryState) => void
): () => void {
  let disposed = false
  let historyLoaded = false
  const pendingItems: SummaryItem[] = []
  let reconcileInFlight = false
  let reconcileQueued = false
  let state: SummaryState = {
    subagents: host.subagents.list(threadId),
    automations: [],
    plan: null,
    items: []
  }

  const publish = (patch: Partial<SummaryState>): void => {
    if (disposed) return
    state = { ...state, ...patch }
    onChange(state)
  }

  const addItems = (incoming: readonly SummaryItem[]): void => {
    if (incoming.length === 0) return
    const next = [...state.items]
    const positions = new Map(next.map((item, index) => [item.id, index]))
    for (const item of incoming) {
      const position = positions.get(item.id)
      if (position === undefined) {
        positions.set(item.id, next.length)
        next.push(item)
      } else {
        next[position] = item
      }
    }
    publish({ items: next })
  }

  const readItemsPage = (cursor: string | null) => host.appServer.request('thread/items/list', {
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
      const result = await host.appServer.request('automation/list', {})
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

  const loadPlan = async (): Promise<void> => {
    try {
      const result = await host.appServer.request('thread/read', { threadId })
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
      const page = await host.appServer.request('thread/turns/list', {
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

  const stopSubAgents = host.subagents.onChange(threadId, (subagents) => publish({ subagents }))

  const stopItems = host.appServer.onNotification('item/completed', (params) => {
    if (params.threadId !== threadId) return
    const item = toSummaryItem(params.item, params.turnId)
    if (!item) return
    if (historyLoaded) addItems([item])
    else pendingItems.push(item)
  })

  const stopPlan = host.appServer.onNotification('plan/updated', (params) => {
    if (params.threadId !== threadId) return
    publish({ plan: toPlan(params) })
  })

  const stopAutomations = host.appServer.onNotification('automation/updated', (params) => {
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

  const stopTurnStarted = host.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId === threadId) void reconcileRollback()
  })

  const stopRuntime = host.appServer.onNotification('thread/runtimeChanged', (params) => {
    if (params.threadId === threadId) void reconcileRollback()
  })

  onChange(state)
  void loadHistory()
  void loadAutomations()
  void loadPlan()

  return () => {
    disposed = true
    stopSubAgents()
    stopItems()
    stopPlan()
    stopAutomations()
    stopTurnStarted()
    stopRuntime()
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
