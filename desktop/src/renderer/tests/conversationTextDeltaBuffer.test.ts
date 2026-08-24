import { describe, expect, it, vi } from 'vitest'
import { createConversationTextDeltaBuffer } from '../stores/conversationTextDeltaBuffer'

describe('conversation text delta buffer', () => {
  it('coalesces agent and reasoning deltas into one frame update', () => {
    vi.useFakeTimers()
    const commit = vi.fn()
    const buffer = createConversationTextDeltaBuffer(commit)

    buffer.queueAgentMessage('Hello', 10)
    buffer.queueAgentMessage(', world', 20)
    buffer.queueReasoning('Step 1. ')
    buffer.queueReasoning('Step 2.')

    expect(commit).not.toHaveBeenCalled()
    vi.advanceTimersByTime(16)
    expect(commit).toHaveBeenCalledTimes(1)
    expect(commit).toHaveBeenCalledWith({
      agentMessage: 'Hello, world',
      agentMessageLastDeltaAt: 20,
      reasoning: 'Step 1. Step 2.'
    })
    vi.useRealTimers()
  })

  it('flushes synchronously for lifecycle ordering and cancels on reset', () => {
    vi.useFakeTimers()
    const commit = vi.fn()
    const buffer = createConversationTextDeltaBuffer(commit)

    buffer.queueAgentMessage('final', 30)
    buffer.flush()
    expect(commit).toHaveBeenCalledTimes(1)

    buffer.queueReasoning('discarded')
    buffer.reset()
    vi.advanceTimersByTime(32)
    expect(commit).toHaveBeenCalledTimes(1)
    vi.useRealTimers()
  })
})
