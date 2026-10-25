import type { ChatHistory, HistoryItem } from './history'
import type { ChatDetail, ComputerState, MobileState, PersistedComputer, PersistedState } from './state'

const PERSISTED_ITEMS = 200

function newestDetails(state: ComputerState): string[] {
  return Object.keys(state.details)
    .filter((key) => state.chats[key])
    .sort((left, right) => (Date.parse(state.chats[right].updatedAt ?? '') || 0) - (Date.parse(state.chats[left].updatedAt ?? '') || 0))
    .slice(0, 8)
}

function sameHistory(left: ChatHistory, right: ChatHistory): boolean {
  return left.items === right.items && left.turns === right.turns
}

function sameDetail(left: ChatDetail, right: ChatDetail): boolean {
  return left === right || (
    left.loadingOlder === right.loadingOlder &&
    left.profileName === right.profileName &&
    left.config === right.config &&
    left.workspacePath === right.workspacePath &&
    left.effectiveWorkspacePath === right.effectiveWorkspacePath &&
    left.context === right.context &&
    sameHistory(left.history, right.history)
  )
}

function sameComputer(left: ComputerState, right: ComputerState): boolean {
  if (left === right) return true
  if (left.computer !== right.computer || left.accessOff !== right.accessOff || left.syncedAt !== right.syncedAt ||
    left.projects !== right.projects || left.chats !== right.chats) return false
  if (left.details === right.details) return true
  const previous = newestDetails(left)
  const next = newestDetails(right)
  return previous.length === next.length && next.every((key, index) => key === previous[index] && sameDetail(left.details[key], right.details[key]))
}

export function samePersistedState(left: MobileState, right: MobileState): boolean {
  return left.hydrated === right.hydrated && left.selected === right.selected &&
    left.order.length === right.order.length && right.order.every((id, index) =>
      id === left.order[index] && sameComputer(left.computers[id], right.computers[id]),
    )
}

function withoutImageData(item: HistoryItem): HistoryItem {
  if (item.type === 'imageGeneration') return { ...item, payload: { ...item.payload, result: undefined, imageDropped: true } }
  const parts = item.payload.nativeInputParts
  if (item.type === 'userMessage' && Array.isArray(parts)) {
    return { ...item, payload: { ...item.payload, nativeInputParts: parts.filter((part: { type?: string }) => part.type !== 'image') } }
  }
  const contentItems = item.payload.contentItems
  if (!Array.isArray(contentItems)) return item
  return { ...item, payload: { ...item.payload, contentItems: contentItems.filter((entry: { type?: string }) => entry.type !== 'image') } }
}

/** Keeps complete Turns, even when the newest Turn alone exceeds the budget. */
function trimmed(history: ChatHistory): ChatHistory {
  const counts = new Map<string, number>()
  for (const item of history.items) counts.set(item.turnId, (counts.get(item.turnId) ?? 0) + 1)
  const kept = new Set<string>()
  let total = 0
  for (const turn of [...history.turns].reverse()) {
    total += counts.get(turn.id) ?? 0
    if (total > PERSISTED_ITEMS && kept.size > 0) break
    kept.add(turn.id)
  }
  return {
    items: history.items.filter((item) => kept.has(item.turnId)).map(withoutImageData),
    turns: history.turns.filter((turn) => kept.has(turn.id)),
    echoes: [],
  }
}

interface CachedDetail {
  source: ChatDetail
  snapshot: ChatDetail
}

interface CachedComputer {
  source: ComputerState
  snapshot: PersistedComputer
  details: Map<string, CachedDetail>
}

export class SnapshotProjector {
  private computers = new Map<string, CachedComputer>()

  project(state: MobileState): PersistedState {
    const computers: PersistedState['computers'] = {}
    const cache = new Map<string, CachedComputer>()
    for (const id of state.order) {
      const source = state.computers[id]
      const previous = this.computers.get(id)
      if (previous && sameComputer(previous.source, source)) {
        computers[id] = previous.snapshot
        cache.set(id, previous)
        continue
      }
      const details: PersistedComputer['details'] = {}
      const entries = new Map<string, CachedDetail>()
      for (const key of newestDetails(source)) {
        const detail = source.details[key]
        const old = previous?.details.get(key)
        const snapshot = old && sameDetail(old.source, detail)
          ? old.snapshot
          : {
              ...detail,
              loading: false,
              history: old && sameHistory(old.source.history, detail.history) ? old.snapshot.history : trimmed(detail.history),
            }
        details[key] = snapshot
        entries.set(key, { source: detail, snapshot })
      }
      const snapshot: PersistedComputer = {
        computer: source.computer,
        accessOff: source.accessOff,
        syncedAt: source.syncedAt,
        projects: source.projects,
        chats: source.chats,
        details,
      }
      computers[id] = snapshot
      cache.set(id, { source, snapshot, details: entries })
    }
    this.computers = cache
    return { order: state.order, selected: state.selected, computers }
  }
}

export function persistable(state: MobileState): PersistedState {
  return new SnapshotProjector().project(state)
}
