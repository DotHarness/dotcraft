import type { JsonValue } from '@dotcraft/sdk/contracts'
import { describe, expect, it } from 'vitest'
import { applyEvent, emptyHistory, historyFromTurns, prependTurns, restoreEchoes, type ChatHistory } from './history'
import { buildTranscript as layout } from './transcript'

// The trusted source and presentation the server declares on first-party tools.
const CORE_PRESENTATIONS: Record<string, { presentationId: string; options?: { operation: string } }> = {
  ReadFile: { presentationId: 'core.read-file', options: { operation: 'read' } },
  EditFile: { presentationId: 'core.file-write', options: { operation: 'edit' } },
  CreatePlan: { presentationId: 'core.create-plan' },
}

function core(toolName: string) {
  return { toolName, source: { kind: 'CoreNative', sourceId: 'core-native' }, presentation: CORE_PRESENTATIONS[toolName] }
}

const buildTranscript = (history: ChatHistory) => layout(history).filter((entry) => entry.kind !== 'activity')

const item = (id: string, type: string, payload: { [key: string]: JsonValue }, extra: Record<string, unknown> = {}) => ({
  id,
  turnId: 't1',
  type,
  status: 'completed',
  payload,
  createdAt: '2026-10-03T08:00:00Z',
  completedAt: '2026-10-03T08:00:01Z',
  partial: false,
  ...extra,
})

const turn = (id: string, status: string) => ({ id, status, threadId: 'thread', startedAt: '2026-10-03T08:00:00Z' })

function apply(history: ChatHistory, ...events: Parameters<typeof applyEvent>[1][]): ChatHistory {
  return events.reduce(applyEvent, history)
}

describe('history catch-up', () => {
  it('replays buffered live events onto the reloaded pages without duplicates', () => {
    const pages = historyFromTurns([{ turn: turn('t1', 'running'), items: [item('a', 'userMessage', { text: 'go' })] }], null)
    const next = apply(
      pages,
      { kind: 'item', item: item('a', 'userMessage', { text: 'go' }) },
      { kind: 'item', item: item('m', 'agentMessage', { text: '' }, { status: 'started', completedAt: null }) },
      { kind: 'delta', itemId: 'm', turnId: 't1', itemType: 'agentMessage', delta: 'Hello ' },
      { kind: 'delta', itemId: 'm', turnId: 't1', itemType: 'agentMessage', delta: 'there' },
      { kind: 'item', item: item('m', 'agentMessage', { text: 'Hello there' }) },
      { kind: 'delta', itemId: 'm', turnId: 't1', itemType: 'agentMessage', delta: 'late' },
    )
    expect(buildTranscript(next)).toMatchObject([
      { kind: 'user', id: 't1/a', text: 'go', added: false },
      { kind: 'assistant', id: 't1/m', text: 'Hello there', streaming: false },
      { kind: 'thinking', quiet: false },
    ])
  })

  it('freezes a message seen mid-stream until its final snapshot, so captured deltas cannot double or garble it', () => {
    const pages = historyFromTurns(
      [{ turn: turn('t1', 'running'), items: [item('m', 'agentMessage', { text: 'Partial' }, { status: 'started', completedAt: null })] }],
      null,
    )
    const doubled = apply(pages, { kind: 'delta', itemId: 'm', turnId: 't1', itemType: 'agentMessage', delta: 'Partial' })
    expect(buildTranscript(doubled)).toMatchObject([
      { kind: 'assistant', id: 't1/m', text: 'Partial', streaming: true },
      { kind: 'thinking', quiet: false },
    ])

    const missed = apply(emptyHistory(), { kind: 'delta', itemId: 'n', turnId: 't1', itemType: 'agentMessage', delta: 'middle of a sentence' })
    expect(buildTranscript(missed)).toMatchObject([{ kind: 'thinking', quiet: true }])
    const done = apply(missed, { kind: 'item', item: item('n', 'agentMessage', { text: 'The start of the middle of a sentence' }) })
    expect(buildTranscript(done)).toMatchObject([{ kind: 'assistant', id: 't1/n', text: 'The start of the middle of a sentence', streaming: false }])
  })

  it('drops an optimistic echo once the server acknowledges its message, live or in reloaded pages', () => {
    const echoed: ChatHistory = { ...emptyHistory(), echoes: [{ clientId: 'c1', text: 'also run lint', added: true }] }
    const acknowledged = apply(echoed, {
      kind: 'item',
      item: item('u2', 'userMessage', { text: 'also run lint', deliveryMode: 'guidance', clientUserMessageId: 'c1' }),
    })
    expect(buildTranscript(acknowledged)).toMatchObject([{ kind: 'user', id: 't1/u2', text: 'also run lint', added: true }])

    const pages = historyFromTurns([{ turn: turn('t1', 'running'), items: [item('a', 'userMessage', { text: 'go', clientUserMessageId: 'c1' })] }], null)
    const restored = restoreEchoes(pages, [
      { clientId: 'c1', text: 'go', added: false },
      { clientId: 'c2', text: 'and then lint', added: true },
    ])
    expect(restored.echoes.map((echo) => echo.clientId)).toEqual(['c2'])
  })
})

