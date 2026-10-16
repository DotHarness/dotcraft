import { describe, expect, it } from 'vitest'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { getActivityElapsedMs, getTerminalWake, getTurnActivityState } from '../components/conversation/turnActivityModel'

const start = '2026-01-01T12:00:00Z'
const finalStart = '2026-01-01T12:00:12Z'
const end = '2026-01-01T12:00:20Z'
const commentary: ConversationItem = { id: 'progress', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'Progress', createdAt: start }
const final: ConversationItem = { id: 'answer', type: 'agentMessage', status: 'streaming', phase: 'final', text: 'Partial answer', createdAt: finalStart }
const turn: ConversationTurn = { id: 'turn', threadId: 'thread', status: 'running', startedAt: start, items: [] }

describe('turn activity lifecycle', () => {
  it('waits for explicit final phase while running and freezes work time when it begins', () => {
    expect(getTurnActivityState(turn, [commentary], true)).toMatchObject({ status: 'working', finalIndex: -1 })
    const state = getTurnActivityState(turn, [commentary, final], true)
    expect(state).toMatchObject({ status: 'worked', finalIndex: 1, completedAt: finalStart })
    expect(getActivityElapsedMs(start, state.completedAt)).toBe(12000)
  })

  it('uses the full stop timestamp while retaining the final answer boundary', () => {
    const state = getTurnActivityState({ ...turn, status: 'cancelled', completedAt: end }, [commentary, final], false)
    expect(state).toMatchObject({ status: 'stopped', finalIndex: 1, completedAt: end })
    expect(getActivityElapsedMs(start, state.completedAt)).toBe(20000)
  })

  it('supports completed providers without phase metadata but never treats live unphased text as final', () => {
    const unphased = { ...final, phase: undefined }
    expect(getTurnActivityState(turn, [unphased], true).finalIndex).toBe(-1)
    expect(getTurnActivityState({ ...turn, status: 'completed' }, [unphased], false).finalIndex).toBe(0)
    expect(getTurnActivityState({ ...turn, status: 'failed' }, [final], false).status).toBeNull()
  })

  it('does not invent elapsed time for missing, invalid or reversed timestamps', () => {
    expect(getActivityElapsedMs(undefined, end)).toBeUndefined()
    expect(getActivityElapsedMs(start, undefined)).toBeUndefined()
    expect(getActivityElapsedMs('invalid', end)).toBeUndefined()
    expect(getActivityElapsedMs(end, start)).toBeUndefined()
    expect(getActivityElapsedMs(start, start)).toBe(0)
  })

  it('reads the background command and its outcome only for a turn its notice started', () => {
    const notice: ConversationItem = { id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm run build', triggerStatus: 'failed', text: 'Background command finished (exit code 2): npm run build', createdAt: start }
    const prompt: ConversationItem = { id: 'prompt', type: 'userMessage', status: 'completed', text: 'Run the build', createdAt: start }
    expect(getTerminalWake({ ...turn, items: [notice, commentary] })).toEqual({ command: 'npm run build', status: 'failed' })
    expect(getTerminalWake({ ...turn, items: [{ ...notice, triggerLabel: undefined, triggerStatus: undefined }] })).toEqual({ command: '', status: undefined })
    expect(getTerminalWake({ ...turn, items: [prompt, { ...notice, deliveryMode: 'guidance' }] })).toBeNull()
    expect(getTerminalWake({ ...turn, items: [{ ...notice, deliveryMode: 'guidance' }] })).toBeNull()
    expect(getTerminalWake({ ...turn, items: [commentary] })).toBeNull()
  })
})
