import { describe, expect, it } from 'vitest'
import {
  fileChangeEntryToTurnFileChange,
  parseFileChangeStructuredContent,
  parseUnifiedDiff,
  toWorkspaceRelativePatch
} from '../utils/unifiedDiff'

const CR = String.fromCharCode(13)
const patch = (...lines: string[]): string => lines.map((line) => `${line}\n`).join('')

const fooUpdate = patch(
  'diff --git a/src/Foo.cs b/src/Foo.cs',
  'index c60197f..9dc664d',
  '--- a/src/Foo.cs',
  '+++ b/src/Foo.cs',
  '@@ -1,3 +1,4 @@',
  ' using System;',
  '+using System.Linq;',
  ' ',
  ' namespace Demo;'
)

const barUpdate = patch(
  'diff --git a/src/Bar.cs b/src/Bar.cs',
  'index 1111111..2222222',
  '--- a/src/Bar.cs',
  '+++ b/src/Bar.cs',
  '@@ -1,2 +1,2 @@',
  '-old',
  '+new',
  ' tail'
)

describe('parseUnifiedDiff', () => {
  it('returns nothing for an empty diff', () => {
    expect(parseUnifiedDiff('')).toEqual([])
  })

  it('splits a multi-file diff into one entry per file with exact patch text', () => {
    const parsed = parseUnifiedDiff(fooUpdate + barUpdate)

    expect(parsed.map((entry) => entry.diff.filePath)).toEqual(['src/Foo.cs', 'src/Bar.cs'])
    expect(parsed.map((entry) => [entry.diff.additions, entry.diff.deletions])).toEqual([[1, 0], [1, 1]])
    expect(parsed.map((entry) => entry.patchText)).toEqual([fooUpdate, barUpdate])
    expect(parsed[0].diff).toMatchObject({ status: 'written', isNewFile: false })
    expect(parsed[0].error).toBeUndefined()
  })

  it('marks an added file as new and keeps the hunk numbers from the header', () => {
    const [entry] = parseUnifiedDiff(
      patch(
        'diff --git a/src/New.cs b/src/New.cs',
        'new file mode 100644',
        'index 0000000..abc1234',
        '--- /dev/null',
        '+++ b/src/New.cs',
        '@@ -0,0 +1,2 @@',
        '+line 1',
        '+line 2'
      )
    )

    expect(entry.diff.filePath).toBe('src/New.cs')
    expect(entry.diff.isNewFile).toBe(true)
    expect(entry.diff.additions).toBe(2)
    expect(entry.diff.diffHunks).toEqual([
      {
        oldStart: 0,
        oldLines: 0,
        newStart: 1,
        newLines: 2,
        lines: [
          { type: 'add', content: 'line 1' },
          { type: 'add', content: 'line 2' }
        ]
      }
    ])
  })

  it('names an empty created file from the git header', () => {
    const [entry] = parseUnifiedDiff(
      patch('diff --git a/docs/empty.md b/docs/empty.md', 'new file mode 100644', 'index 0000000..e69de29')
    )

    expect(entry.diff).toMatchObject({ filePath: 'docs/empty.md', isNewFile: true, additions: 0, diffHunks: [] })
  })

  it('treats a removal of all content as a change to an existing file', () => {
    const [entry] = parseUnifiedDiff(
      patch(
        'diff --git a/src/Old.cs b/src/Old.cs',
        'deleted file mode 100644',
        'index abc1234..0000000',
        '--- a/src/Old.cs',
        '+++ /dev/null',
        '@@ -1,2 +0,0 @@',
        '-a',
        '-b'
      )
    )

    expect(entry.diff).toMatchObject({ filePath: 'src/Old.cs', isNewFile: false, additions: 0, deletions: 2 })
  })

  it('keeps carriage returns in the patch text but not in displayed lines', () => {
    const text = patch(
      'diff --git a/crlf.txt b/crlf.txt',
      'index 1111111..2222222',
      '--- a/crlf.txt',
      '+++ b/crlf.txt',
      '@@ -1,2 +1,2 @@',
      ` one${CR}`,
      `-two${CR}`,
      `+2${CR}`
    )
    const [entry] = parseUnifiedDiff(text)

    expect(entry.patchText).toBe(text)
    expect(entry.diff.diffHunks[0].lines.map((line) => line.content)).toEqual(['one', 'two', '2'])
  })

  it('skips no-newline markers on both sides', () => {
    const [entry] = parseUnifiedDiff(
      patch(
        'diff --git a/eof.txt b/eof.txt',
        'index 1111111..2222222',
        '--- a/eof.txt',
        '+++ b/eof.txt',
        '@@ -1,2 +1,2 @@',
        ' keep',
        '-old',
        '\\ No newline at end of file',
        '+new',
        '\\ No newline at end of file'
      )
    )

    expect(entry.diff.additions).toBe(1)
    expect(entry.diff.deletions).toBe(1)
    expect(entry.diff.diffHunks[0].lines).toEqual([
      { type: 'context', content: 'keep' },
      { type: 'remove', content: 'old' },
      { type: 'add', content: 'new' }
    ])
  })

  it('decodes git-quoted paths', () => {
    const quotedA = '"a/\\344\\275\\240\\345\\245\\275.txt"'
    const quotedB = '"b/\\344\\275\\240\\345\\245\\275.txt"'
    const [entry] = parseUnifiedDiff(
      patch(`diff --git ${quotedA} ${quotedB}`, 'index 1..2', `--- ${quotedA}`, `+++ ${quotedB}`, '@@ -1 +1 @@', '-a', '+b')
    )

    expect(entry.diff.filePath).toBe('你好.txt')
  })

  it('normalizes backslash separators', () => {
    const [entry] = parseUnifiedDiff(
      patch('diff --git a/src\\Win.cs b/src\\Win.cs', 'index 1..2', '--- a/src\\Win.cs', '+++ b/src\\Win.cs', '@@ -1 +1 @@', '-a', '+b')
    )

    expect(entry.diff.filePath).toBe('src/Win.cs')
  })

  it('makes absolute paths inside the workspace relative', () => {
    const inside = 'C:/Work/Repo/src/Abs.cs'
    const outside = 'D:/Other/x.cs'
    const parsed = parseUnifiedDiff(
      patch(`diff --git a/${inside} b/${inside}`, 'index 1..2', `--- a/${inside}`, `+++ b/${inside}`, '@@ -1 +1 @@', '-a', '+b') +
        patch(`diff --git a/${outside} b/${outside}`, 'index 1..2', `--- a/${outside}`, `+++ b/${outside}`, '@@ -1 +1 @@', '-a', '+b'),
      'c:\\work\\repo\\'
    )

    expect(parsed.map((entry) => entry.diff.filePath)).toEqual(['src/Abs.cs', outside])
  })
})

