import type { TaskWake, TranscriptEntry } from './transcriptTypes'
import type { TurnChanges } from './turnChanges'

type ActivityEntry = Extract<TranscriptEntry, { kind: 'activity' }>

export interface TurnPiece {
  turnId: string
  entries: TranscriptEntry[]
  wake: TaskWake | null
  /** The ending row that names a subagent wake in place of the generic line. */
  wakeEntry: TranscriptEntry | null
  /** Completed without a reply, so a notification may continue it. */
  open: boolean
  replied: boolean
  changes: TurnChanges | null
}

function continues(previous: TurnPiece | undefined, piece: TurnPiece): boolean {
  return !!previous?.open && piece.wake !== null && !piece.entries.some((entry) => entry.kind === 'user' && !entry.added)
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
  return piece.wakeEntry ? [piece.wakeEntry] : piece.wake ? [{ kind: 'wake', id: `wake-${piece.turnId}`, origin: piece.wake }] : []
}

function work(piece: TurnPiece, first: boolean, lifted: boolean): TranscriptEntry[] {
  const { activity, rest } = split(piece)
  const body = [...(activity?.children ?? []), ...rest]
  return [
    ...(first ? [] : wakeLine(piece)),
    ...(lifted
      ? body.flatMap((entry): TranscriptEntry[] => entry.kind === 'changes' ? [] : entry.kind === 'assistant' && entry.changes.length > 0 ? [{ ...entry, changes: [] }] : [entry])
      : body),
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
  const earlier = leading.flatMap((piece, index) => work(piece, index === 0, folds))
  if (!tail.activity) return [...head.opening, ...earlier, ...wakeLine(last), ...tail.rest]
  const started = Date.parse(tail.activity.startedAt ?? '')
  const before = leading.reduce((sum, piece) => sum + elapsed(split(piece).activity), 0)
  const activity: ActivityEntry = {
    ...tail.activity,
    startedAt: Number.isFinite(started) && before > 0 ? new Date(started - before).toISOString() : tail.activity.startedAt,
    origin: head.activity?.origin ?? null,
  }
  if (!folds) return [...head.opening, activity, ...earlier, ...wakeLine(last), ...tail.rest]
  const earlierChanges = leading.flatMap((piece) => piece.changes ? [piece.changes] : [])
  const reply = last.replied ? tail.rest.findLastIndex((entry) => entry.kind === 'assistant' && (entry.copy || entry.phase === 'final')) : -1
  const rest = reply < 0 || earlierChanges.length === 0 ? tail.rest : tail.rest.map((entry, index) =>
    index === reply && entry.kind === 'assistant' ? { ...entry, changes: [...earlierChanges, ...entry.changes] } : entry)
  const working = rest.findIndex((entry) => entry.kind === 'thinking')
  const end = working < 0 ? rest.length : working
  return [
    ...head.opening,
    { ...activity, children: [...earlier, ...wakeLine(last), ...tail.activity.children] },
    ...rest.slice(0, end),
    ...(reply < 0 ? earlierChanges.map((changes): TranscriptEntry => ({ kind: 'changes', id: `changes-${changes.turnId}`, changes })) : []),
    ...rest.slice(end),
  ]
}

export function joinResponses(pieces: readonly TurnPiece[]): TranscriptEntry[] {
  const output: TranscriptEntry[] = []
  let run: TurnPiece[] = []
  let previous: TurnPiece | undefined
  for (const piece of pieces) {
    if (!continues(previous, piece)) {
      output.push(...joinRun(run))
      run = []
    }
    previous = piece
    if (piece.entries.length > 0) run.push(piece)
  }
  if (run.length > 0) output.push(...joinRun(run))
  return output
}
