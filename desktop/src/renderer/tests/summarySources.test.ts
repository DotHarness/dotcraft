import { describe, expect, it } from 'vitest'
import { changeTotals, diffTotals, extractSources, hasWebSearch, rolledBackTurns, sumChanges, toSummaryItem, workflowCallIds, type SummaryItem } from '../components/conversation/summary/sources'
import { changesByFolder } from '../components/conversation/summary/folderChanges'

let counter = 0

function item(type: string, payload: Record<string, unknown>, turnId = 'turn-1', createdAt = '2026-10-01T10:00:00Z'): SummaryItem {
  counter += 1
  return { id: `item-${counter}`, turnId, type, createdAt, payload }
}

const PRESENTATIONS: Record<string, Record<string, unknown>> = {
  WebSearch: { presentationId: 'core.web', options: { operation: 'search' } },
  WebFetch: { presentationId: 'core.web', options: { operation: 'fetch' } },
  ReadFile: { presentationId: 'core.read-file', options: { operation: 'read' } },
  SkillView: { presentationId: 'core.skill-view' }
}

function core(toolName: string): Record<string, unknown> {
  const presentation = PRESENTATIONS[toolName]
  return presentation ? { source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation } : {}
}

function call(callId: string, toolName: string, args: Record<string, unknown>, extra: Record<string, unknown> = {}): SummaryItem {
  return item('toolCall', { callId, toolName, ...core(toolName), arguments: args, ...extra })
}

function result(callId: string, toolName: string, text: string, success = true): SummaryItem {
  return item('toolResult', { callId, toolName, ...core(toolName), result: text, success })
}

describe('Summary sources', () => {
  it('lists every kind in one first-use order without duplicates within a kind', () => {
    const search = JSON.stringify({ results: [{ title: 'Guide', url: 'https://example.com/guide' }] })
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
      item('webSearch', { callId: 'w1', status: 'completed', results: [{ ref_id: 'turn0search0', url: 'https://example.org' }] }),
      item('mcpToolCall', { callId: 'm4', server: 'wiki' })
    ], '/work')

    expect(sources).toEqual([
      { kind: 'mcp', id: 'tracker', title: 'tracker', target: null },
      { kind: 'web', id: 'https://example.com/guide', title: 'example.com', target: 'https://example.com/guide' },
      { kind: 'skill', id: 'release-notes', title: 'release-notes', target: 'release-notes' },
      { kind: 'mcp', id: 'wiki', title: 'wiki', target: null }
    ])
  })

  it('lists the files, images, and skills the user provided, but not files the agent read', () => {
    const sources = extractSources([
      item('userMessage', {
        nativeInputParts: [
          { type: 'text', text: 'Compare these' },
          { type: 'fileRef', path: 'specs/summary.md', displayPath: 'specs/summary.md' },
          { type: 'localImage', path: '/work/.craft/attachments/images/shot.png', fileName: 'Screenshot.png' },
          { type: 'skillRef', name: 'design-prototype' }
        ],
        images: [{ path: '/work/.craft/attachments/images/shot.png', fileName: 'Screenshot.png' }]
      }),
      item('userMessage', { triggerKind: 'terminal', nativeInputParts: [{ type: 'fileRef', path: 'build.log' }] }),
      call('c1', 'ReadFile', { path: '/work/references/chatgpt-app' }),
      result('c1', 'ReadFile', 'listing'),
      call('c2', 'SkillView', { name: 'design-prototype' }),
      result('c2', 'SkillView', '# Design Prototype')
    ], '/work')

    expect(sources).toEqual([
      { kind: 'file', id: 'specs/summary.md', title: 'summary.md', target: 'specs/summary.md', image: false },
      { kind: 'file', id: '.craft/attachments/images/shot.png', title: 'Screenshot.png', target: '/work/.craft/attachments/images/shot.png', image: true },
      { kind: 'skill', id: 'design-prototype', title: 'design-prototype', target: 'design-prototype' }
    ])
  })

  it('maps each Workflow run to the call that started it', () => {
    expect([...workflowCallIds([
      call('w1', 'Workflow', { mode: 'name', name: 'review' }),
      result('w1', 'Workflow', '{"runId":"run_1","name":"review","status":"running"}'),
      call('w2', 'Workflow', {}),
      result('w2', 'Workflow', 'not json'),
      result('x', 'Exec', '{"runId":"run_2"}')
    ])]).toEqual([['run_1', 'w1']])
  })

  it('ignores failed reads and missing skills', () => {
    const sources = extractSources([
      call('c1', 'ReadFile', { path: 'missing.ts' }),
      result('c1', 'ReadFile', 'File not found', false),
      call('c2', 'SkillView', { name: 'ghost' }),
      result('c2', 'SkillView', "Skill 'ghost' not found.")
    ], null)

    expect(sources).toEqual([])
  })

  it('accepts only source item types from the wire', () => {
    expect(toSummaryItem({ id: 'a', type: 'agentMessage', payload: {} }, 't1')).toBeNull()
    expect(toSummaryItem({ id: 'u', type: 'userMessage', payload: {} }, 't1')?.type).toBe('userMessage')
    expect(toSummaryItem({ id: 'w', type: 'webSearch', payload: {} }, 't1')?.type).toBe('webSearch')
    expect(toSummaryItem({ id: 'a', type: 'toolCall', payload: { callId: 'c' }, createdAt: '2026-10-01T00:00:00Z' }, 't1'))
      .toEqual({ id: 'a', turnId: 't1', type: 'toolCall', createdAt: '2026-10-01T00:00:00Z', payload: { callId: 'c' } })
  })
})