describe('parseFileChangeStructuredContent', () => {
  const canonical = {
    kind: 'fileChange',
    changes: [
      { path: 'src/Foo.cs', kind: 'update', diff: fooUpdate, additions: 1, deletions: 0 },
      { path: 'big.txt', kind: 'add', additions: 5000, deletions: 0, truncated: true }
    ]
  }

  it('accepts the canonical shape', () => {
    expect(parseFileChangeStructuredContent(canonical)).toEqual(canonical)
  })

  it('treats null optional fields as absent', () => {
    const parsed = parseFileChangeStructuredContent({
      kind: 'fileChange',
      changes: [{ path: 'a.txt', kind: 'update', diff: null, additions: 1, deletions: 1, truncated: null }]
    })

    expect(parsed?.changes[0]).toEqual({ path: 'a.txt', kind: 'update', additions: 1, deletions: 1 })
  })

  it('rejects other shapes', () => {
    const change = canonical.changes[0]
    for (const value of [
      null,
      'fileChange',
      { ...canonical, kind: 'mcp' },
      { kind: 'fileChange' },
      { kind: 'fileChange', changes: 'none' },
      { kind: 'fileChange', changes: [{ ...change, kind: 'delete' }] },
      { kind: 'fileChange', changes: [{ ...change, additions: '1' }] },
      { kind: 'fileChange', changes: [{ ...change, deletions: Number.NaN }] },
      { kind: 'fileChange', changes: [{ ...change, path: 3 }] },
      { kind: 'fileChange', changes: [{ ...change, diff: 42 }] },
      { kind: 'fileChange', changes: [{ ...change, truncated: 'yes' }] }
    ]) {
      expect(parseFileChangeStructuredContent(value)).toBeNull()
    }
  })
})

