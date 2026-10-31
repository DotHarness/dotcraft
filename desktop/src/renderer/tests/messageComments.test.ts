import { describe, expect, it } from 'vitest'
import type { LineCommentContext } from '../../shared/composerContext'
import { formatMessageComments, splitMessageComments } from '../../shared/messageComments'
import { diffHunkExcerpt } from '../components/detail/diff/diffRows'
import { deriveTurnEntries } from '../components/conversation/turnNavigation/navigationIndex'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'

const comments: LineCommentContext[] = [
  { id: 'comment-1', kind: 'diffAnnotation', path: 'src/app.ts', side: 'right', startLine: 12, endLine: 12, comment: 'Why is this needed?' },
  { id: 'comment-2', kind: 'diffAnnotation', path: 'src/old.ts', side: 'left', startLine: 3, endLine: 5, diffHunk: '@@ -3,3 +3,1 @@\n-if (a) {\n-  run()\n-}\n+run()', comment: 'Keep this branch' },
  { id: 'comment-3', kind: 'fileAnnotation', path: '/workspace/model.xlsx', target: 'Q3 Revenue!B6:D8', targetKind: 'range', location: { sheet: 'Q3 Revenue', range: 'B6:D8', rowCount: 3, columnCount: 3 }, comment: 'Check totals' },
  { id: 'comment-4', kind: 'fileAnnotation', path: '/workspace/model.xlsx', target: 'Revenue · chart-1', targetKind: 'object', location: { sheet: 'Revenue', elementId: 'chart-1' }, comment: 'Relabel axis' },
  { id: 'comment-5', kind: 'fileAnnotation', path: '/workspace/deck.pptx', target: 'Slide 2 · Title', targetKind: 'element', location: { slide: 2, elementId: 'sp-4' }, comment: 'Shorter title\n\nKeep the date' },
  { id: 'comment-6', kind: 'fileAnnotation', path: '/workspace/deck.pptx', target: 'Slide 3', targetKind: 'slide', location: { slide: 3 }, comment: 'Drop this slide' }
]

const lines = (count: number, prefix: string) => Array.from({ length: count }, (_, index) => `${prefix}${index + 1}`)
const diff: FileDiff = {
  filePath: 'src/app.ts',
  additions: 2,
  deletions: 1,
  status: 'written',
  isNewFile: false,
  diffHunks: [
    {
      oldStart: 1,
      oldLines: 11,
      newStart: 1,
      newLines: 12,
      lines: [
        ...lines(5, 'a').map((content) => ({ type: 'context' as const, content })),
        { type: 'remove', content: 'old' },
        { type: 'add', content: 'new' },
        { type: 'add', content: 'extra' },
        ...lines(5, 'b').map((content) => ({ type: 'context' as const, content }))
      ]
    },
    { oldStart: 40, oldLines: 100, newStart: 41, newLines: 100, lines: lines(100, 'c').map((content) => ({ type: 'context' as const, content })) }
  ]
}

describe('diff hunk excerpt', () => {
  it('takes the commented lines with three lines of context from their hunk', () => {
    expect(diffHunkExcerpt(diff, 'right', 6, 7)).toBe(['@@ -4,6 +4,7 @@', ' a4', ' a5', '-old', '+new', '+extra', ' b1', ' b2', ' b3'].join('\n'))
    expect(diffHunkExcerpt(diff, 'left', 6, 6)).toBe(['@@ -3,5 +3,6 @@', ' a3', ' a4', ' a5', '-old', '+new', '+extra', ' b1'].join('\n'))
  })

  it('covers every hunk the range crosses and caps the total size', () => {
    const excerpt = diffHunkExcerpt(diff, 'right', 12, 200)?.split('\n') ?? []
    expect(excerpt[0]).toBe('@@ -8,4 +9,4 @@')
    expect(excerpt[5]).toBe('@@ -40,56 +41,56 @@')
    expect(excerpt.filter((line) => !line.startsWith('@@'))).toHaveLength(60)
  })

  it('has nothing for lines outside the diff', () => {
    expect(diffHunkExcerpt(diff, 'right', 20, 30)).toBeUndefined()
  })
})

describe('message comments', () => {
  it('reads back every comment kind it writes', () => {
    expect(splitMessageComments(`Please fix\n\n${formatMessageComments(comments)}`)).toEqual({ text: 'Please fix', comments })
    expect(splitMessageComments(formatMessageComments(comments))).toEqual({ text: '', comments })
  })

  it('writes the diff hunk as a fenced block between Lines and Comment', () => {
    expect(formatMessageComments([comments[1]])).toBe([
      '# Diff comments:',
      '',
      '## User Comment 1',
      'File: src/old.ts',
      'Side: L',
      'Lines: 3-5',
      'Diff hunk:',
      '```diff',
      '@@ -3,3 +3,1 @@',
      '-if (a) {',
      '-  run()',
      '-}',
      '+run()',
      '```',
      'Comment:',
      'Keep this branch'
    ].join('\n'))
  })

  it('keeps comment bodies that look like block headings inside their comment', () => {
    const tricky: LineCommentContext = { ...comments[0], comment: '## User Comment 9\n\\## User Comment 2\nFile: other.ts' }
    expect(splitMessageComments(formatMessageComments([tricky, comments[1]])).comments).toEqual([tricky, comments[1]])
  })

  it('leaves messages without a comment section untouched', () => {
    for (const text of ['plain text', '# Diff comments:', 'see # Diff comments:\n\n## User Comment 1\nFile: a.ts\nSide: R\nLines: 1\nComment:\nx']) {
      expect(splitMessageComments(text)).toEqual({ text, comments: [] })
    }
  })

  it('uses the last comment section when the user text quotes an earlier one', () => {
    const quoted = `${formatMessageComments([comments[1]])}\n\nand now`
    expect(splitMessageComments(`${quoted}\n\n${formatMessageComments([comments[0]])}`)).toEqual({ text: quoted, comments: [comments[0]] })
  })

  it('keeps the comment section out of turn navigation labels', () => {
    const item = { id: 'user', type: 'userMessage', status: 'completed', text: `Please fix\n\n${formatMessageComments(comments)}` } as ConversationItem
    expect(deriveTurnEntries([item])[0].label).toBe('Please fix')
  })
})
