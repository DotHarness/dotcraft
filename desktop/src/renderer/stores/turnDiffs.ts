import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import type { FileChangeEntry, ThreadFileSummary, TurnDiff, TurnFileChange } from '../types/turnDiff'
import {
  fileChangeEntryToTurnFileChange,
  isFileChangeTruncated,
  parseFileChangeStructuredContent,
  type ParsedFilePatch
} from '../utils/unifiedDiff'

const FILE_CHANGE_TOOLS = new Set(['WriteFile', 'EditFile'])

interface FileChangeCall {
  item: ConversationItem
  change: FileChangeEntry
}

function fileChangeCalls(turn: ConversationTurn): FileChangeCall[] {
  let results: Map<string, ConversationItem> | undefined
  const calls: FileChangeCall[] = []
  for (const item of turn.items) {
    if (item.type !== 'toolCall' || !FILE_CHANGE_TOOLS.has(item.toolName ?? '')) continue
    let content = parseFileChangeStructuredContent(item.structuredResult)
    if (!content && item.toolCallId) {
      results ??= new Map(
        turn.items
          .filter((candidate) => candidate.type === 'toolResult' && candidate.toolCallId)
          .map((result) => [result.toolCallId!, result] as const)
      )
      content = parseFileChangeStructuredContent(results.get(item.toolCallId)?.structuredResult)
    }
    const change = content?.changes[0]
    if (change) calls.push({ item, change })
  }
  return calls
}

export function deriveItemDiffs(
  turns: readonly ConversationTurn[],
  previous: ReadonlyMap<string, FileDiff>,
  workspacePath?: string
): Map<string, FileDiff> {
  const next = new Map<string, FileDiff>()
  for (const turn of turns) {
    for (const { item, change } of fileChangeCalls(turn)) {
      next.set(
        item.id,
        previous.get(item.id) ?? fileChangeEntryToTurnFileChange(change, turn.id, item.id, workspacePath).diff
      )
    }
  }
  return next
}

function withStatusOf(row: TurnFileChange, prior: TurnFileChange | undefined): TurnFileChange {
  if (!prior || prior.diff.status === row.diff.status) return row
  return { ...row, diff: { ...row.diff, status: prior.diff.status } }
}

function historyRow(
  turnId: string,
  { item, change }: FileChangeCall,
  known: FileDiff | undefined,
  prior: TurnFileChange | undefined,
  workspacePath?: string
): TurnFileChange {
  if (!known) return withStatusOf(fileChangeEntryToTurnFileChange(change, turnId, item.id, workspacePath), prior)
  const patchText = change.diff ?? ''
  if (prior && prior.patchText === patchText && prior.diff.diffHunks === known.diffHunks) return prior
  const row: TurnFileChange = {
    key: `${turnId}::${item.id}`,
    turnId,
    diff: known,
    patchText,
    truncated: isFileChangeTruncated(change, known)
  }
  return withStatusOf(row, prior)
}

function foldTurn(
  turn: ConversationTurn,
  itemDiffs: ReadonlyMap<string, FileDiff>,
  prior: TurnDiff | undefined,
  workspacePath?: string
): TurnDiff | undefined {
  const priorRows = new Map(prior?.files.map((row) => [row.key, row] as const))
  const files = fileChangeCalls(turn).map((call) =>
    historyRow(turn.id, call, itemDiffs.get(call.item.id), priorRows.get(`${turn.id}::${call.item.id}`), workspacePath)
  )
  if (files.length === 0) return undefined
  if (prior && prior.files.length === files.length && files.every((row, index) => row === prior.files[index])) {
    return prior
  }
  return { turnId: turn.id, source: 'history', files }
}

/** Keeps live entries and lists `previous` entries for turns outside `turns` first, so map order stays chronological. */
export function foldHistoryTurnDiffs(
  turns: readonly ConversationTurn[],
  itemDiffs: ReadonlyMap<string, FileDiff>,
  previous: ReadonlyMap<string, TurnDiff>,
  workspacePath?: string
): Map<string, TurnDiff> {
  const turnIds = new Set(turns.map((turn) => turn.id))
  const next = new Map([...previous].filter(([turnId]) => !turnIds.has(turnId)))
  for (const turn of turns) {
    const prior = previous.get(turn.id)
    const entry = prior?.source === 'live' ? prior : foldTurn(turn, itemDiffs, prior, workspacePath)
    if (entry) next.set(turn.id, entry)
  }
  return next
}

