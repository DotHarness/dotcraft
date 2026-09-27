import { describe, expect, it } from 'vitest'
import { deriveTurnOutputs } from '../components/conversation/turnNavigation/turnOutputs'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import type { TurnDiff } from '../types/turnDiff'

const CREATED_AT = '2026-01-01T00:00:00.000Z'

function turn(status: ConversationTurn['status'], items: ConversationItem[] = []): ConversationTurn {
  return { id: 't1', threadId: 'thread-1', status, startedAt: CREATED_AT, items }
}

function image(id: string, extra: Partial<ConversationItem> = {}): ConversationItem {
  return {
    id,
    type: 'imageGeneration',
    status: 'completed',
    imageGenerationStatus: 'completed',
    result: 'aW1hZ2U=',
    createdAt: CREATED_AT,
    ...extra
  }
}

function diffs(...files: Array<string | [string, FileDiff['status']]>): Map<string, TurnDiff> {
  return new Map([['t1', {
    turnId: 't1',
    source: 'history',
    files: files.map((file, index) => {
      const [filePath, status] = typeof file === 'string' ? [file, 'written' as const] : file
      return {
        key: `t1::item-${index}`,
        turnId: 't1',
        diff: { filePath, additions: 1, deletions: 0, diffHunks: [], status, isNewFile: true },
        patchText: '',
        truncated: false
      }
    })
  }]])
}

describe('turn outputs', () => {
  it('lists written Markdown files, HTML files, and generated images', () => {
    const outputs = deriveTurnOutputs(
      turn('completed', [image('img-1'), image('img-failed', { imageGenerationStatus: 'failed', result: undefined })]),
      diffs('docs/guide.md', 'site/index.html', 'src/app.ts', ['docs/reverted.md', 'reverted'], '.craft/visualizations/chart.html')
    )

    expect(outputs).toEqual([
      { kind: 'webPreview', label: 'index.html' },
      { kind: 'file', label: 'guide.md' },
      { kind: 'image', label: '' }
    ])
  })

  it('orders web previews, files, then images and drops duplicates of the same kind and label', () => {
    const outputs = deriveTurnOutputs(
      turn('completed', [image('img-1'), image('img-2')]),
      diffs('a/notes.md', 'report.htm', 'b/notes.md', 'notes.markdown', 'site/report.htm')
    )

    expect(outputs).toEqual([
      { kind: 'webPreview', label: 'report.htm' },
      { kind: 'file', label: 'notes.md' },
      { kind: 'file', label: 'notes.markdown' },
      { kind: 'image', label: '' }
    ])
  })

  it('has none for a turn that did not complete', () => {
    for (const status of ['running', 'failed', 'cancelled'] as const) {
      expect(deriveTurnOutputs(turn(status, [image('img-1')]), diffs('notes.md'))).toEqual([])
    }
  })
})
