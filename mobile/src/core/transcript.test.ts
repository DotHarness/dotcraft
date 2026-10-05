import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { buildTranscript, thinkingStatus } from './transcript'

let clock = 0

function item(type: string, payload: Record<string, unknown>, extra: Partial<HistoryItem> = {}): HistoryItem {
  clock += 1
  return {
    id: `i${clock}`,
    turnId: 't1',
    type,
    status: 'completed',
    payload,
    createdAt: `2026-10-04T08:00:${String(clock).padStart(2, '0')}Z`,
    completedAt: `2026-10-04T08:00:${String(clock).padStart(2, '0')}Z`,
    partial: false,
    ...extra,
  }
}

function read(path: string, callId: string): HistoryItem[] {
  const presentation = { presentationId: 'core.read-file' }
  return [
    item('toolCall', { toolName: 'ReadFile', callId, arguments: { path }, presentation }),
    item('toolResult', { toolName: 'ReadFile', callId, result: 'ok', presentation }),
  ]
}

function history(items: HistoryItem[], status: string): ChatHistory {
  return {
    ...emptyHistory(),
    items,
    turns: [{ id: 't1', status, error: null, startedAt: '2026-10-04T08:00:00Z', completedAt: status === 'running' ? null : '2026-10-04T08:01:00Z' }],
  }
}

describe('turn layout', () => {
  it('folds a finished turn behind one Worked row, keeps the last plan in view, and offers Copy only on the final reply', () => {
    const items = [
      item('userMessage', { text: 'Plan the release' }),
      item('agentMessage', { text: 'Looking around first.', phase: 'commentary' }),
      ...read('a.ts', 'c1'),
      ...read('b.ts', 'c2'),
      item('toolCall', { toolName: 'CreatePlan', callId: 'p1', arguments: { plan: '# Release' } }),
      item('toolResult', { toolName: 'CreatePlan', callId: 'p1', result: 'ok', success: true }),
      item('agentMessage', { text: 'Here is the plan.', phase: 'final' }),
    ]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'plan', 'assistant'])
    const [, activity, , final] = transcript
    expect(activity).toMatchObject({ status: 'worked', startedAt: '2026-10-04T08:00:00Z' })
    expect(activity.kind === 'activity' && activity.children.map((entry) => entry.kind)).toEqual(['assistant', 'toolGroup'])
    expect(activity.kind === 'activity' && activity.children[1]).toMatchObject({ label: { kind: 'explored', count: 2 } })
    expect(final).toMatchObject({ text: 'Here is the plan.', copy: true })
    expect(activity.kind === 'activity' && activity.children[0]).toMatchObject({ copy: false })
  })

  it('keeps a running turn unfolded with its latest tool run ungrouped and its live reasoning shown', () => {
    const items = [
      item('userMessage', { text: 'Check the build' }),
      ...read('a.ts', 'c1'),
      ...read('b.ts', 'c2'),
      item('reasoningContent', { text: '**Reading the config**' }, { status: 'started', completedAt: null }),
    ]
    const transcript = buildTranscript(history(items, 'running'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'toolGroup', 'reasoning'])
    expect(transcript[1]).toMatchObject({ status: 'working', endedAt: null, children: [] })

    const reading = buildTranscript(history(items.slice(0, -1), 'running'))
    expect(reading.map((entry) => entry.kind)).toEqual(['user', 'activity', 'tool', 'tool'])
  })

  it('marks a stopped turn and drops reasoning that never finished', () => {
    const items = [item('userMessage', { text: 'Go' }), item('reasoningContent', { text: 'Hmm' }, { status: 'started', completedAt: null })]
    expect(buildTranscript(history(items, 'cancelled'))).toMatchObject([{ kind: 'user' }, { kind: 'activity', status: 'stopped' }])
  })
})

function edit(path: string, callId: string): HistoryItem[] {
  const structuredContent = { kind: 'fileChange', changes: [{ path, kind: 'update', additions: 2, deletions: 1 }] }
  return [
    item('toolCall', { toolName: 'EditFile', callId, arguments: { path } }),
    item('toolResult', { toolName: 'EditFile', callId, result: 'ok', structuredContent }),
  ]
}

describe('turn changes', () => {
  it('ends a completed turn that changed files with its changes on the final reply, outside Worked for', () => {
    const items = [item('userMessage', { text: 'Fix it' }), ...edit('a.ts', 'c1'), ...edit('b.ts', 'c2'), item('agentMessage', { text: 'Fixed.' })]
    const transcript = buildTranscript(history(items, 'completed'))
    expect(transcript.map((entry) => entry.kind)).toEqual(['user', 'activity', 'assistant'])
    expect(transcript[2]).toMatchObject({ copy: true, changes: { turnId: 't1', added: 4, removed: 2 } })
  })

  it('gives a completed turn without a reply its own changes entry, and a stopped or failed turn none', () => {
    const items = [item('userMessage', { text: 'Fix it' }), ...edit('a.ts', 'c1')]
    expect(buildTranscript(history(items, 'completed')).at(-1)).toMatchObject({ kind: 'changes', changes: { turnId: 't1' } })
    for (const status of ['cancelled', 'failed', 'running']) {
      expect(buildTranscript(history(items, status)).some((entry) => entry.kind === 'changes')).toBe(false)
    }
  })
})

describe('thinking status', () => {
  it('reads the last line of streaming reasoning without its emphasis', () => {
    expect(thinkingStatus('Plan first.\n\n**Checking the lockfile**\n<!-- note -->')).toBe('Checking the lockfile')
    expect(thinkingStatus('  ')).toBeNull()
  })
})
