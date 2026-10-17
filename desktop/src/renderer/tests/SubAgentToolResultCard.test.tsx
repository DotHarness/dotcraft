import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { render } from '@testing-library/react'
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

    expect(container.querySelector('[data-running-shimmer]')).toBeInTheDocument()
  })
})
