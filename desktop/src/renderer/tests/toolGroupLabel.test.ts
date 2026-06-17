import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import { formatToolGroupLabel } from '../utils/toolGroupLabel'

function makeItem(
  toolName: string,
  id: string,
  path?: string
): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    arguments: path ? { path } : undefined,
    createdAt: new Date().toISOString()
  }
}

function makeDiff(path: string, isNewFile: boolean): FileDiff {
  return {
    filePath: path,
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 0,
    deletions: 0,
    diffHunks: [],
    status: 'written',
    isNewFile
  }
}

describe('formatToolGroupLabel write dedup', () => {
  it('deduplicates repeated EditFile calls on the same file path', () => {
    const items = [
      makeItem('EditFile', '1', 'src/a.ts'),
      makeItem('EditFile', '2', 'src/a.ts')
    ]

    const label = formatToolGroupLabel('write', items, 'en', new Map())
    expect(label).toBe('Modified 1 files')
  })

  it('prefers created over modified for the same file path', () => {
    const items = [
      makeItem('WriteFile', '1', 'src/new.ts'),
      makeItem('EditFile', '2', 'src/new.ts')
    ]
    const changedFiles = new Map<string, FileDiff>([
      ['src/new.ts', makeDiff('src/new.ts', true)]
    ])

    const label = formatToolGroupLabel('write', items, 'en', changedFiles)
    expect(label).toBe('Created 1 files')
  })

  it('deduplicates mixed write operations across multiple files', () => {
    const items = [
      makeItem('EditFile', '1', 'src/a.ts'),
      makeItem('EditFile', '2', 'src/a.ts'),
      makeItem('WriteFile', '3', 'src/b.ts'),
      makeItem('WriteFile', '4', 'src/c.ts'),
      makeItem('EditFile', '5', 'src/c.ts')
    ]
    const changedFiles = new Map<string, FileDiff>([
      ['src/b.ts', makeDiff('src/b.ts', false)],
      ['src/c.ts', makeDiff('src/c.ts', true)]
    ])

    const label = formatToolGroupLabel('write', items, 'en', changedFiles)
    expect(label).toBe('Created 1, modified 2 files')
  })
})
