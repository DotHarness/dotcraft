import { act } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'
import { installDesktopApiMock } from './desktopApiMock'
import { useToolCallRevealStore } from '../stores/toolCallRevealStore'

interface CoreFixturePresentation {
  presentationId: string
  options?: Record<string, unknown>
}

const SUBAGENT_SPAWN: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'spawn' }
}

function makeToolCallItem(
  id: string,
  toolCallId: string,
  toolName: string,
  createdAt: string,
  presentation?: CoreFixturePresentation
): ConversationItem {
  const item: ConversationItem = {
    id,
    type: 'toolCall',
    status: 'completed',
    toolCallId,
    toolName,
    arguments: {},
    success: true,
    createdAt
  }
  return presentation
    ? withTestCorePresentation(item, presentation.presentationId, presentation.options)
    : item
}


function renderBlock(turn: ConversationTurn): string {
  const { container } = render(<LocaleProvider><AgentResponseBlock turn={turn} /></LocaleProvider>)
  return container.textContent ?? ''
}

describe('AgentResponseBlock subagent transcript rendering', () => {
  beforeEach(() => {
    installDesktopApiMock({
        settings: {
          get: async () => ({ locale: 'en' })
        }
      })
  })

  it('does not render the old inline subagent progress summary between SpawnAgent and later tool calls', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:00:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'SpawnAgent', '2026-04-18T10:00:01.000Z', SUBAGENT_SPAWN),
        makeToolCallItem('tool-2', 'call-2', 'FollowupTool', '2026-04-18T10:00:02.000Z')
      ]
    }

    const text = renderBlock(turn)
    const spawnIndex = text.indexOf('Spawned agent')
    const followupIndex = text.indexOf('Called FollowupTool')

    expect(spawnIndex).toBeGreaterThan(-1)
    expect(followupIndex).toBeGreaterThan(-1)
    expect(spawnIndex).toBeLessThan(followupIndex)
    expect(text).not.toContain('SubAgent completed')
  })

  it('keeps SpawnAgent output compact when no follow-up tools exist', () => {
    const turn: ConversationTurn = {
      id: 'turn-2',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:01:00.000Z',
      items: [
        makeToolCallItem('tool-3', 'call-3', 'SpawnAgent', '2026-04-18T10:01:01.000Z', SUBAGENT_SPAWN)
      ]
    }

    const text = renderBlock(turn)
    expect(text).toContain('Spawned agent')
    expect(text).not.toContain('SubAgent completed')
  })

  it('renders grouped SpawnAgent calls as inline chips that open each subagent', () => {
    const turn: ConversationTurn = {
      id: 'turn-spawn-group',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:01:00.000Z',
      items: [
        {
          id: 'spawn-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'spawn-call-1',
          toolName: 'SpawnAgent',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
          presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
          arguments: {
            agentPrompt: 'Inspect Settings diagnostics output',
            agentNickname: 'Kepler',
            agentRole: 'explorer'
          },
          result: JSON.stringify({
            childThreadId: 'thread_kepler',
            agentNickname: 'Kepler',
            agentRole: 'explorer',
            status: 'running'
          }),
          success: true,
          createdAt: '2026-04-18T10:01:01.000Z'
        },
        {
          id: 'spawn-2',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'spawn-call-2',
          toolName: 'SpawnAgent',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
          presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
          arguments: {
            agentPrompt: 'Review AppServer credential redaction',
            agentNickname: 'Lagrange',
            agentRole: 'explorer'
          },
          result: JSON.stringify({
            childThreadId: 'thread_lagrange',
            agentNickname: 'Lagrange',
            agentRole: 'explorer',
            status: 'running'
          }),
          success: true,
          createdAt: '2026-04-18T10:01:02.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByTestId('subagent-chips')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Kepler/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Lagrange/ })).toBeInTheDocument()
    // The prompt rides the chip's tooltip rather than taking a line of its own.
    expect(screen.queryByText('Inspect Settings diagnostics output')).toBeNull()
    expect(screen.queryByText(/childThreadId/)).toBeNull()
  })

  it('shows a folded spawn chip when a reveal targets it', () => {
    const names = ['Kepler', 'Lagrange', 'Noether', 'Hopper']
    const turn: ConversationTurn = {
      id: 'turn-spawn-many',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:01:00.000Z',
      items: names.map((name, index) => ({
        id: `spawn-${index + 1}`,
        type: 'toolCall',
        status: 'completed',
        toolCallId: `spawn-call-${index + 1}`,
        toolName: 'SpawnAgent',
        source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
        presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
        arguments: { agentPrompt: `Task for ${name}`, agentNickname: name },
        result: JSON.stringify({ childThreadId: `thread_${name}`, agentNickname: name, status: 'running' }),
        success: true,
        createdAt: `2026-04-18T10:01:0${index + 1}.000Z`
      }))
    }
    useToolCallRevealStore.setState({ request: null, target: null })
    render(<LocaleProvider><AgentResponseBlock turn={turn} /></LocaleProvider>)
    expect(screen.queryByRole('button', { name: /Hopper/ })).toBeNull()

    act(() => {
      useToolCallRevealStore.setState({ target: { threadId: 'thread-1', turnId: 'turn-spawn-many', callId: 'spawn-call-4', nonce: 1 } })
    })

    expect(screen.getByRole('button', { name: /Hopper/ })).toBeInTheDocument()
  })

  it('hides successful SubAgent messages but preserves targeted failures', () => {
    const hiddenMessage = makeToolCallItem('message', 'message-call', 'SendMessage', '2026-05-03T10:00:02.000Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'sendMessage' }
    })
    hiddenMessage.result = JSON.stringify({ status: 'sent', agentNickname: 'Reviewer' })

    const failedMessage = makeToolCallItem('failed-message', 'failed-message-call', 'SendMessage', '2026-05-03T10:00:03.000Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'sendMessage' }
    })
    failedMessage.success = false
    failedMessage.arguments = { target: '/root/reviewer' }
    failedMessage.result = JSON.stringify({ status: 'failed', agentNickname: 'Reviewer', error: 'Send failed' })

    const text = renderBlock({
      id: 'turn-hidden-controls',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [hiddenMessage, failedMessage]
    })

    expect(text).not.toContain('Sent message')
    expect(text).toContain('Reviewer failed')
  })

})