describe('fileChangeEntryToTurnFileChange', () => {
  it('parses the diff but reports the server counts', () => {
    const row = fileChangeEntryToTurnFileChange(
      { path: 'src/Foo.cs', kind: 'update', diff: fooUpdate, additions: 7, deletions: 2 },
      'turn-1',
      'item-1'
    )

    expect(row).toMatchObject({ key: 'turn-1::item-1', turnId: 'turn-1', patchText: fooUpdate, truncated: false })
    expect(row.diff).toMatchObject({ filePath: 'src/Foo.cs', additions: 7, deletions: 2, isNewFile: false })
    expect(row.diff.diffHunks).toHaveLength(1)
  })

  it('marks a change without a diff as truncated', () => {
    const row = fileChangeEntryToTurnFileChange(
      { path: 'C:/Work/Repo/big.txt', kind: 'add', additions: 5000, deletions: 0 },
      'turn-1',
      'item-2',
      'C:\\Work\\Repo'
    )

    expect(row.truncated).toBe(true)
    expect(row.patchText).toBe('')
    expect(row.diff).toMatchObject({ filePath: 'big.txt', additions: 5000, isNewFile: true, diffHunks: [] })
  })

  it('keeps a no-op write as an empty, untruncated change', () => {
    const row = fileChangeEntryToTurnFileChange(
      { path: 'src/Same.cs', kind: 'update', additions: 0, deletions: 0 },
      'turn-1',
      'item-4'
    )

    expect(row).toMatchObject({ truncated: false, patchText: '' })
    expect(row.diff).toMatchObject({ filePath: 'src/Same.cs', additions: 0, deletions: 0, diffHunks: [] })
  })

  it('marks a diff cut mid-hunk as truncated', () => {
    const cut = fooUpdate.slice(0, fooUpdate.lastIndexOf(' namespace'))
    expect(parseUnifiedDiff(cut)[0].error).toBe(true)

    const row = fileChangeEntryToTurnFileChange(
      { path: 'src/Foo.cs', kind: 'update', diff: cut, additions: 1, deletions: 0 },
      'turn-1',
      'item-3'
    )

    expect(row.truncated).toBe(true)
    expect(row.patchText).toBe(cut)
    expect(row.diff).toMatchObject({ filePath: 'src/Foo.cs', additions: 1, deletions: 0, diffHunks: [] })
  })
})

describe('toWorkspaceRelativePatch', () => {
  it('rewrites only header paths inside the workspace', () => {
    const abs = 'C:/Work/Repo/src/Foo.cs'
    const absolute = patch(
      `diff --git a/${abs} b/${abs}`,
      'index 1111111..2222222',
      `--- a/${abs}`,
      `+++ b/${abs}`,
      '@@ -1,2 +1,2 @@',
      `--- a/${abs}${CR}`,
      `+new${CR}`,
      ` tail${CR}`
    )
    const relative = patch(
      'diff --git a/src/Foo.cs b/src/Foo.cs',
      'index 1111111..2222222',
      '--- a/src/Foo.cs',
      '+++ b/src/Foo.cs',
      '@@ -1,2 +1,2 @@',
      `--- a/${abs}${CR}`,
      `+new${CR}`,
      ` tail${CR}`
    )
    const elsewhere = patch(
      'diff --git a/D:/Elsewhere/x.txt b/D:/Elsewhere/x.txt',
      'new file mode 100644',
      'index 0000000..1111111',
      '--- /dev/null',
      '+++ b/D:/Elsewhere/x.txt',
      '@@ -0,0 +1 @@',
      '+x'
    )

    expect(toWorkspaceRelativePatch(absolute + elsewhere + fooUpdate, 'C:\\Work\\Repo')).toBe(relative + elsewhere + fooUpdate)
  })

  it('keeps rewritten quoted names quoted', () => {
    const withNames = (name: (side: string) => string): string =>
      patch(`diff --git ${name('a')} ${name('b')}`, 'index 1..2', `--- ${name('a')}`, `+++ ${name('b')}`, '@@ -1 +1 @@', '-a', '+b')
    const absolute = withNames((side) => `"${side}/C:/Work/Repo/\\344\\275\\240.txt"`)
    const relative = withNames((side) => `"${side}/\\344\\275\\240.txt"`)

    expect(toWorkspaceRelativePatch(absolute, 'C:/Work/Repo')).toBe(relative)
  })
})
