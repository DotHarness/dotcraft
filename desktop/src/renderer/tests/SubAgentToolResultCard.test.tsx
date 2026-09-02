import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import type { ConversationItem } from '../types/conversation'

function renderWithLocale(node: JSX.Element): ReturnType<typeof render> {
  return render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('ToolCallCard subagent result rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      appServer: {
        sendRequest: vi.fn(async () => ({}))
      }
    })
  })

  it('renders SpawnAgent result with role, external profile, and prompt without raw JSON', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SpawnAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
      toolCallId: 'call-1',
      arguments: {
        agentPrompt: 'Create hatch pet',
        agentNickname: 'Popper',
        agentRole: 'worker',
        profile: 'cursor-cli'
      },
      result: JSON.stringify({
        childThreadId: 'thread_child',
        agentNickname: 'Popper',
        agentRole: 'worker',
        profileName: 'cursor-cli',
        runtimeType: 'cli-oneshot',
        status: 'running'
      }),
      success: true,
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" />)

    expect(container.querySelector('span[style*="width: 7px"]')).toBeNull()
    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
  })

  it('renders streaming SpawnAgent from argument preview without raw JSON', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-streaming',
      type: 'toolCall',
      status: 'streaming',
      toolName: 'SpawnAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
      toolCallId: 'call-streaming',
      argumentsPreview: '{"agentPrompt":"Review the API surface","agentNickname":"Reviewer"}',
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" turnRunning />)

    expect(container.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
  })

  it('folds WaitAgent message behind an expandable result body', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
      toolCallId: 'call-2',
      arguments: { childThreadId: 'thread_child' },
      result: JSON.stringify({
        childThreadId: 'thread_child',
        agentNickname: 'Reviewer',
        profileName: 'codex',
        status: 'completed',
        message: 'Detailed child agent result'
      }),
      success: true,
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" />)

    expect(container.querySelector('.selectable')).toBeNull()
    const button = screen.getByTestId('tool-row')
    fireEvent.click(button)
    expect(container.querySelector('.selectable')).toBeInTheDocument()
  })

  it('renders running WaitAgent with the shared running gradient label', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-running-wait',
      type: 'toolCall',
      status: 'started',
      toolName: 'WaitAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
      toolCallId: 'call-running-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
    expect(document.querySelector('.animate-spin-custom')).toBeNull()
  })

  it('keeps WaitAgent running after toolCall completion until the tool result arrives', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-pending-wait-result',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
      toolCallId: 'call-pending-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" turnRunning />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('does not show a stale running state for historical WaitAgent calls without a result', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-historical-missing-result',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
      toolCallId: 'call-historical-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" />)

    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
  })

  it('renders WaitAgent timeout as a wait timeout rather than a subagent failure', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-timeout',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WaitAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'wait' } },
      toolCallId: 'call-timeout',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      result: JSON.stringify({
        childThreadId: 'thread_child',
        agentNickname: 'Reviewer',
        status: 'timeout',
        message: 'Wait timed out.'
      }),
      success: true,
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard threadId="thread-1" item={item} turnId="turn-1" />)

    expect(screen.getByTestId('tool-row')).toBeInTheDocument()
    expect(document.querySelector('[data-tone="error"]')).toBeNull()
  })
})
