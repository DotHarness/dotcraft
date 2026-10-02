import { describe, expect, it } from 'vitest'
import { extractSources, hasFileChanges, rolledBackTurns, toSummaryItem, type SummaryItem } from './sources'

let counter = 0

function item(type: string, payload: Record<string, unknown>, turnId = 'turn-1', createdAt = '2026-10-01T10:00:00Z'): SummaryItem {
  counter += 1
  return { id: `item-${counter}`, turnId, type, createdAt, payload }
}

function call(callId: string, toolName: string, args: Record<string, unknown>, extra: Record<string, unknown> = {}): SummaryItem {
  return item('toolCall', { callId, toolName, arguments: args, ...extra })
}

function result(callId: string, toolName: string, text: string, success = true): SummaryItem {
  return item('toolResult', { callId, toolName, result: text, success })
}

describe('Summary sources', () => {
  it('lists every kind in one first-use order without duplicates within a kind', () => {
    const search = JSON.stringify({
      results: [
        { title: 'Guide', url: 'https://example.com/guide#intro' },
        { title: 'Other', url: 'https://www.example.org/a' }
      ]
    })
    const sources = extractSources([
      item('mcpToolCall', { callId: 'm1', server: 'tracker' }),
      call('c1', 'WebFetch', { url: 'https://example.com/guide' }),
      result('c1', 'WebFetch', '{"status":200}'),
      call('c3', 'ReadFile', { path: '/work/src/app/main.ts' }),
      result('c3', 'ReadFile', 'contents'),
      call('c2', 'WebSearch', { query: 'guide' }),
      result('c2', 'WebSearch', search),
      call('c4', 'ReadFile', { path: 'src/app/main.ts' }),
      result('c4', 'ReadFile', 'contents'),
      call('c6', 'SkillView', { name: 'release-notes' }),
      result('c6', 'SkillView', '# Release notes'),
      call('c7', 'SkillView', { name: 'release-notes' }),
      result('c7', 'SkillView', '# Release notes'),
      call('m3', 'list_issues', {}, { source: { kind: 'Mcp', sourceId: 'tracker' } }),
      item('mcpToolCall', { callId: 'm4', server: 'wiki' })
    ], '/work')

    expect(sources).toEqual([
      { kind: 'mcp', id: 'tracker', title: 'tracker', target: null },
      { kind: 'web', id: 'https://example.com/guide', title: 'Guide', target: 'https://example.com/guide' },
      { kind: 'file', id: 'src/app/main.ts', title: 'main.ts', target: '/work/src/app/main.ts' },
      { kind: 'web', id: 'https://www.example.org/a', title: 'Other', target: 'https://www.example.org/a' },
      { kind: 'skill', id: 'release-notes', title: 'release-notes', target: null },
      { kind: 'mcp', id: 'wiki', title: 'wiki', target: null }
    ])
  })

  it('keeps a row position and replaces a domain fallback with a title learned later', () => {
    const sources = extractSources([
      call('c1', 'WebFetch', { url: 'https://www.example.com/a' }),
      result('c1', 'WebFetch', '{}'),
      call('c2', 'WebFetch', { url: 'https://example.com/b' }),
      result('c2', 'WebFetch', '{}'),
      call('c3', 'WebSearch', { query: 'a' }),
      result('c3', 'WebSearch', JSON.stringify({ results: [{ title: 'Page A', url: 'https://www.example.com/a' }] })),
      call('c4', 'WebSearch', { query: 'a' }),
      result('c4', 'WebSearch', JSON.stringify({ results: [{ title: 'Renamed', url: 'https://www.example.com/a' }] }))
    ], null)

    expect(sources.map((source) => source.title)).toEqual(['Page A', 'example.com'])
  })

  it('ignores failed reads, missing skills, and failed searches', () => {
    const sources = extractSources([
      call('c1', 'ReadFile', { path: 'missing.ts' }),
      result('c1', 'ReadFile', 'File not found', false),
      call('c2', 'SkillView', { name: 'ghost' }),
      result('c2', 'SkillView', "Skill 'ghost' not found."),
      call('c3', 'WebSearch', { query: 'x' }),
      result('c3', 'WebSearch', JSON.stringify({ error: 'rate limited' })),
      call('c4', 'WebFetch', { url: 'https://example.com' }),
      result('c4', 'WebFetch', '{"error":"timeout"}', false)
    ], null)

    expect(sources).toEqual([])
  })

  it('accepts only source item types from the wire', () => {
    expect(toSummaryItem({ id: 'a', type: 'agentMessage', payload: {} }, 't1')).toBeNull()
    expect(toSummaryItem({ id: 'a', type: 'toolCall', payload: { callId: 'c' }, createdAt: '2026-10-01T00:00:00Z' }, 't1'))
      .toEqual({ id: 'a', turnId: 't1', type: 'toolCall', createdAt: '2026-10-01T00:00:00Z', payload: { callId: 'c' } })
  })
})

describe('Summary changes', () => {
  it('detects a thread that changed a file from its tool results', () => {
    const change = { kind: 'fileChange', changes: [{ path: 'a.ts', kind: 'update', additions: 1, deletions: 0 }] }
    expect(hasFileChanges([call('c1', 'ReadFile', { path: 'a.ts' }), result('c1', 'ReadFile', 'x')])).toBe(false)
    expect(hasFileChanges([item('toolResult', { callId: 'c2', toolName: 'EditFile', structuredContent: { kind: 'fileChange', changes: [] } })]))
      .toBe(false)
    expect(hasFileChanges([item('toolResult', { callId: 'c3', toolName: 'EditFile', structuredContent: change })])).toBe(true)
  })
})

describe('Summary rollback', () => {
  const items = [
    item('toolCall', {}, 'turn-1', '2026-10-01T10:00:01Z'),
    item('toolCall', {}, 'turn-2', '2026-10-01T10:05:01Z'),
    item('toolCall', {}, 'turn-3', '2026-10-01T10:10:01Z')
  ]

  it('drops every unknown turn when the turn page is complete', () => {
    const removed = rolledBackTurns(items, [{ id: 'turn-1', startedAt: '2026-10-01T10:00:00Z' }], true)
    expect([...removed].sort()).toEqual(['turn-2', 'turn-3'])
  })

  it('drops only turns newer than a partial page', () => {
    const removed = rolledBackTurns(items, [
      { id: 'turn-4', startedAt: '2026-10-01T10:11:00Z' },
      { id: 'turn-2', startedAt: '2026-10-01T10:05:00Z' }
    ], false)
    expect([...removed]).toEqual(['turn-3'])
  })
})