describe('history pages', () => {
  it('puts older whole turns first, skips turns already loaded, and moves the cursor', () => {
    const t2 = { ...turn('t2', 'completed') }
    const head = historyFromTurns([{ turn: t2, items: [item('b', 'userMessage', { text: 'second' }, { turnId: 't2' })] }], 'older-1')
    const older = prependTurns(
      head,
      [
        { turn: turn('t1', 'completed'), items: [item('a', 'userMessage', { text: 'first' })] },
        { turn: t2, items: [item('b', 'userMessage', { text: 'second' }, { turnId: 't2' })] },
      ],
      null,
    )

    expect(older.turns.map((entry) => entry.id)).toEqual(['t1', 't2'])
    expect(older.items.map((entry) => entry.id)).toEqual(['a', 'b'])
    expect(older.olderCursor).toBeNull()
  })
})

describe('turn-scoped item ids', () => {
  it('pairs approvals and tool results with their own turn when ids repeat across turns', () => {
    const history: ChatHistory = {
      ...emptyHistory(),
      items: [
        item('item_001', 'toolCall', { ...core('EditFile'), callId: 'call_001', arguments: { path: 'src/a.ts' } }),
        item('item_002', 'toolResult', { ...core('EditFile'), callId: 'call_001', structuredContent: { changes: [{ additions: 3, deletions: 1 }] } }),
        item('item_003', 'approvalRequest', { requestId: 'approval_001', approvalType: 'shell', operation: 'npm test', target: 'repo' }),
        item('item_004', 'approvalResponse', { requestId: 'approval_001', approved: true, decision: 'accept' }),
        item('item_001', 'toolCall', { ...core('EditFile'), callId: 'call_001', arguments: { path: 'src/b.ts' } }, { turnId: 't2' }),
        item(
          'item_002',
          'toolResult',
          { ...core('EditFile'), callId: 'call_001', structuredContent: { changes: [{ additions: 7, deletions: 0 }] } },
          { turnId: 't2' },
        ),
        item('item_003', 'approvalRequest', { requestId: 'approval_001', approvalType: 'shell', operation: 'npm run lint', target: 'repo' }, { turnId: 't2' }),
        item('item_004', 'approvalResponse', { requestId: 'approval_001', approved: false, decision: 'decline' }, { turnId: 't2' }),
      ],
    }
    expect(buildTranscript(history)).toMatchObject([
      { kind: 'tool', id: 't1/item_001', verb: 'edited', subject: 'a.ts', added: 3, removed: 1 },
      { kind: 'notice', id: 't1/item_004', notice: 'allowedOnce', detail: 'npm test' },
      { kind: 'tool', id: 't2/item_001', verb: 'edited', subject: 'b.ts', added: 7, removed: 0 },
      { kind: 'notice', id: 't2/item_004', notice: 'rejected', detail: 'npm run lint' },
    ])
  })
})
