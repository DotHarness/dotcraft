import { describe, expect, it, vi } from 'vitest'
import { resolveCustomCommandExecution } from '../utils/customCommandExecution'
import type { CustomCommandInfo } from '../hooks/useCustomCommandCatalog'

const CUSTOM_COMMANDS: CustomCommandInfo[] = [
  {
    name: '/code-review',
    aliases: ['/cr'],
    description: 'Review changed files',
    category: 'custom',
    requiresAdmin: false
  }
]

describe('resolveCustomCommandExecution', () => {
  it('passes through normal text', async () => {
    const sendRequest = vi.fn()
    const result = await resolveCustomCommandExecution({
      text: 'hello world',
      threadId: 'thread-1',
      commands: CUSTOM_COMMANDS,
      sendRequest
    })
    expect(result.matchedCustomCommand).toBe(false)
    expect(result.shouldSendTurn).toBe(true)
    expect(result.textForTurn).toBe('hello world')
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('expands matching custom commands', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      handled: true,
      expandedPrompt: 'Please review the recent diff'
    })
    const result = await resolveCustomCommandExecution({
      text: '/code-review fast',
      threadId: 'thread-1',
      commands: CUSTOM_COMMANDS,
      sendRequest
    })
    expect(sendRequest).toHaveBeenCalledWith('command/execute', {
      threadId: 'thread-1',
      command: '/code-review',
      arguments: ['fast']
    })
    expect(result.matchedCustomCommand).toBe(true)
    expect(result.shouldSendTurn).toBe(true)
    expect(result.textForTurn).toBe('Please review the recent diff')
  })

  it('supports alias matching', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      handled: true,
      expandedPrompt: 'Alias expansion text'
    })
    const result = await resolveCustomCommandExecution({
      text: '/cr',
      threadId: 'thread-1',
      commands: CUSTOM_COMMANDS,
      sendRequest
    })
    expect(sendRequest).toHaveBeenCalledWith('command/execute', {
      threadId: 'thread-1',
      command: '/cr',
      arguments: []
    })
    expect(result.shouldSendTurn).toBe(true)
    expect(result.textForTurn).toBe('Alias expansion text')
  })

  it('returns message-only outcome without follow-up turn', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      handled: true,
      message: 'Command executed',
      isMarkdown: true,
      expandedPrompt: null
    })
    const result = await resolveCustomCommandExecution({
      text: '/code-review',
      threadId: 'thread-1',
      commands: CUSTOM_COMMANDS,
      sendRequest
    })
    expect(result.matchedCustomCommand).toBe(true)
    expect(result.shouldSendTurn).toBe(false)
    expect(result.message).toBe('Command executed')
    expect(result.isMarkdown).toBe(true)
  })

  it('surfaces session reset thread metadata', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      handled: true,
      sessionReset: true,
      createdLazily: true,
      thread: {
        id: 'thread-new'
      },
      expandedPrompt: 'run on new thread'
    })
    const result = await resolveCustomCommandExecution({
      text: '/code-review',
      threadId: 'thread-1',
      commands: CUSTOM_COMMANDS,
      sendRequest
    })
    expect(result.sessionResetThreadId).toBe('thread-new')
    expect(result.sessionResetThreadSummary?.id).toBe('thread-new')
    expect(result.createdLazily).toBe(true)
    expect(result.textForTurn).toBe('run on new thread')
  })
})
