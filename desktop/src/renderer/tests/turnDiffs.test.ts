import { describe, expect, it } from 'vitest'
import {
  applyLiveTurnDiff,
  deriveItemDiffs,
  foldHistoryTurnDiffs,
  latestTurnDiff,
  setTurnFileStatus,
  threadFileSummaries,
  turnPatchTotals,
  turnWrittenFiles
} from '../stores/turnDiffs'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { TurnDiff } from '../types/turnDiff'
import { parseUnifiedDiff } from '../utils/unifiedDiff'

const createdAt = '2026-09-23T00:00:00Z'

const patch = (...lines: string[]): string => lines.map((line) => `${line}\n`).join('')

const updateDiff = (path: string, before: string, after: string): string =>
  patch(`diff --git a/${path} b/${path}`, 'index 1111111..2222222', `--- a/${path}`, `+++ b/${path}`, '@@ -1 +1 @@', `-${before}`, `+${after}`)

const addDiff = (path: string): string =>
  patch(`diff --git a/${path} b/${path}`, 'new file mode 100644', 'index 0000000..2222222', '--- /dev/null', `+++ b/${path}`, '@@ -0,0 +1,2 @@', '+one', '+two')

function fileChange(path: string, diff: string, additions = 1, deletions = 1, kind: 'add' | 'update' = 'update') {
  return { kind: 'fileChange', changes: [{ path, kind, diff, additions, deletions }] }
}

function edit(id: string, structuredResult?: unknown, toolName = 'EditFile'): ConversationItem {
  return { id, type: 'toolCall', status: 'completed', toolName, toolCallId: `call-${id}`, createdAt, structuredResult }
}

function editResult(id: string, structuredResult: unknown): ConversationItem {
  return { id: `${id}-result`, type: 'toolResult', status: 'completed', toolName: 'EditFile', toolCallId: `call-${id}`, createdAt, structuredResult }
}

function turn(id: string, items: ConversationItem[]): ConversationTurn {
  return { id, threadId: 'thread-1', status: 'completed', items, startedAt: createdAt }
}

function fold(turns: ConversationTurn[], previous: ReadonlyMap<string, TurnDiff> = new Map()) {
  return foldHistoryTurnDiffs(turns, deriveItemDiffs(turns, new Map()), previous)
}

const twoEditsOfA = turn('t1', [
  edit('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))),
  edit('b', fileChange('src/a.ts', updateDiff('src/a.ts', 'y', 'z'))),
  edit('c', fileChange('src/b.ts', addDiff('src/b.ts'), 2, 0, 'add'), 'WriteFile')
])

describe('deriveItemDiffs', () => {
  it('reads the file change from the tool call or its sibling result', () => {
    const turns = [
      turn('t1', [
        edit('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))),
        edit('b'),
        editResult('b', fileChange('src/b.ts', updateDiff('src/b.ts', 'x', 'y')))
      ])
    ]
    const diffs = deriveItemDiffs(turns, new Map())

    expect([...diffs.keys()]).toEqual(['a', 'b'])
    expect(diffs.get('a')).toMatchObject({ filePath: 'src/a.ts', additions: 1, deletions: 1 })
    expect(diffs.get('b')?.filePath).toBe('src/b.ts')
    expect(diffs.get('b')?.diffHunks).toHaveLength(1)
  })

  it('reuses previous entries by identity', () => {
    const turns = [twoEditsOfA]
    const first = deriveItemDiffs(turns, new Map())
    const second = deriveItemDiffs(turns, first)

    expect(second.get('a')).toBe(first.get('a'))
    expect(second.get('c')).toBe(first.get('c'))
  })

  it('ignores calls without a file change', () => {
    const turns = [
      turn('t1', [
        edit('a'),
        edit('b', { kind: 'other' }),
        edit('c', fileChange('src/c.ts', updateDiff('src/c.ts', 'x', 'y')), 'ReadFile'),
        editResult('d', fileChange('src/d.ts', updateDiff('src/d.ts', 'x', 'y')))
      ])
    ]

    expect(deriveItemDiffs(turns, new Map()).size).toBe(0)
  })
})

describe('foldHistoryTurnDiffs', () => {
  it('produces one row per file-changing call in call order', () => {
    const entry = fold([twoEditsOfA]).get('t1')

    expect(entry?.source).toBe('history')
    expect(entry?.files.map((row) => row.key)).toEqual(['t1::a', 't1::b', 't1::c'])
    expect(entry?.files.map((row) => row.diff.filePath)).toEqual(['src/a.ts', 'src/a.ts', 'src/b.ts'])
    expect(entry?.files[2].diff.isNewFile).toBe(true)
  })

  it('keeps live entries and carries row status by key', () => {
    const live: TurnDiff = { turnId: 't2', source: 'live', files: [] }
    const turns = [twoEditsOfA, turn('t2', [edit('d', fileChange('src/d.ts', updateDiff('src/d.ts', 'x', 'y')))])]
    const reverted = setTurnFileStatus(fold(turns), 't1', 't1::a', 'reverted')
    const grown = [
      turn('t1', [...twoEditsOfA.items, edit('e', fileChange('src/e.ts', updateDiff('src/e.ts', 'x', 'y')))]),
      turns[1]
    ]
    const next = fold(grown, new Map(reverted).set('t2', live))

    expect(next.get('t2')).toBe(live)
    expect(next.get('t1')?.files.map((row) => [row.key, row.diff.status])).toEqual([
      ['t1::a', 'reverted'],
      ['t1::b', 'written'],
      ['t1::c', 'written'],
      ['t1::e', 'written']
    ])
  })

  it('omits turns without file changes and keeps entries for turns outside the fold', () => {
    const older: TurnDiff = { turnId: 't0', source: 'history', files: [] }
    const turns = [
      turn('t1', [{ id: 'm', type: 'agentMessage', status: 'completed', text: 'done', createdAt }]),
      turn('t2', [edit('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y')))])
    ]
    const next = fold(turns, new Map([['t0', older]]))

    expect([...next.keys()]).toEqual(['t0', 't2'])
    expect(next.get('t0')).toBe(older)
  })

  it('keeps a no-op write as an untruncated empty row', () => {
    const noOp = { kind: 'fileChange', changes: [{ path: 'src/same.ts', kind: 'update', additions: 0, deletions: 0 }] }
    const row = fold([turn('t1', [edit('a', noOp, 'WriteFile')])]).get('t1')?.files[0]

    expect(row).toMatchObject({ key: 't1::a', truncated: false, patchText: '' })
    expect(row?.diff).toMatchObject({ filePath: 'src/same.ts', additions: 0, deletions: 0, diffHunks: [] })
  })

  it('returns the previous entry when nothing changed', () => {
    const turns = [twoEditsOfA]
    const itemDiffs = deriveItemDiffs(turns, new Map())
    const first = foldHistoryTurnDiffs(turns, itemDiffs, new Map())
    const second = foldHistoryTurnDiffs(turns, deriveItemDiffs(turns, itemDiffs), first)

    expect(second.get('t1')).toBe(first.get('t1'))
  })
})