describe('Summary web search', () => {
  it('marks only a thread that searched the web', () => {
    expect(hasWebSearch([item('webSearch', { callId: 'w', status: 'inProgress', query: '', action: null, results: null })])).toBe(true)
    expect(hasWebSearch([call('c1', 'WebSearch', { query: 'dotnet 10' })])).toBe(true)
    expect(hasWebSearch([
      call('c1', 'WebFetch', { url: 'https://example.com' }),
      result('c1', 'WebFetch', '{"status":200}'),
      call('c2', 'ReadFile', { path: 'a.ts' })
    ])).toBe(false)
  })
})

describe('Summary changes', () => {
  const edit = (callId: string, changes: unknown[]): SummaryItem =>
    item('toolResult', { callId, toolName: 'EditFile', structuredContent: { kind: 'fileChange', changes } })

  it('totals the changed lines of every file change in the thread per file', () => {
    expect(changeTotals([
      edit('c1', [{ path: 'a.ts', kind: 'update', additions: 4, deletions: 1 }]),
      call('c2', 'ReadFile', { path: 'a.ts' }),
      result('c2', 'ReadFile', 'x'),
      edit('c3', [{ path: 'a.ts', kind: 'update', additions: 2, deletions: 3 }]),
      edit('c4', [{ path: 'b.ts', kind: 'add', additions: 10, deletions: 0 }])
    ])).toEqual(new Map([
      ['a.ts', { additions: 6, deletions: 4 }],
      ['b.ts', { additions: 10, deletions: 0 }]
    ]))
    expect(changeTotals([call('c1', 'ReadFile', { path: 'a.ts' }), result('c1', 'ReadFile', 'x')])).toBeNull()
    expect(changeTotals([edit('c2', [])])).toBeNull()
  })

  it("uses a turn's live diff in place of its per-call totals", () => {
    const later = item('toolResult', {
      callId: 'c3',
      toolName: 'EditFile',
      structuredContent: { kind: 'fileChange', changes: [{ path: 'b.ts', kind: 'update', additions: 2, deletions: 0 }] }
    }, 'turn-2')
    const totals = changeTotals([
      edit('c1', [{ path: 'notes.md', kind: 'add', additions: 5, deletions: 0 }]),
      edit('c2', [{ path: 'notes.md', kind: 'update', additions: 1, deletions: 1 }]),
      later
    ], new Map([['turn-1', new Map([['notes.md', { additions: 5, deletions: 0 }]])]]))
    expect(sumChanges(totals!.values())).toEqual({ additions: 7, deletions: 0 })
  })

  it('counts added and removed lines inside diff hunks only, per file', () => {
    const diff = [
      'diff --git a/a.ts b/a.ts', '--- a/a.ts', '+++ b/a.ts', '@@ -1,2 +1,3 @@', ' keep', '-old', '+new', '+more',
      'diff --git a/D:/other/b.md b/D:/other/b.md', '--- /dev/null', '+++ b/D:/other/b.md', '@@ -0,0 +1 @@', '+hello'
    ].join('\n')
    expect(diffTotals(diff)).toEqual(new Map([
      ['a.ts', { additions: 2, deletions: 1 }],
      ['D:/other/b.md', { additions: 1, deletions: 0 }]
    ]))
    expect(diffTotals('').size).toBe(0)
  })

  it('groups changes under the project folder that contains them', () => {
    const changes = new Map([
      ['src/a.ts', { additions: 3, deletions: 1 }],
      ['D:/docs/page.tsx', { additions: 2, deletions: 0 }],
      ['D:\\docs\\style.css', { additions: 1, deletions: 4 }]
    ])
    expect(changesByFolder(changes, 'D:/app', ['D:/app', 'D:/docs'])).toEqual(new Map([
      ['D:/app', { additions: 3, deletions: 1 }],
      ['D:/docs', { additions: 3, deletions: 4 }]
    ]))
  })
})

describe('Summary rollback', () => {
  const items = [
    item('toolCall', {}, 'turn-1', '2026-10-01T10:00:01Z'),
    item('toolCall', {}, 'turn-2', '2026-10-01T10:05:01Z'),
    item('toolCall', {}, 'turn-3', '2026-10-01T10:10:01Z')
  ]

  it('drops every unknown turn for a complete page but only newer turns for a partial one', () => {
    const removed = rolledBackTurns(items, [{ id: 'turn-1', startedAt: '2026-10-01T10:00:00Z' }], true)
    expect([...removed].sort()).toEqual(['turn-2', 'turn-3'])

    const partial = rolledBackTurns(items, [
      { id: 'turn-4', startedAt: '2026-10-01T10:11:00Z' },
      { id: 'turn-2', startedAt: '2026-10-01T10:05:00Z' }
    ], false)
    expect([...partial]).toEqual(['turn-3'])
  })
})
