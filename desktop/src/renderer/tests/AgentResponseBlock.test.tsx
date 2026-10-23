import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { useUIStore } from '../stores/uiStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation, coreToolFields } from './testToolPresentation'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/conversation/McpAppView', () => ({
  McpAppView: ({ item }: { item: ConversationItem }) => (
    <div data-testid="mcp-app-view">MCP App: {item.toolName}</div>
  )
}))

function makeCreatePlanItem(
  id: string,
  title: string,
  createdAt: string
): ConversationItem {
  return withTestCorePresentation({
    id,
    type: 'toolCall',
    status: 'completed',
    toolCallId: `${id}-call`,
    toolName: 'CreatePlan', ...coreToolFields('CreatePlan'),
    arguments: {
      title,
      overview: `${title} overview`,
      plan: `# ${title}\n\n- keep visible`
    },
    success: true,
    createdAt
  }, CORE_TOOL_PRESENTATION_IDS.createPlan)
}

beforeEach(() => {
  useConversationStore.getState().reset()
  useConnectionStore.getState().reset()
  useThreadStore.getState().reset()
  useUIStore.getState().setShowThinkingContent(true)
})

afterEach(() => {
  vi.useRealTimers()
})

describe('AgentResponseBlock interactive card pinning', () => {
  beforeEach(() => {
    installDesktopApiMock({
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({})
        },
        shell: {
          openExternal: vi.fn().mockResolvedValue(undefined)
        }
      })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
  })

  function makeInteractiveCardItem(
    id: string,
    toolName: string,
    resourceUri: string,
    createdAt: string
  ): ConversationItem {
    return {
      id,
      type: 'toolCall',
      status: 'completed',
      toolCallId: `${id}-call`,
      toolName,
      arguments: {},
      success: true,
      createdAt,
      source: { kind: 'LegacyAppBinding', sourceId: 'workflow' },
      toolUi: { resourceUri, prefersBorder: true, domain: toolName }
    }
  }

  it('does not restore a private iframe without live authority', () => {
    const turn: ConversationTurn = {
      id: 'turn-pinned-card',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-06-10T11:30:00.000Z',
      completedAt: '2026-06-10T11:30:06.000Z',
      items: [
        {
          id: 'tool-read',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-read',
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-06-10T11:30:01.000Z'
        },
        makeInteractiveCardItem('card-board', 'ListBoardItems', 'ui://workflow/board.html', '2026-06-10T11:30:03.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'here is the board',
          createdAt: '2026-06-10T11:30:05.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(container.querySelector('.interactive-tool-view__frame')).toBeNull()
    expect(screen.getByText('here is the board')).toBeInTheDocument()
    expect(screen.getByText(/Worked for/)).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Worked for/ }))
    fireEvent.click(screen.getByRole('button', { name: /Called a tool, explored a file/ }))
    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
  })

  it('does not restore a private iframe from persisted items', () => {
    const turn: ConversationTurn = {
      id: 'turn-two-cards',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-06-10T11:40:00.000Z',
      completedAt: '2026-06-10T11:40:09.000Z',
      items: [
        makeInteractiveCardItem('card-first', 'ListBoardItems', 'ui://workflow/board.html', '2026-06-10T11:40:01.000Z'),
        {
          id: 'tool-read',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-read',
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-06-10T11:40:02.000Z'
        },
        makeInteractiveCardItem('card-latest', 'GetBoardItem', 'ui://workflow/item.html', '2026-06-10T11:40:05.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'opened the item',
          createdAt: '2026-06-10T11:40:07.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(container.querySelector('iframe[title="GetBoardItem"]')).toBeNull()
    expect(container.querySelector('iframe[title="ListBoardItems"]')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Worked for/ }))
    expect(container.querySelector('iframe[title="ListBoardItems"]')).toBeNull()
  })

  it('keeps a plan but not a private App Binding iframe', () => {
    const turn: ConversationTurn = {
      id: 'turn-plan-and-card',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-06-10T11:50:00.000Z',
      completedAt: '2026-06-10T11:50:09.000Z',
      items: [
        makeCreatePlanItem('plan-1', 'My Plan', '2026-06-10T11:50:01.000Z'),
        {
          id: 'tool-read',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-read',
          toolName: 'ReadFile', ...coreToolFields('ReadFile'),
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-06-10T11:50:02.000Z'
        },
        makeInteractiveCardItem('card-board', 'ListBoardItems', 'ui://workflow/board.html', '2026-06-10T11:50:04.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'plan and board',
          createdAt: '2026-06-10T11:50:07.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('My Plan')).toBeInTheDocument()
    expect(container.querySelector('.interactive-tool-view__frame')).toBeNull()
    expect(screen.queryByText('Read main.ts')).toBeNull()
  })
})