describe('applyLiveTurnDiff', () => {
  const turns = [twoEditsOfA]
  const itemDiffs = deriveItemDiffs(turns, new Map())
  const history = foldHistoryTurnDiffs(turns, itemDiffs, new Map())

  it('replaces the turn with one live row per file', () => {
    const aggregate = updateDiff('src/a.ts', 'x', 'z') + addDiff('src/b.ts')
    const next = applyLiveTurnDiff(history, turns, itemDiffs, 't1', parseUnifiedDiff(aggregate))
    const entry = next.get('t1')

    expect(entry?.source).toBe('live')
    expect(entry?.files.map((row) => [row.key, row.truncated])).toEqual([
      ['t1::src/a.ts', false],
      ['t1::src/b.ts', false]
    ])
  })

  it('replaces an earlier snapshot and keeps row status by path', () => {
    const first = applyLiveTurnDiff(history, turns, itemDiffs, 't1', parseUnifiedDiff(updateDiff('src/a.ts', 'x', 'z') + addDiff('src/b.ts')))
    const reverted = setTurnFileStatus(first, 't1', 't1::src/b.ts', 'reverted')
    const second = applyLiveTurnDiff(reverted, turns, itemDiffs, 't1', parseUnifiedDiff(addDiff('src/b.ts')))

    expect(second.get('t1')?.files.map((row) => [row.key, row.diff.status])).toEqual([['t1::src/b.ts', 'reverted']])
  })

  it('falls back to per-call rows when the aggregate is unavailable', () => {
    const live = applyLiveTurnDiff(history, turns, itemDiffs, 't1', parseUnifiedDiff(addDiff('src/b.ts')))
    const next = applyLiveTurnDiff(live, turns, itemDiffs, 't1', null)

    expect(next.get('t1')?.source).toBe('history')
    expect(next.get('t1')?.files.map((row) => row.key)).toEqual(['t1::a', 't1::b', 't1::c'])
  })
})

describe('setTurnFileStatus', () => {
  it('returns the same map when nothing changes', () => {
    const map = fold([twoEditsOfA])

    expect(setTurnFileStatus(map, 't1', 't1::a', 'written')).toBe(map)
    expect(setTurnFileStatus(map, 't1', 't1::missing', 'reverted')).toBe(map)
    expect(setTurnFileStatus(map, 'missing', 't1::a', 'reverted')).toBe(map)
  })

  it('changes only the addressed row', () => {
    const map = fold([twoEditsOfA])
    const next = setTurnFileStatus(map, 't1', 't1::b', 'reverted')
    const before = map.get('t1')!.files
    const after = next.get('t1')!.files

    expect(after.map((row) => row.diff.status)).toEqual(['written', 'reverted', 'written'])
    expect(after[0]).toBe(before[0])
    expect(before[1].diff.status).toBe('written')
  })
})

describe('selectors', () => {
  const turns = [twoEditsOfA, turn('t2', [edit('d', fileChange('src/a.ts', updateDiff('src/a.ts', 'z', 'w')))])]
  const map = setTurnFileStatus(fold(turns), 't2', 't2::d', 'reverted')

  it('picks the newest turn that changed files', () => {
    const later = [...turns, turn('t3', [])]

    expect(latestTurnDiff(map, later)?.turnId).toBe('t2')
    expect(latestTurnDiff(new Map(), later)).toBeUndefined()
  })

  it('summarizes each path once with summed counts and the latest status', () => {
    expect(threadFileSummaries(map)).toEqual([
      { filePath: 'src/a.ts', additions: 3, deletions: 3, status: 'reverted' },
      { filePath: 'src/b.ts', additions: 2, deletions: 0, status: 'written' }
    ])
  })

  it('totals a turn', () => {
    expect(turnPatchTotals(map.get('t1')!.files)).toEqual({ additions: 4, deletions: 2, files: 2 })
    expect(turnPatchTotals([])).toEqual({ additions: 0, deletions: 0, files: 0 })
  })

  it('lists the written files of a turn once per path', () => {
    const next = setTurnFileStatus(map, 't1', 't1::c', 'reverted')

    expect(turnWrittenFiles(next, 't1').map((row) => row.key)).toEqual(['t1::b'])
    expect(turnWrittenFiles(next, 't2')).toEqual([])
  })
})