export function refreshTurnFileChanges(
  turns: readonly ConversationTurn[],
  turnId: string,
  itemDiffs: Map<string, FileDiff>,
  turnDiffs: ReadonlyMap<string, TurnDiff>,
  workspacePath?: string
): { itemDiffs: Map<string, FileDiff>; turnDiffs: ReadonlyMap<string, TurnDiff> } {
  const turn = turns.find((candidate) => candidate.id === turnId)
  if (!turn) return { itemDiffs, turnDiffs }
  const added = [...deriveItemDiffs([turn], itemDiffs, workspacePath)].filter(([itemId]) => !itemDiffs.has(itemId))
  if (added.length === 0) return { itemDiffs, turnDiffs }
  const nextItemDiffs = new Map([...itemDiffs, ...added])
  return { itemDiffs: nextItemDiffs, turnDiffs: foldHistoryTurnDiffs([turn], nextItemDiffs, turnDiffs, workspacePath) }
}

export function applyLiveTurnDiff(
  previous: ReadonlyMap<string, TurnDiff>,
  turns: readonly ConversationTurn[],
  itemDiffs: ReadonlyMap<string, FileDiff>,
  turnId: string,
  parsed: ParsedFilePatch[] | null,
  workspacePath?: string
): Map<string, TurnDiff> {
  const prior = previous.get(turnId)
  if (!parsed || parsed.length === 0) {
    const withoutLive = new Map(previous)
    if (prior?.source === 'live') withoutLive.delete(turnId)
    const turn = turns.find((candidate) => candidate.id === turnId)
    return turn ? foldHistoryTurnDiffs([turn], itemDiffs, withoutLive, workspacePath) : withoutLive
  }
  const priorRows = new Map(prior?.files.map((row) => [row.key, row] as const))
  const files = parsed.map(({ diff, patchText, error }) => {
    const key = `${turnId}::${diff.filePath}`
    return withStatusOf({ key, turnId, diff, patchText, truncated: error === true }, priorRows.get(key))
  })
  return new Map(previous).set(turnId, { turnId, source: 'live', files })
}

export function setTurnFileStatus(
  previous: ReadonlyMap<string, TurnDiff>,
  turnId: string,
  key: string,
  status: FileDiff['status']
): ReadonlyMap<string, TurnDiff> {
  const turn = previous.get(turnId)
  const row = turn?.files.find((file) => file.key === key)
  if (!turn || !row || row.diff.status === status) return previous
  const files = turn.files.map((file) => (file === row ? { ...file, diff: { ...file.diff, status } } : file))
  return new Map(previous).set(turnId, { ...turn, files })
}

function turnDiffsNewestFirst(map: ReadonlyMap<string, TurnDiff>, turns: readonly ConversationTurn[]): TurnDiff[] {
  const position = new Map(turns.map((turn, index) => [turn.id, index] as const))
  return [...map.values()].sort((a, b) => (position.get(b.turnId) ?? -1) - (position.get(a.turnId) ?? -1))
}

export function latestTurnDiff(map: ReadonlyMap<string, TurnDiff>, turns: readonly ConversationTurn[]): TurnDiff | undefined {
  return turnDiffsNewestFirst(map, turns)[0]
}

const pathKey = (filePath: string): string => filePath.replace(/\\/g, '/')

/** One summary per path. Rows are read in map order, so the status comes from the path's last row in the last turn. */
export function threadFileSummaries(map: ReadonlyMap<string, TurnDiff>): ThreadFileSummary[] {
  const summaries = new Map<string, ThreadFileSummary>()
  for (const turn of map.values()) {
    for (const row of turn.files) {
      const filePath = pathKey(row.diff.filePath)
      const existing = summaries.get(filePath)
      summaries.set(filePath, {
        filePath,
        additions: (existing?.additions ?? 0) + row.diff.additions,
        deletions: (existing?.deletions ?? 0) + row.diff.deletions,
        status: row.diff.status
      })
    }
  }
  return [...summaries.values()]
}

export function writtenFileSummaries(map: ReadonlyMap<string, TurnDiff>): ThreadFileSummary[] {
  return threadFileSummaries(map).filter((file) => file.status === 'written')
}

/** Totals over every row of a turn, whatever its status; `files` counts distinct paths. */
export function turnPatchTotals(
  rows: readonly TurnFileChange[]
): { additions: number; deletions: number; files: number } {
  return {
    additions: rows.reduce((sum, row) => sum + row.diff.additions, 0),
    deletions: rows.reduce((sum, row) => sum + row.diff.deletions, 0),
    files: new Set(rows.map((row) => pathKey(row.diff.filePath))).size
  }
}

export function turnWrittenFiles(map: ReadonlyMap<string, TurnDiff>, turnId: string): TurnFileChange[] {
  const byPath = new Map<string, TurnFileChange>()
  for (const row of map.get(turnId)?.files ?? []) {
    if (row.diff.status !== 'written') continue
    byPath.set(pathKey(row.diff.filePath), row)
  }
  return [...byPath.values()]
}
