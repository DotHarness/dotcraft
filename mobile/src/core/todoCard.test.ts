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
    expect(todoCard(plan(['completed', 'in_progress', 'pending']), turn('inProgress'), [input(), ...todoWrite], true, null))
      .toMatchObject({ live: true })
  })

  it('shows an approved plan once its execution turn starts, before any update', () => {
    const planned = plan(['pending', 'pending'], 'Ship it')
    expect(todoCard(planned, turn('inProgress'), [input('implement')], true, null)).toBeNull()
    expect(todoCard(planned, turn('inProgress'), [input('implement')], true, 'implement')).toMatchObject({ live: true })
  })

  it('keeps unfinished work after a stopped turn without a live step', () => {
    expect(todoCard(plan(['completed', 'in_progress']), turn('cancelled'), [input(), ...todoWrite], false, null)).toMatchObject({ live: false })
  })

  it('hides a finished list once the turn ends', () => {
    expect(todoCard(plan(['completed', 'completed']), turn('inProgress'), [input(), ...todoWrite], true, null)).not.toBeNull()
    expect(todoCard(plan(['completed', 'completed']), turn('completed'), [input(), ...todoWrite], false, null)).toBeNull()
    expect(todoCard(plan(['completed', 'in_progress']), turn('completed'), [input(), ...todoWrite], false, null)).toBeNull()
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
