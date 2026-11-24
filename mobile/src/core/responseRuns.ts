import { withTodoCard, type TodoCard } from './todoCard'
import type { TaskWake, TranscriptEntry } from './transcriptTypes'
import { responseChanges, type TurnChanges } from './turnChanges'

type ActivityEntry = Extract<TranscriptEntry, { kind: 'activity' }>

export interface TurnPiece {
  turnId: string
  entries: TranscriptEntry[]
  wake: TaskWake | null
  /** The ending row that names a subagent wake in place of the generic line. */
  wakeEntry: TranscriptEntry | null
  completed: boolean
  replied: boolean
  changes: TurnChanges | null
}

function continues(previous: TurnPiece | undefined, piece: TurnPiece): boolean {
  return !!previous?.completed && piece.wake !== null && !piece.entries.some((entry) => entry.kind === 'user' && !entry.added)
}

function elapsed(activity: ActivityEntry | undefined): number {
  const value = Date.parse(activity?.endedAt ?? '') - Date.parse(activity?.startedAt ?? '')
  return Number.isFinite(value) && value > 0 ? value : 0
}

function split(piece: TurnPiece): { opening: TranscriptEntry[]; activity: ActivityEntry | undefined; rest: TranscriptEntry[] } {
  const index = piece.entries.findIndex((entry) => entry.kind === 'activity')
  if (index < 0) return { opening: [], activity: undefined, rest: piece.entries }
  return { opening: piece.entries.slice(0, index), activity: piece.entries[index] as ActivityEntry, rest: piece.entries.slice(index + 1) }
}

function wakeLine(piece: TurnPiece): TranscriptEntry[] {
  return piece.wakeEntry ? [piece.wakeEntry] : piece.wake && !piece.wake.message ? [{ kind: 'wake', id: `wake-${piece.turnId}`, origin: piece.wake }] : []
}

function work(piece: TurnPiece, first: boolean): TranscriptEntry[] {
  const { activity, rest } = split(piece)
  const body = [...(activity?.children ?? []), ...rest]
  return [
    ...(first ? [] : wakeLine(piece)),
    ...body.flatMap((entry): TranscriptEntry[] => {
      if (entry.kind === 'changes') return []
      if (entry.kind !== 'assistant') return [entry]
      return [{ ...entry, copy: false, changes: [] }]
    }),
    ...(activity?.pendingTurnId !== undefined ? [{ kind: 'turnWork' as const, id: `turn-work-${piece.turnId}`, turnId: activity.pendingTurnId }] : []),
  ]
}

function joinRun(run: TurnPiece[]): TranscriptEntry[] {
  if (run.length < 2) return run[0]?.entries ?? []
  const leading = run.slice(0, -1)
  const last = run[run.length - 1]
  const head = split(leading[0])
  const tail = split(last)
  const folds = tail.activity?.status === 'worked' && (last.replied || tail.activity.pendingTurnId !== undefined)
  const earlier = leading.flatMap((piece, index) => work(piece, index === 0))
  if (!tail.activity) return [...head.opening, ...earlier, ...wakeLine(last), ...tail.rest]
  const started = Date.parse(tail.activity.startedAt ?? '')
  const before = leading.reduce((sum, piece) => sum + elapsed(split(piece).activity), 0)
  const activity: ActivityEntry = {
    ...tail.activity,
    startedAt: Number.isFinite(started) && before > 0 ? new Date(started - before).toISOString() : tail.activity.startedAt,
    origin: head.activity?.origin ?? null,
  }
  if (!folds) return [...head.opening, activity, ...earlier, ...wakeLine(last), ...tail.rest]
  return [
    ...head.opening,
    { ...activity, children: [...earlier, ...wakeLine(last), ...tail.activity.children] },
    ...tail.rest,
  ]
}

function responseCompletion(entries: TranscriptEntry[], changes: TurnChanges | null, settled: boolean): TranscriptEntry[] {
  const reply = entries.findLastIndex((entry) => entry.kind === 'assistant' && entry.copy)
  const output = entries.flatMap((entry, index): TranscriptEntry[] => {
    if (entry.kind === 'changes') return []
    if (index !== reply || entry.kind !== 'assistant') return [entry]
    return [{ ...entry, copy: settled, changes: changes ? [changes] : [] }]
  })
  if (reply < 0 && changes) output.push({ kind: 'changes', id: `changes-${changes.turnId}`, changes })
  return output
}

export function joinResponses(pieces: readonly TurnPiece[], options: {
  workspacePath: string | null
  latestTurnId: string | undefined
  hasBackgroundWork: boolean
  latestTodoCard: (run: readonly TurnPiece[]) => TodoCard | null
}): TranscriptEntry[] {
  const output: TranscriptEntry[] = []
  let run: TurnPiece[] = []
  function flush() {
    const last = run.at(-1)
    if (!last) return
    const latest = last.turnId === options.latestTurnId
    const settled = last.completed && !(latest && options.hasBackgroundWork)
    const joined = joinRun(run.filter((piece) => piece.entries.length > 0))
    const entries = latest ? withTodoCard(joined, options.latestTodoCard(run)) : joined
    if (run.length === 1 && (settled || !last.completed)) {
      output.push(...entries)
      return
    }
    const changes = settled ? responseChanges(run.flatMap((piece) => piece.changes ? [piece.changes] : []), options.workspacePath) : null
    output.push(...responseCompletion(entries, changes, settled))
  }
  for (const piece of pieces) {
    if (!continues(run.at(-1), piece)) {
      flush()
      run = []
    }
    run.push(piece)
  }
  flush()
  return output
}
