import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyDeltas, applyEvent, emptyHistory, type ChatHistory, type HistoryDelta, type HistoryItem } from './history'
import { StreamBatch } from './streamBatch'

const delta = (itemId: string, text: string, turnId = 't1'): HistoryDelta => ({
  kind: 'delta', itemType: 'agentMessage', itemId, turnId, delta: text,
})

function item(id: string, turnId: string, extra: Partial<HistoryItem> = {}): HistoryItem {
  return { id, turnId, type: 'agentMessage', status: 'started', payload: { text: '' }, createdAt: '', completedAt: null, partial: false, ...extra }
}

afterEach(() => vi.useRealTimers())

describe('stream batch', () => {
  it('commits at the first delta deadline, not after the stream becomes quiet', () => {
    vi.useFakeTimers()
    const commits: { thread: string; deltas: HistoryDelta[] }[] = []
    const batch = new StreamBatch((thread, deltas) => commits.push({ thread, deltas }))
    batch.push('a', delta('m', 'first'))
    vi.advanceTimersByTime(40)
    batch.push('a', delta('m', ' second'))
    batch.push('b', delta('m', 'other'))
    vi.advanceTimersByTime(10)
    expect(commits).toEqual([{ thread: 'a', deltas: [delta('m', 'first second')] }])
    batch.flush('b')
    vi.advanceTimersByTime(100)
    expect(commits).toEqual([
      { thread: 'a', deltas: [delta('m', 'first second')] },
      { thread: 'b', deltas: [delta('m', 'other')] },
    ])
  })

  it('matches individual delta application across repeated ids, partial snapshots and completed items', () => {
    const history: ChatHistory = { ...emptyHistory(), items: [
      item('m', 't1'), item('m', 't2'), item('r', 't1', { type: 'reasoningContent' }),
      item('partial', 't1', { partial: true, payload: { text: 'snapshot' } }),
      item('done', 't1', { status: 'completed', payload: { text: 'final' } }),
    ] }
    const deltas = [delta('m', 'a'), delta('m', 'b', 't2'), delta('m', 'c'),
      { ...delta('r', 'thought'), itemType: 'reasoningContent' as const },
      delta('partial', 'duplicate'), delta('done', 'late'), delta('missing', 'lost'), delta('missing', 'also lost')]
    const expected = deltas.reduce(applyEvent, history)
    const actual = applyDeltas(history, deltas)
    expect(actual).toEqual(expected)
    expect(actual.items.map((entry) => entry.payload.text)).toEqual(['ac', 'b', 'thought', 'snapshot', 'final', ''])
    expect(history.items.map((entry) => entry.payload.text)).toEqual(['', '', '', 'snapshot', 'final'])
    expect(actual.items[3]).toBe(history.items[3])
    expect(actual.items[4]).toBe(history.items[4])
    expect(applyDeltas(actual, [delta('done', 'late')])).toBe(actual)
  })
})
