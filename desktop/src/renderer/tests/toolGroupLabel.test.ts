import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import { formatToolGroupLabel } from '../utils/toolGroupLabel'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

function makeItem(
  toolName: string,
  id: string,
  operation: 'write' | 'edit',
  path?: string
): ConversationItem {
  return withTestCorePresentation({
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    arguments: path ? { path } : undefined,
    createdAt: new Date().toISOString()
  }, CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation })
}

function makeDiff(path: string, isNewFile: boolean): FileDiff {
  return {
    filePath: path,
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
      makeItem('EditFile', '1', 'edit', 'src/a.ts'),
      makeItem('EditFile', '2', 'edit', 'src/a.ts')
    ]

    const label = formatToolGroupLabel('write', items, 'en', new Map())
    expect(label).toBe('Modified 1 files')
  })

  it('prefers created over modified for the same file path', () => {
    const items = [
      makeItem('WriteFile', '1', 'write', 'src/new.ts'),
      makeItem('EditFile', '2', 'edit', 'src/new.ts')
    ]
    const itemDiffs = new Map<string, FileDiff>([
      ['1', makeDiff('src/new.ts', true)]
    ])

    const label = formatToolGroupLabel('write', items, 'en', itemDiffs)
    expect(label).toBe('Created 1 files')
  })

  it('deduplicates mixed write operations across multiple files', () => {
    const items = [
      makeItem('EditFile', '1', 'edit', 'src/a.ts'),
      makeItem('EditFile', '2', 'edit', 'src/a.ts'),
      makeItem('WriteFile', '3', 'write', 'src/b.ts'),
      makeItem('WriteFile', '4', 'write', 'src/c.ts'),
      makeItem('EditFile', '5', 'edit', 'src/c.ts')
    ]
    const itemDiffs = new Map<string, FileDiff>([
      ['3', makeDiff('src/b.ts', false)],
      ['4', makeDiff('src/c.ts', true)]
    ])

    const label = formatToolGroupLabel('write', items, 'en', itemDiffs)
    expect(label).toBe('Created 1, modified 2 files')
  })
})
