import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { latestChanges, parseUnifiedDiff, turnChanges } from './turnChanges'

const UPDATE = [
  'diff --git a/src/store.ts b/src/store.ts',
  'index 1111111..2222222 100644',
  '--- a/src/store.ts',
  '+++ b/src/store.ts',
  '@@ -3,4 +3,5 @@ export class Store {',
  ' const a = 1',
  '-const b = 2',
  '+const b = 3',
  '+const c = 4',
  ' const d = 5',
  ' const e = 6',
  '\\ No newline at end of file',
  '@@ -20 +21 @@',
  '-old()',
  '+renewed()',
  '',
].join('\n')

const CREATE = [
  'diff --git a/docs/new file.md b/docs/new file.md',
  'new file mode 100644',
  'index 0000000..3333333',
  '--- /dev/null',
  '+++ b/docs/new file.md',
  '@@ -0,0 +1,2 @@',
  '+# Title',
  '+--- not a header',
  '',
].join('\n')

function result(turnId: string, id: string, changes: Record<string, unknown>[]): HistoryItem {
  return {
    id,
    turnId,
    type: 'toolResult',
    status: 'completed',
    payload: { toolName: 'EditFile', callId: id, structuredContent: { kind: 'fileChange', writeState: 'applied', changes } },
    createdAt: '2026-10-04T08:00:00Z',
    completedAt: '2026-10-04T08:00:01Z',
    partial: false,
  }
}

function history(items: HistoryItem[], turns: string[], diffs?: Record<string, string>): ChatHistory {
  return { ...emptyHistory(), items, turns: turns.map((id) => ({ id, status: 'completed', error: null })), ...(diffs ? { diffs } : {}) }
}

describe('unified diff parsing', () => {
  it('splits a turn diff into files with their line counts, hunks, and line numbers', () => {
    const [store, created] = parseUnifiedDiff(`${UPDATE}${CREATE}`)
    expect(store).toMatchObject({ path: 'src/store.ts', added: 3, removed: 2, truncated: false })
    expect(store.hunks.map((hunk) => hunk.header)).toEqual(['@@ -3,4 +3,5 @@ export class Store {', '@@ -20 +21 @@'])
    expect(store.hunks[0].lines).toEqual([
      { kind: 'context', text: 'const a = 1', oldLine: 3, newLine: 3 },
      { kind: 'remove', text: 'const b = 2', oldLine: 4, newLine: null },
      { kind: 'add', text: 'const b = 3', oldLine: null, newLine: 4 },
      { kind: 'add', text: 'const c = 4', oldLine: null, newLine: 5 },
      { kind: 'context', text: 'const d = 5', oldLine: 5, newLine: 6 },
      { kind: 'context', text: 'const e = 6', oldLine: 6, newLine: 7 },
    ])
    expect(created).toMatchObject({ path: 'docs/new file.md', added: 2, removed: 0 })
    expect(created.hunks[0].lines.map((line) => line.text)).toEqual(['# Title', '--- not a header'])
  })

  it('decodes quoted names and tolerates Windows line endings', () => {
    const quoted = ['diff --git "a/\\346\\226\\207.txt" "b/\\346\\226\\207.txt"', '--- "a/\\346\\226\\207.txt"', '+++ "b/\\346\\226\\207.txt"', '@@ -1 +1 @@', '-a', '+b', ''].join('\r\n')
    expect(parseUnifiedDiff(quoted)).toMatchObject([{ path: '文.txt', added: 1, removed: 1 }])
  })
})

describe('turn changes', () => {
  it('rebuilds a reopened turn from its recorded file changes, one row per file', () => {
    const items = [
      result('t1', 'r1', [{ path: 'src/store.ts', kind: 'update', diff: UPDATE, additions: 3, deletions: 2 }]),
      result('t1', 'r2', [{ path: 'src/store.ts', kind: 'update', diff: UPDATE, additions: 3, deletions: 2 }]),
      result('t1', 'r3', [{ path: 'D:/Projects/app/big.json', kind: 'update', additions: 900, deletions: 10, truncated: true }]),
    ]
    const changes = turnChanges(history(items, ['t1']), 't1', 'D:\\Projects\\app')
    expect(changes).toMatchObject({ turnId: 't1', added: 906, removed: 14 })
    expect(changes?.files.map((file) => [file.path, file.added, file.removed, file.hunks.length, file.truncated])).toEqual([
      ['src/store.ts', 6, 4, 4, false],
      ['big.json', 900, 10, 0, true],
    ])
  })

  it('keeps files that differ only by case apart unless the computer is Windows', () => {
    const items = [
      result('t1', 'r1', [{ path: 'src/Foo.ts', kind: 'update', additions: 1, deletions: 0 }]),
      result('t1', 'r2', [{ path: 'src/foo.ts', kind: 'update', additions: 2, deletions: 0 }]),
    ]
    const rows = (workspacePath: string) =>
      turnChanges(history(items, ['t1']), 't1', workspacePath)?.files.map((file) => [file.path, file.added])
    expect(rows('/home/me/app')).toEqual([['src/Foo.ts', 1], ['src/foo.ts', 2]])
    expect(rows('D:\\Projects\\app')).toEqual([['src/Foo.ts', 3]])
  })

  it('prefers the live turn diff and falls back to recorded changes when it is empty', () => {
    const items = [result('t1', 'r1', [{ path: 'src/store.ts', kind: 'update', diff: UPDATE, additions: 3, deletions: 2 }])]
    expect(turnChanges(history(items, ['t1'], { t1: CREATE }), 't1', null)?.files.map((file) => file.path)).toEqual(['docs/new file.md'])
    expect(turnChanges(history(items, ['t1'], { t1: '' }), 't1', null)?.files.map((file) => file.path)).toEqual(['src/store.ts'])
  })

  it('summarizes the latest turn that changed files', () => {
    const items = [result('t1', 'r1', [{ path: 'a.ts', kind: 'add', diff: CREATE, additions: 2, deletions: 0 }])]
    expect(latestChanges(history(items, ['t1', 't2']), null)?.turnId).toBe('t1')
    expect(latestChanges(history(items, ['t1', 't2'], { t3: UPDATE }), null)).toMatchObject({ turnId: 't3', added: 3, removed: 2 })
    expect(latestChanges(history([], ['t1']), null)).toBeNull()
  })
})
