import { beforeEach, describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'
import { installDesktopApiMock } from './desktopApiMock'

interface CoreFixturePresentation {
  presentationId: string
  options?: Record<string, unknown>
}

const SUBAGENT_SPAWN: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'spawn' }
}
const SUBAGENT_FOLLOWUP: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'followupTask' }
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

  it('renders a single FollowupTask as an ungrouped Updated agent row', () => {
    const item = makeToolCallItem(
      'followup-1',
      'followup-call-1',
      'FollowupTask',
      '2026-05-03T09:59:00.000Z',
      SUBAGENT_FOLLOWUP
    )
    item.arguments = { target: '/root/reviewer', message: 'Check the updated tests' }
    item.result = JSON.stringify({
      agentPath: '/root/reviewer',
      agentNickname: 'Reviewer',
      agentRole: 'explorer',
      status: 'running'
    })

    const text = renderBlock({
      id: 'turn-single-followup',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-03T09:58:00.000Z',
      items: [item]
    })

    expect(text).toContain('Updated Reviewer')
    expect(text).not.toContain('Updated 1 agents')
  })

  it('groups consecutive FollowupTask calls as inline chips', () => {
    const items = ['Reviewer', 'Researcher'].map((name, index) => {
      const item = makeToolCallItem(
        `followup-${index}`,
        `followup-call-${index}`,
        'FollowupTask',
        `2026-05-03T10:00:0${index}.000Z`,
        SUBAGENT_FOLLOWUP
      )
      item.arguments = {
        target: `/root/${name.toLowerCase()}`,
        message: `Update ${name} instructions`
      }
      item.result = JSON.stringify({
        agentPath: `/root/${name.toLowerCase()}`,
        agentNickname: name,
        agentRole: 'explorer',
        status: 'running'
      })
      return item
    })

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={{
          id: 'turn-followup-group',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: '2026-05-03T10:00:00.000Z',
          items
        }} />
      </LocaleProvider>
    )

    expect(screen.getByTestId('subagent-chips')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Reviewer/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Researcher/ })).toBeInTheDocument()
  })

  it('hides a pending WaitAgent row from the transcript', () => {
    const turn: ConversationTurn = {
      id: 'turn-wait-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [
        {
          id: 'tool-wait',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-wait',
          toolName: 'WaitAgent',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
          presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
          arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
          createdAt: '2026-05-03T10:00:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(container.textContent).not.toContain('Waiting for')
  })

  it('hides historical WaitAgent calls when toolResult is missing', () => {
    const turn: ConversationTurn = {
      id: 'turn-wait-history',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [
        {
          id: 'tool-wait-history',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-wait-history',
          toolName: 'WaitAgent',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
          presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
          arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
          createdAt: '2026-05-03T10:00:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(container.textContent).not.toContain('Wait')
  })

  it('hides WaitAgent failures and normal SubAgent controls but preserves targeted failures', () => {
    const hiddenWait = makeToolCallItem('wait', 'wait-call', 'WaitAgent', '2026-05-03T10:00:01.000Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'wait' }
    })
    hiddenWait.result = JSON.stringify({ status: 'timeout', timedOut: true })

    const hiddenFailedWait = makeToolCallItem('failed-wait', 'failed-wait-call', 'WaitAgent', '2026-05-03T10:00:01.500Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'wait' }
    })
    hiddenFailedWait.success = false
    hiddenFailedWait.arguments = { timeoutMs: 1000 }
    hiddenFailedWait.result = 'timeoutMs must be at least 15000. (Parameter \'timeoutMs\')'

    const hiddenMessage = makeToolCallItem('message', 'message-call', 'SendMessage', '2026-05-03T10:00:02.000Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'sendMessage' }
    })
    hiddenMessage.result = JSON.stringify({ status: 'sent', agentNickname: 'Reviewer' })

    const failedClose = makeToolCallItem('close', 'close-call', 'CloseAgent', '2026-05-03T10:00:03.000Z', {
      presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
      options: { operation: 'close' }
    })
    failedClose.success = false
    failedClose.arguments = { target: '/root/reviewer' }
    failedClose.result = JSON.stringify({ status: 'failed', agentNickname: 'Reviewer', error: 'Close failed' })

    const text = renderBlock({
      id: 'turn-hidden-controls',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-03T10:00:00.000Z',
      items: [hiddenWait, hiddenFailedWait, hiddenMessage, failedClose]
    })

    expect(text).not.toContain('Wait timed out')
    expect(text).not.toContain('agent failed')
    expect(text).not.toContain('timeoutMs must be at least 15000')
    expect(text).not.toContain('Sent message')
    expect(text).toContain('Reviewer failed')
  })

})
