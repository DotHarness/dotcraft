import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { installDesktopApiMock } from './desktopApiMock'
import { withTestCorePresentation } from './testToolPresentation'

function coordinationItems(status: 'streaming' | 'completed'): ConversationItem[] {
  const toolCallId = 'async-message-call'
  const toolCall = withTestCorePresentation({
    id: 'async-message-tool-call',
    type: 'toolCall',
    status,
    toolCallId,
    toolName: 'SendUserMessageAsync',
    arguments: { message: 'Which target should I use?' },
    createdAt: '2026-08-31T12:00:00.000Z'
  }, CORE_TOOL_PRESENTATION_IDS.sendUserMessageAsync)
  const toolResult = withTestCorePresentation({
    id: 'async-message-tool-result',
    type: 'toolResult',
    status: 'completed',
    toolCallId,
    toolName: 'SendUserMessageAsync',
    result: '{"accepted":true}',
    success: true,
    createdAt: '2026-08-31T12:00:01.000Z',
    completedAt: '2026-08-31T12:00:01.000Z'
  }, CORE_TOOL_PRESENTATION_IDS.sendUserMessageAsync)

  return [
    toolCall,
    {
      id: 'async-agent-message',
      type: 'agentMessage',
      status: 'completed',
      deliveryMode: 'async',
      text: 'Which target should I use?',
      createdAt: '2026-08-31T12:00:00.500Z',
      completedAt: '2026-08-31T12:00:00.500Z'
    },
    toolResult
  ]
}

function renderTurn(turn: ConversationTurn, isRunning: boolean): void {
  render(
    <LocaleProvider>
      <AgentResponseBlock
        turn={turn}
        isRunning={isRunning}
        historicalToolContentMode="full"
      />
    </LocaleProvider>
  )
}

describe('asynchronous user message rendering', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) }
    })
  })

  it.each([
    ['live', 'running', 'streaming', true],
    ['restored', 'completed', 'completed', false]
  ] as const)('shows the async message and hides its %s tool lifecycle', (
    _scenario,
    turnStatus,
    toolStatus,
    isRunning
  ) => {
    renderTurn({
      id: `turn-${turnStatus}`,
      threadId: 'thread-1',
      status: turnStatus,
      startedAt: '2026-08-31T12:00:00.000Z',
      completedAt: turnStatus === 'completed' ? '2026-08-31T12:00:02.000Z' : undefined,
      items: coordinationItems(toolStatus)
    }, isRunning)

    expect(screen.getByText('Which target should I use?')).toBeInTheDocument()
    expect(screen.queryByText(/SendUserMessageAsync/)).toBeNull()
    expect(screen.queryByText(/accepted/)).toBeNull()
  })
})
