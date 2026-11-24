import { describe, expect, it } from 'vitest'
import { emptyHistory, type HistoryItem, type HistoryTurn } from './history'
import { todoCard, todoPlanOf, withTodoCard, type TodoPlan } from './todoCard'
import { buildTranscript } from './transcript'
import type { TranscriptEntry } from './transcriptTypes'

const presentation = { presentationId: 'core.todo', options: { operation: 'todoWrite' } }

function item(type: string, payload: Record<string, unknown>): HistoryItem {
  return { id: `${type}-${String(payload.callId ?? 'input')}`, turnId: 't1', type, status: 'completed', payload, createdAt: '2026-01-01T00:00:00Z', completedAt: null, partial: false }
}

const input = (clientUserMessageId?: string) => item('userMessage', { text: 'Do it', clientUserMessageId })
const todoWrite = [
  item('toolCall', { toolName: 'TodoWrite', callId: 'w1', presentation, source: { kind: 'CoreNative', sourceId: 'core-native' }, arguments: {} }),
  item('toolResult', { toolName: 'TodoWrite', callId: 'w1', presentation, source: { kind: 'CoreNative', sourceId: 'core-native' }, success: true, result: 'ok' }),
]

function plan(statuses: string[], title = ''): TodoPlan {
  return todoPlanOf({ title, todos: statuses.map((status, index) => ({ id: `s${index}`, content: `Step ${index}`, status, activeForm: `Doing step ${index}` })) })!
}

const turn = (status: string): HistoryTurn => ({ id: 't1', status, error: null })

describe('to-do card', () => {
  it('shows a list the running turn updates, with its step live', () => {
    expect(todoCard(plan(['completed', 'in_progress', 'pending']), turn('inProgress'), [input(), ...todoWrite], false, null))
      .toMatchObject({ live: true })
  })

  it('keeps an approved plan visible without a write until background work ends', () => {
    const planned = plan(['pending', 'pending'], 'Ship it')
    const items = [input('implement')]
    expect(todoCard(planned, turn('inProgress'), items, false, null)).toBeNull()
    expect(todoCard(planned, turn('inProgress'), items, false, 'implement')).toMatchObject({ live: true })
    expect(todoCard(planned, turn('completed'), items, true, 'implement')).toMatchObject({ live: true })
    expect(todoCard(plan(['completed'], 'Ship it'), turn('completed'), items, true, 'implement')).not.toBeNull()
    expect(todoCard(planned, turn('completed'), items, false, 'implement')).toBeNull()
  })

  it('keeps unfinished work after a stopped turn without a live step', () => {
    expect(todoCard(plan(['completed', 'in_progress']), turn('cancelled'), [input(), ...todoWrite], true, null)).toMatchObject({ live: false })
  })

  it('keeps a checked-off list visible while the turn runs', () => {
    expect(todoCard(plan(['completed', 'completed']), turn('inProgress'), [input(), ...todoWrite], false, null)).not.toBeNull()
  })

  it('hides an unfinished list once its turn completes successfully', () => {
    expect(todoCard(plan(['completed', 'in_progress', 'pending']), turn('completed'), [input(), ...todoWrite], false, null)).toBeNull()
  })

  it('does not carry unfinished tasks into a turn that has not updated them', () => {
    expect(todoCard(plan(['completed', 'in_progress', 'pending']), turn('inProgress'), [input()], false, null)).toBeNull()
    expect(todoCard(plan(['completed', 'in_progress', 'pending']), turn('completed'), [input()], false, null)).toBeNull()
  })

  it('sits above the working line, or under the reply above Copy', () => {
    const card = { todos: plan(['in_progress']).todos, live: true }
    const user: TranscriptEntry = { kind: 'user', id: 'u', text: 'Do it', segments: [], images: [], added: false, origin: null, goal: false }
    const reply: TranscriptEntry = { kind: 'assistant', id: 'r', text: 'Done', streaming: false, phase: 'final', at: '', copy: true, changes: [] }
    const working: TranscriptEntry = { kind: 'thinking', id: 'w', working: true, heading: null }

    expect(withTodoCard([user, working], card).map((entry) => entry.kind)).toEqual(['user', 'todos', 'thinking'])
    expect(withTodoCard([user, reply, { ...working, working: false }], card)[1]).toMatchObject({ kind: 'assistant', todos: card })
    expect(withTodoCard([user], card)).toEqual([user])
  })

  it('leaves TodoWrite calls out of the transcript', () => {
    const transcript = buildTranscript({ ...emptyHistory(), items: [input(), ...todoWrite], turns: [turn('completed')] })
    expect(transcript.some((entry) => entry.kind === 'tool' || entry.kind === 'toolGroup')).toBe(false)
  })
})
