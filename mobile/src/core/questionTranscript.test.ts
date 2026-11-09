import { describe, expect, it } from 'vitest'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { buildTranscript, createTranscriptProjector, type TranscriptEntry } from './transcript'

function item(id: string, type: string, payload: Record<string, unknown>, turnId = 'turn'): HistoryItem {
  return { id, turnId, type, payload, status: 'completed', createdAt: '2026-11-09T10:00:00Z', completedAt: '2026-11-09T10:00:01Z', partial: false }
}

function history(items: HistoryItem[]): ChatHistory {
  return { ...emptyHistory(), items, turns: [{ id: 'turn', status: 'completed', error: null }] }
}

function flatten(entries: TranscriptEntry[]): TranscriptEntry[] {
  return entries.flatMap((entry) => entry.kind === 'activity' || entry.kind === 'toolGroup' ? [entry, ...flatten(entry.children)] : [entry])
}

const questions = [
  { id: 'manager', question: 'Which package manager?', header: 'Package manager' },
  { id: 'schedule', question: 'When should CI run?', header: 'Schedule' },
]

const request = item('request', 'userInputRequest', { requestId: 'input', questions })
const response = item('response', 'userInputResponse', {
  requestId: 'input', response: { answers: { schedule: { answers: ['Every pull request'] }, manager: { answers: ['pnpm'] } } },
})

function projected(items: HistoryItem[]) {
  return flatten(buildTranscript(history(items))).filter((entry) => entry.kind === 'questions')
}

describe('answered question transcript', () => {
  it('pairs answers in request order and projects each question interaction once', () => {
    const call = item('call', 'toolCall', { callId: 'call', toolName: 'RequestUserInput', arguments: { questions }, source: { kind: 'CoreNative' }, presentation: { presentationId: 'core.request-user-input' } })
    const result = item('result', 'toolResult', { callId: 'call', result: JSON.stringify(response.payload.response), success: true })
    const entries = flatten(buildTranscript(history([call, request, response, result])))
    expect(entries.filter((entry) => entry.kind === 'questions')).toEqual([{
      kind: 'questions', id: 'turn/response', questions: [
        { id: 'manager', question: 'Which package manager?', answers: ['pnpm'], hidden: false },
        { id: 'schedule', question: 'When should CI run?', answers: ['Every pull request'], hidden: false },
      ],
    }])
    expect(entries.some((entry) => entry.kind === 'notice' || entry.kind === 'tool')).toBe(false)
  })

  it('marks unanswered questions and excludes private input from display data', () => {
    const current = item('request', 'userInputRequest', { requestId: 'input', questions: [
      { id: 'notes', question: 'Additional constraints?' },
      { id: 'private', question: 'Private note?', isSecret: true },
      { id: 'empty', header: 'Optional answer' },
    ] })
    const answered = item('response', 'userInputResponse', { requestId: 'input', response: { answers: {
      notes: { answers: ['user_note: Use the workspace configuration.'] }, private: { answers: ['user_note: example-private-answer'] },
    } } })
    const entries = projected([current, answered])
    expect(entries[0].questions).toEqual([
      { id: 'notes', question: 'Additional constraints?', answers: ['Use the workspace configuration.'], hidden: false },
      { id: 'private', question: 'Private note?', answers: [], hidden: true },
      { id: 'empty', question: 'Optional answer', answers: [], hidden: false },
    ])
  })

  it('keeps dismissed questions as unanswered and shows no completed row for a pending request', () => {
    expect(projected([request])).toEqual([])
    const dismissed = { ...response, payload: { requestId: 'input', response: { answers: {} } } }
    expect(projected([request, dismissed])[0].questions).toEqual(questions.map((question) => ({ id: question.id, question: question.question, answers: [], hidden: false })))
  })

  it('does not pair a response with a request from another turn or request id', () => {
    const unrelated = { ...request, turnId: 'older' }
    const different = { ...request, id: 'different', payload: { ...request.payload, requestId: 'other' } }
    const entries = projected([unrelated, different, response])
    expect(entries[0].questions).toEqual([
      { id: 'schedule', question: 'schedule', answers: [], hidden: true },
      { id: 'manager', question: 'manager', answers: [], hidden: true },
    ])
  })

  it('keeps answers hidden until request metadata loads and rechecks privacy when it changes', () => {
    const projector = createTranscriptProjector()
    const initial = history([response])
    expect(flatten(projector.project(initial)).find((entry) => entry.kind === 'questions')?.questions).toEqual([
      { id: 'schedule', question: 'schedule', answers: [], hidden: true },
      { id: 'manager', question: 'manager', answers: [], hidden: true },
    ])
    const restored = history([request, response])
    expect(projector.project(restored)).toEqual(buildTranscript(restored))
    expect(flatten(projector.project(restored)).find((entry) => entry.kind === 'questions')?.questions[0]).toMatchObject({ question: 'Which package manager?', answers: ['pnpm'], hidden: false })
    const replaced = history([{ ...request, payload: { ...request.payload, questions: [{ id: 'manager', question: 'Updated question?', isSecret: true }] } }, response])
    expect(flatten(projector.project(replaced)).find((entry) => entry.kind === 'questions')?.questions).toEqual([
      { id: 'manager', question: 'Updated question?', answers: [], hidden: true },
    ])
    expect(projector.project(replaced)).toEqual(buildTranscript(replaced))
  })
})
