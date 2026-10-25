import type { HistoryTurn } from './history'
import type { ToolGroupLabel } from './toolGroups'
import { groupTools } from './toolGroups'
import type { TaskWake, TranscriptEntry } from './transcriptTypes'
import type { TurnChanges } from './turnChanges'

function sameLabel(left: ToolGroupLabel, right: ToolGroupLabel): boolean {
  return left.length === right.length && left.every((part, index) => part.part === right[index].part && part.many === right[index].many)
}

export function sameReferences<T>(left: readonly T[], right: readonly T[]): boolean {
  return left === right || (left.length === right.length && left.every((entry, index) => entry === right[index]))
}

function sameFields(left: TranscriptEntry, right: TranscriptEntry): boolean {
  const keys = Object.keys(left) as (keyof TranscriptEntry)[]
  return keys.length === Object.keys(right).length && keys.every((key) => Object.is(left[key], right[key]))
}

export function stabilizeEntries(next: TranscriptEntry[], previous: TranscriptEntry[]): TranscriptEntry[] {
  const byId = new Map(previous.map((entry) => [entry.id, entry]))
  const stable = next.map((entry): TranscriptEntry => {
    const old = byId.get(entry.id)
    if (!old || old.kind !== entry.kind || entry === old) return entry
    if (entry.kind === 'toolGroup' && old.kind === 'toolGroup') {
      entry = {
        ...entry,
        children: sameReferences(entry.children, old.children) ? old.children : entry.children,
        label: sameLabel(entry.label, old.label) ? old.label : entry.label,
      }
    } else if (entry.kind === 'activity' && old.kind === 'activity') {
      entry = { ...entry, children: stabilizeEntries(entry.children, old.children) }
    }
    return sameFields(entry, old) ? old : entry
  })
  return sameReferences(stable, previous) ? previous : stable
}

function lastIndex(entries: TranscriptEntry[], test: (entry: TranscriptEntry) => boolean): number {
  for (let index = entries.length - 1; index >= 0; index -= 1) if (test(entries[index])) return index
  return -1
}

export function showsWork(entry: TranscriptEntry): boolean {
  switch (entry.kind) {
    case 'tool':
      return entry.live
    case 'toolGroup':
      return entry.children.some((child) => child.live)
    case 'image':
      return entry.status === 'inProgress'
    case 'assistant':
      return entry.streaming
    default:
      return false
  }
}

export function layoutTurn(
  turnId: string,
  turn: HistoryTurn | undefined,
  entries: TranscriptEntry[],
  running: boolean,
  changes: TurnChanges | null,
  origin: TaskWake | null = null,
  heading: string | null = null,
  stoppedByYou = false,
): TranscriptEntry[] {
  let start = 0
  while (start < entries.length && entries[start].kind === 'user' && !(entries[start] as { added: boolean }).added) start += 1
  const opening = entries.slice(0, start)
  const body = groupTools(entries.slice(start), running)
  const tail: TranscriptEntry[] = running
    ? [{ kind: 'thinking', id: `thinking-${turnId}`, working: true, heading }]
    : []
  const completed = !running && turn?.status === 'completed'
  const final =
    turn?.status === 'failed'
      ? -1
      : lastIndex(body, (entry) => entry.kind === 'assistant' && (entry.phase === 'final' || (completed && entry.phase === null)))
  if (completed) {
    const footer = lastIndex(body, (entry) => entry.kind === 'assistant')
    if (footer >= 0) body[footer] = { ...(body[footer] as Extract<TranscriptEntry, { kind: 'assistant' }>), copy: true, changes }
    else if (changes) body.push({ kind: 'changes', id: `changes-${turnId}`, changes })
  }
  if (!turn) return [...opening, ...body, ...tail]
  const status: Extract<TranscriptEntry, { kind: 'activity' }>['status'] | null =
    turn.status === 'cancelled' ? (stoppedByYou ? 'youStopped' : 'stopped') : final >= 0 || turn.status === 'completed' ? 'worked' : running ? 'working' : null
  if (!status) return [...opening, ...body, ...tail]
  const finalAt = final >= 0 ? (body[final] as { at: string }).at || null : null
  const activity = {
    kind: 'activity' as const,
    id: `activity-${turnId}`,
    status,
    startedAt: turn.startedAt ?? null,
    endedAt: status === 'worked' ? (finalAt ?? turn.completedAt ?? null) : (turn.completedAt ?? null),
    origin,
  }
  if (status !== 'worked' || final <= 0) return [...opening, { ...activity, children: [] }, ...body, ...tail]
  const before = body.slice(0, final)
  const plan = lastIndex(before, (entry) => entry.kind === 'plan')
  const image = lastIndex(before, (entry) => entry.kind === 'image' && entry.status === 'completed' && entry.uri !== null)
  const pinned = (entry: TranscriptEntry, index: number) =>
    (entry.kind === 'user' && entry.added) || entry.kind === 'divider' || entry.kind === 'threadAction' || index === plan || index === image
  return [
    ...opening,
    { ...activity, children: before.filter((entry, index) => !pinned(entry, index)) },
    ...before.filter(pinned),
    ...body.slice(final),
    ...tail,
  ]
}
