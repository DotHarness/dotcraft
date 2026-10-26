import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { summarizeSkillActivity } from '../utils/skillOperationDisplay'
import { resolveSkillOperation } from '../utils/toolKind'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

let nextId = 0

function call(presentationId: string, options: Record<string, unknown>, args?: Record<string, unknown>): ConversationItem {
  nextId += 1
  return withTestCorePresentation({
    id: `item-${nextId}`,
    type: 'toolCall',
    status: 'completed',
    toolName: 'Tool',
    arguments: args,
    createdAt: '2026-10-26T00:00:00Z'
  }, presentationId, options)
}

const read = (skillName: string) =>
  call(CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', skill: 'read', skillName })
const write = (skillName: string, skill: 'create' | 'update') =>
  call(CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: skill === 'create' ? 'write' : 'edit', skill, skillName })
const view = (name: string) => call(CORE_TOOL_PRESENTATION_IDS.skillView, {}, { name })

describe('summarizeSkillActivity', () => {
  it('counts each skill once, and reports loading only when nothing was written', () => {
    expect(summarizeSkillActivity([read('pdf'), read('pdf'), view('pdf'), view('docx')]))
      .toEqual({ activity: 'loaded', count: 2 })
  })

  it('reports a skill created in the group as created, even when later calls update it', () => {
    expect(summarizeSkillActivity([write('pdf', 'create'), write('pdf', 'update'), read('pdf')]))
      .toEqual({ activity: 'created', count: 1 })
    expect(summarizeSkillActivity([write('pdf', 'create'), write('docx', 'update'), read('xlsx')]))
      .toEqual({ activity: 'updated', count: 2 })
  })
})

describe('resolveSkillOperation', () => {
  it('yields to the memory mark when a call carries both', () => {
    const both = call(CORE_TOOL_PRESENTATION_IDS.readFile, {
      operation: 'read',
      memory: 'recall',
      skill: 'read',
      skillName: 'pdf'
    })

    expect(resolveSkillOperation(both)).toBeNull()
  })
})
