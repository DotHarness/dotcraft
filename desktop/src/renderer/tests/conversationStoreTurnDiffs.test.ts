import { beforeEach, describe, expect, it } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { wireTurnToConversationTurn } from '../types/conversation'

const s = () => useConversationStore.getState()

const createdAt = '2026-09-23T00:00:00.000Z'

const patch = (...lines: string[]): string => lines.map((line) => `${line}\n`).join('')

const updateDiff = (path: string, before: string, after: string): string =>
  patch(`diff --git a/${path} b/${path}`, `--- a/${path}`, `+++ b/${path}`, '@@ -1 +1 @@', `-${before}`, `+${after}`)

const addDiff = (path: string): string =>
  patch(`diff --git a/${path} b/${path}`, 'new file mode 100644', '--- /dev/null', `+++ b/${path}`, '@@ -0,0 +1,2 @@', '+one', '+two')

function fileChange(path: string, diff: string, additions = 1, deletions = 1, kind: 'add' | 'update' = 'update') {
  return { kind: 'fileChange', changes: [{ path, kind, diff, additions, deletions }] }
}

function wireCall(id: string, toolName = 'EditFile'): Record<string, unknown> {
  return { id, type: 'toolCall', createdAt, payload: { callId: `call-${id}`, toolName, arguments: { path: 'ignored' } } }
}

function wireResult(id: string, structuredContent: unknown): Record<string, unknown> {
  return {
    id: `${id}-result`,
    type: 'toolResult',
    createdAt,
    completedAt: createdAt,
    payload: { callId: `call-${id}`, result: 'ok', success: true, structuredContent }
  }
}

function wireTurn(id: string, items: Record<string, unknown>[], status = 'completed'): Record<string, unknown> {
  return { id, threadId: 'thread-1', status, items, startedAt: createdAt }
}

const twoEditsOfA = wireTurn('t1', [
  wireCall('a'),
  wireResult('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))),
  wireCall('b'),
  wireResult('b', fileChange('src/a.ts', updateDiff('src/a.ts', 'y', 'z')))
])

const loadHistory = (...turns: Record<string, unknown>[]) => turns.map(wireTurnToConversationTurn)

const rowsOf = (turnId: string) =>
  s().turnDiffs.get(turnId)?.files.map((row) => [row.key, row.diff.filePath, row.diff.status]) ?? []

function startLiveTurn(): void {
  s().onTurnStarted(wireTurn('t1', [], 'running'))
  s().onItemStarted({ turnId: 't1', item: wireCall('a') })
}

beforeEach(() => {
  s().reset()
  useConversationStore.setState({ remoteWorkspaceActive: false, workspacePath: '' })
})

describe('history turn diffs', () => {
  it('builds item diffs and one row per edit from persisted file changes', () => {
    s().setTurns(loadHistory(twoEditsOfA))

    expect([...s().itemDiffs.keys()]).toEqual(['a', 'b'])
    expect(s().turnDiffs.get('t1')?.source).toBe('history')
    expect(rowsOf('t1')).toEqual([
      ['t1::a', 'src/a.ts', 'written'],
      ['t1::b', 'src/a.ts', 'written']
    ])
  })

  it('shows diffs for remote workspaces too', () => {
    s().setRemoteWorkspaceActive(true)
    s().setTurns(loadHistory(twoEditsOfA))

    expect(rowsOf('t1')).toHaveLength(2)
  })

  it('keeps live entries and reverted rows when history reloads in place', () => {
    const history = loadHistory(twoEditsOfA, wireTurn('t2', [wireCall('c'), wireResult('c', fileChange('src/c.ts', addDiff('src/c.ts'), 2, 0, 'add'))]))
    s().setTurns(history)
    s().onTurnDiffUpdated({ turnId: 't2', diff: addDiff('src/c.ts') })
    s().setTurnFileStatus('t1', 't1::a', 'reverted')

    s().setTurns(history, { preserveExistingRealtime: true })

    expect(s().turnDiffs.get('t2')?.source).toBe('live')
    expect(rowsOf('t1')[0]).toEqual(['t1::a', 'src/a.ts', 'reverted'])
  })

  it('reset clears item and turn diffs', () => {
    s().setTurns(loadHistory(twoEditsOfA))
    s().reset()

    expect(s().itemDiffs.size).toBe(0)
    expect(s().turnDiffs.size).toBe(0)
  })
})

describe('live turn diffs', () => {
  it('fills the item diff and the turn row when a file tool result completes', () => {
    startLiveTurn()
    s().onItemCompleted({ turnId: 't1', item: wireResult('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))) })

    expect(s().itemDiffs.get('a')?.filePath).toBe('src/a.ts')
    expect(rowsOf('t1')).toEqual([['t1::a', 'src/a.ts', 'written']])
  })

  it('picks up a file change whose result arrived before its tool call', () => {
    s().onTurnStarted(wireTurn('t1', [], 'running'))
    s().onItemCompleted({ turnId: 't1', item: wireResult('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))) })
    s().onItemStarted({ turnId: 't1', item: wireCall('a') })

    expect(s().itemDiffs.has('a')).toBe(true)
    expect(rowsOf('t1')).toEqual([['t1::a', 'src/a.ts', 'written']])
  })

  it('replaces the turn entry with each live snapshot', () => {
    startLiveTurn()
    s().onItemCompleted({ turnId: 't1', item: wireResult('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))) })

    s().onTurnDiffUpdated({ turnId: 't1', diff: updateDiff('src/a.ts', 'x', 'y') + addDiff('src/b.ts') })
    expect(s().turnDiffs.get('t1')?.source).toBe('live')
    expect(rowsOf('t1')).toEqual([
      ['t1::src/a.ts', 'src/a.ts', 'written'],
      ['t1::src/b.ts', 'src/b.ts', 'written']
    ])

    s().onTurnDiffUpdated({ turnId: 't1', diff: addDiff('src/b.ts') })
    expect(rowsOf('t1')).toEqual([['t1::src/b.ts', 'src/b.ts', 'written']])
  })

  it('falls back to per-call rows when the snapshot is empty', () => {
    startLiveTurn()
    s().onItemCompleted({ turnId: 't1', item: wireResult('a', fileChange('src/a.ts', updateDiff('src/a.ts', 'x', 'y'))) })
    s().onTurnDiffUpdated({ turnId: 't1', diff: addDiff('src/b.ts') })

    s().onTurnDiffUpdated({ turnId: 't1', diff: '' })

    expect(s().turnDiffs.get('t1')?.source).toBe('history')
    expect(rowsOf('t1')).toEqual([['t1::a', 'src/a.ts', 'written']])
  })

  it('ignores a snapshot without a turn id', () => {
    startLiveTurn()
    s().onTurnDiffUpdated({ turnId: '', diff: addDiff('src/b.ts') })

    expect(s().turnDiffs.size).toBe(0)
  })
})
