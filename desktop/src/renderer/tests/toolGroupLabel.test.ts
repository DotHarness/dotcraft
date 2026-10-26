import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import { formatToolGroupLabel } from '../utils/toolGroupLabel'
import { formatCollapsedToolLabel } from '../utils/toolCallDisplay'
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

function makeCall(id: string, presentationId: string, options: Record<string, unknown>): ConversationItem {
  return withTestCorePresentation({ id, type: 'toolCall', status: 'completed', createdAt: new Date().toISOString() }, presentationId, options)
}

describe('formatToolGroupLabel', () => {
  it('names each kind of work once, in a fixed order, counting repeated edits of one file once', () => {
    const items = [
      makeCall('1', CORE_TOOL_PRESENTATION_IDS.shell, {}),
      makeCall('2', CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read' }),
      makeItem('EditFile', '3', 'edit', 'src/a.ts'),
      makeCall('4', CORE_TOOL_PRESENTATION_IDS.web, { operation: 'search' }),
      makeItem('EditFile', '5', 'edit', 'src/b.ts'),
      makeCall('6', CORE_TOOL_PRESENTATION_IDS.shell, {})
    ]

    expect(formatToolGroupLabel(items, 'en', new Map())).toBe('Edited files, explored a file, ran commands, searched the web')
    expect(formatToolGroupLabel([
      makeItem('EditFile', '1', 'edit', 'src/a.ts'),
      makeItem('EditFile', '2', 'edit', 'src/a.ts')
    ], 'en', new Map())).toBe('Edited a file')
  })

  it('says created only when every written file is new', () => {
    const items = [
      makeItem('WriteFile', '1', 'write', 'src/new.ts'),
      makeItem('EditFile', '2', 'edit', 'src/new.ts')
    ]
    const itemDiffs = new Map<string, FileDiff>([['1', makeDiff('src/new.ts', true)]])

    expect(formatToolGroupLabel(items, 'en', itemDiffs)).toBe('Created a file')
    expect(formatToolGroupLabel([...items, makeItem('EditFile', '3', 'edit', 'src/a.ts')], 'en', itemDiffs)).toBe('Edited files')
  })
})

describe('Node REPL labels', () => {
  const repl = (id: string, surface: string, title?: string): ConversationItem => ({
    ...makeCall(id, CORE_TOOL_PRESENTATION_IDS.nodeRepl, { surface }),
    toolName: 'NodeReplJs',
    arguments: { code: 'await tab.goto(url)', ...(title ? { title } : {}) }
  })

  it('shows the run title, or what the run used, and names the surface in a group', () => {
    expect(formatCollapsedToolLabel(repl('1', 'browser', '  Open   the pricing page '), repl('1', 'browser', '  Open   the pricing page ').arguments, 'en'))
      .toBe('Open the pricing page')
    expect(formatCollapsedToolLabel(repl('2', 'computer'), repl('2', 'computer').arguments, 'en')).toBe('Used the computer')
    expect(formatToolGroupLabel([repl('1', 'chrome'), makeCall('2', CORE_TOOL_PRESENTATION_IDS.shell, {})], 'en', new Map()))
      .toBe('Used Chrome, ran a command')
  })

  it('names a call by the surface it used rather than the plugin that registered the tool', () => {
    const computer = { ...repl('1', 'browser'), structuredResult: { surface: 'computer' } }

    expect(formatCollapsedToolLabel(computer, computer.arguments, 'en')).toBe('Used the computer')
    expect(formatToolGroupLabel([computer, repl('2', 'browser')], 'en', new Map())).toBe('Used the browser, used the computer')
  })
})
