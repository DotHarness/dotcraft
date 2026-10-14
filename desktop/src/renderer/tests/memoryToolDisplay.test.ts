import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { collectMemoryFileNames, countExplorationGroup } from '../utils/memoryToolDisplay'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

let nextId = 0

function call(
  presentationId: string,
  options: Record<string, unknown>,
  args: Record<string, unknown>
): ConversationItem {
  nextId += 1
  return withTestCorePresentation({
    id: `item-${nextId}`,
    type: 'toolCall',
    status: 'completed',
    toolName: 'Tool',
    arguments: args,
    createdAt: '2026-10-13T00:00:00Z'
  }, presentationId, options)
}

const recall = (args: Record<string, unknown>, operation = 'read'): ConversationItem =>
  call(CORE_TOOL_PRESENTATION_IDS.readFile, { operation, memory: 'recall' }, args)
const update = (args: Record<string, unknown>): ConversationItem =>
  call(CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'edit', memory: 'update' }, args)

describe('collectMemoryFileNames', () => {
  it('counts each memory file once, leaving out the index and searched directories', () => {
    const items = [
      recall({ path: '.craft/memory/user_role.md' }),
      recall({ path: 'F:\\ws\\.craft\\memory\\user_role.md' }),
      recall({ path: '.craft/memory/MEMORY.md' }),
      recall({ path: '.craft/memory', pattern: 'role' }, 'grep'),
      update({ path: '.craft/memory/feedback_tests.md' }),
      update({ path: '.craft/memory/memory.md' }),
      call(CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read' }, { path: 'docs/notes.md' })
    ]

    expect(collectMemoryFileNames(items)).toEqual(['user_role.md', 'feedback_tests.md'])
  })

  it('finds no memory file in an update that only rewrote the index', () => {
    expect(collectMemoryFileNames([update({ path: '.craft/memory/MEMORY.md' })])).toEqual([])
  })
})

describe('countExplorationGroup', () => {
  const read = (path: string): ConversationItem =>
    call(CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read' }, { path })

  it('splits recalled memory files from the other explored calls', () => {
    expect(countExplorationGroup([
      read('src/a.ts'),
      recall({ path: '.craft/memory/user_role.md' }),
      recall({ path: '.craft/memory/user_role.md' }, 'grep'),
      recall({ path: '.craft/memory/MEMORY.md' }),
      read('src/b.ts')
    ])).toEqual({ recalls: 3, memoryFiles: 1, explored: 2 })
  })

  it('counts a group without recalls as exploration only', () => {
    expect(countExplorationGroup([read('src/a.ts'), read('src/b.ts')]))
      .toEqual({ recalls: 0, memoryFiles: 0, explored: 2 })
  })

  it('counts a search-only recall group with no named memory file', () => {
    expect(countExplorationGroup([recall({ path: '.craft/memory', pattern: 'role' }, 'grep')]))
      .toEqual({ recalls: 1, memoryFiles: 0, explored: 0 })
  })
})
