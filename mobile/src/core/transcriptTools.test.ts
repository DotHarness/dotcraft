import { describe, expect, it } from 'vitest'
import type { HistoryItem } from './history'
import { toolEntry } from './transcriptTools'

const PRESENTATIONS: Record<string, { id: string; operation: string }> = {
  ReadFile: { id: 'core.read-file', operation: 'read' },
  GrepFiles: { id: 'core.read-file', operation: 'grep' },
  WriteFile: { id: 'core.file-write', operation: 'write' },
  EditFile: { id: 'core.file-write', operation: 'edit' },
}

const SKILL_FILE = 'C:\\Users\\me\\.craft\\skills\\release-notes\\SKILL.md'

function call(toolName: string, path: string, mark?: Record<string, string>): HistoryItem {
  const { id, operation } = PRESENTATIONS[toolName]
  return {
    id: 'c1',
    turnId: 't1',
    type: 'toolCall',
    status: 'completed',
    payload: {
      toolName,
      callId: 'c1',
      arguments: { path },
      source: { kind: 'CoreNative', sourceId: 'core-native' },
      presentation: { presentationId: id, options: { operation, ...mark } },
    },
    createdAt: '2026-10-26T08:00:00Z',
    completedAt: '2026-10-26T08:00:01Z',
    partial: false,
  }
}

function marked(skill: string): Record<string, string> {
  return { skill, skillName: 'release-notes', skillFile: SKILL_FILE }
}

function result(of: HistoryItem): HistoryItem {
  return { ...of, id: 'r1', type: 'toolResult', payload: { ...of.payload, result: 'ok', success: true } }
}

describe('skill operations on file tools', () => {
  it.each([
    ['ReadFile', 'references/style.md', 'read', 'readSkill'],
    ['GrepFiles', '.craft/skills/release-notes', 'read', 'readSkill'],
    ['WriteFile', '.craft/skills/release-notes/SKILL.md', 'create', 'createdSkill'],
    ['EditFile', '.craft/skills/release-notes/scripts/run.py', 'update', 'updatedSkill'],
  ])('%s marked %s names the Skill and opens the skillFile it carries', (tool, path, skill, verb) => {
    const item = call(tool, path, marked(skill))
    const entry = toolEntry(item, result(item), undefined, false)

    expect(entry).toMatchObject({ verb, subject: 'release-notes', skill: { name: 'release-notes', file: SKILL_FILE }, details: [{ target: path, output: 'ok' }] })
  })

  it('keeps the Skill wording while running', () => {
    const entry = toolEntry(call('WriteFile', '.craft/skills/release-notes/SKILL.md', marked('create')), undefined, undefined, true)

    expect(entry).toMatchObject({ verb: 'createdSkill', subject: 'release-notes', live: true })
  })

  it('names the Skill but opens nothing when the mark carries no skillFile', () => {
    const item = call('ReadFile', '.craft/skills/release-notes/SKILL.md', { skill: 'read', skillName: 'release-notes' })

    expect(toolEntry(item, result(item), undefined, false)).toMatchObject({ verb: 'readSkill', skill: { name: 'release-notes', file: null } })
  })

  it('leaves an unmarked file call as a plain read', () => {
    const item = call('ReadFile', '.craft/skills/release-notes/SKILL.md')
    const entry = toolEntry(item, result(item), undefined, false)

    expect(entry).toMatchObject({ verb: 'read', subject: 'SKILL.md' })
    expect(entry?.skill).toBeUndefined()
  })
})
