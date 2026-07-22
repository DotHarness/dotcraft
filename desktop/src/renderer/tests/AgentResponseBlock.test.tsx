import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { useUIStore } from '../stores/uiStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

vi.mock('../components/conversation/McpAppView', () => ({
  hasAvailableMcpApp: (item: ConversationItem) =>
    item.type === 'mcpToolCall' && item.status === 'completed' && item.mcpAppAvailable === true,
  McpAppView: ({ item }: { item: ConversationItem }) => (
    <div data-testid="mcp-app-view">MCP App: {item.toolName}</div>
  )
}))

interface CoreFixturePresentation {
  presentationId: string
  options?: Record<string, unknown>
}

const EXPLORE: CoreFixturePresentation = { presentationId: CORE_TOOL_PRESENTATION_IDS.readFile }
const SHELL: CoreFixturePresentation = { presentationId: CORE_TOOL_PRESENTATION_IDS.shell }
const WEB_SEARCH: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.web,
  options: { operation: 'search' }
}
const WEB_FETCH: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.web,
  options: { operation: 'fetch' }
}
const SUBAGENT_SPAWN: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'spawn' }
}
const SUBAGENT_FOLLOWUP: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'followupTask' }
}

const TEST_IMAGE_BASE64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=='

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
    toolName: 'CreatePlan',
    arguments: {
      title,
      overview: `${title} overview`,
      plan: `# ${title}\n\n- keep visible`
    },
    success: true,
    createdAt
  }, CORE_TOOL_PRESENTATION_IDS.createPlan)
}

function makeImageGenerationItem(
  id: string,
  imageGenerationStatus: 'inProgress' | 'completed' | 'failed',
  createdAt: string,
  result = TEST_IMAGE_BASE64
): ConversationItem {
  return {
    id,
    type: 'imageGeneration',
    status: imageGenerationStatus === 'completed' ? 'completed' : 'started',
    imageGenerationStatus,
    toolCallId: `${id}-call`,
    result: imageGenerationStatus === 'completed' ? result : undefined,
    mediaType: 'image/png',
    savedPath: `<workspace>/.craft/generated_images/thread-1/${id}.png`,
    createdAt,
    completedAt: imageGenerationStatus === 'completed' ? createdAt : undefined
  }
}

function makeDiff(filePath: string, turnId: string): FileDiff {
  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions: 1,
    deletions: 0,
    status: 'written',
    isNewFile: true,
    originalContent: '',
    currentContent: 'new\n',
    diffHunks: [
      {
        oldStart: 0,
        oldLines: 0,
        newStart: 1,
        newLines: 1,
        lines: [{ type: 'add', content: 'new' }]
      }
    ]
  }
}

function renderBlock(
  turn: ConversationTurn,
  options: {
    isRunning?: boolean
    showIdleThinkingFallback?: boolean
    streamingMessage?: string
    streamingMessageLastDeltaAt?: number | null
    streamingReasoning?: string
    activeItemIdOverride?: string | null
  } = {}
): string {
  const { container } = render(
    <LocaleProvider>
      <AgentResponseBlock
        turn={turn}
        isRunning={options.isRunning}
        showIdleThinkingFallback={options.showIdleThinkingFallback}
        streamingMessage={options.streamingMessage}
        streamingMessageLastDeltaAt={options.streamingMessageLastDeltaAt}
        streamingReasoning={options.streamingReasoning}
        activeItemIdOverride={options.activeItemIdOverride}
      />
    </LocaleProvider>
  )
  return container.textContent ?? ''
}

function expectDisclosureInsideTitleGroup(container: HTMLElement): HTMLElement {
  const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
  const disclosureIcon = container.querySelector('[data-testid="tool-disclosure-icon"]') as HTMLElement
  expect(titleGroup).toBeTruthy()
  expect(disclosureIcon).toBeTruthy()
  expect(titleGroup).toContainElement(disclosureIcon)
  expect(titleGroup.style.display).toBe('inline-flex')
  expect(titleGroup.style.flex).toBe('0 1 auto')
  return disclosureIcon
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

describe('AgentResponseBlock error presentation', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'en',
        settings: { get: async () => ({ locale: 'en' }) }
      }
    })
  })

  it('renders an Error item and identical Turn error once', () => {
    const text = renderBlock({
      id: 'turn-error',
      threadId: 'thread-1',
      status: 'failed',
      error: 'Namespace resolution failed.',
      startedAt: '2026-07-15T00:00:00.000Z',
      items: [{
        id: 'error-1',
        type: 'error',
        status: 'completed',
        text: 'Namespace resolution failed.',
        createdAt: '2026-07-15T00:00:01.000Z'
      }]
    })

    expect(text.split('Namespace resolution failed.')).toHaveLength(2)
  })

  it('keeps distinct Item and Turn errors visible', () => {
    const text = renderBlock({
      id: 'turn-error',
      threadId: 'thread-1',
      status: 'failed',
      error: 'Turn cleanup failed.',
      startedAt: '2026-07-15T00:00:00.000Z',
      items: [{
        id: 'error-1',
        type: 'error',
        status: 'completed',
        text: 'Namespace resolution failed.',
        createdAt: '2026-07-15T00:00:01.000Z'
      }]
    })

    expect(text).toContain('Namespace resolution failed.')
    expect(text).toContain('Turn cleanup failed.')
  })
})

describe('AgentResponseBlock fork footer', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({
            thread: {
              id: 'thread-fork',
              displayName: 'Forked thread',
              status: 'active',
              originChannel: 'dotcraft-desktop',
              createdAt: '2026-06-03T00:00:00.000Z',
              lastActiveAt: '2026-06-03T00:00:00.000Z'
            }
          })
        }
      }
    })
  })

  it('forks from the final assistant message item', async () => {
    useConnectionStore.setState({ capabilities: { threadFork: true } })
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-06-03T10:00:00.000Z',
      completedAt: '2026-06-03T10:00:01.000Z',
      items: [
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final answer',
          createdAt: '2026-06-03T10:00:01.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Fork' }))

    await waitFor(() => {
      expect(window.api.appServer.sendRequest).toHaveBeenCalledWith(
        'thread/fork',
        {
          threadId: 'thread-1',
          forkPoint: {
            turnId: 'turn-1',
            itemId: 'assistant-final',
            position: 'after'
          }
        },
        undefined
      )
      expect(useThreadStore.getState().activeThreadId).toBe('thread-fork')
    })
  })
})

describe('AgentResponseBlock subagent transcript rendering', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
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
      ],
      subAgentEntries: [
        {
          label: 'planner',
          isCompleted: true,
          currentTool: undefined,
          currentToolDisplay: undefined,
          inputTokens: 1200,
          outputTokens: 450
        }
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
      ],
      subAgentEntries: [
        {
          label: 'reviewer',
          isCompleted: true,
          currentTool: undefined,
          currentToolDisplay: undefined,
          inputTokens: 300,
          outputTokens: 200
        }
      ]
    }

    const text = renderBlock(turn)
    expect(text).toContain('Spawned agent')
    expect(text).not.toContain('SubAgent completed')
  })

  it('renders grouped SpawnAgent calls as an expanded instruction list with colored names', () => {
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

    expect(screen.getByText('Spawned 2 agents')).toBeInTheDocument()
    expect(screen.getByText('Kepler')).toBeInTheDocument()
    expect(screen.getByText('Lagrange')).toBeInTheDocument()
    expect(screen.getAllByText('(explorer)')).toHaveLength(2)
    expect(screen.getByText('Inspect Settings diagnostics output')).toBeInTheDocument()
    expect(screen.getByText('Review AppServer credential redaction')).toBeInTheDocument()
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

  it('groups consecutive FollowupTask calls as an expanded Updated agents list', () => {
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

    expect(screen.getByText('Updated 2 agents')).toBeInTheDocument()
    expect(screen.getByText('Reviewer')).toBeInTheDocument()
    expect(screen.getByText('Researcher')).toBeInTheDocument()
    expect(screen.getByText('Update Reviewer instructions')).toBeInTheDocument()
    expect(screen.getByText('Update Researcher instructions')).toBeInTheDocument()
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

  it('renders pluginFunctionCall items in the tool run', () => {
    const turn: ConversationTurn = {
      id: 'turn-plugin',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:02:00.000Z',
      items: [
        {
          id: 'plugin-tool-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'plugin-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: '1 + 1' },
          result: '2',
          success: true,
          createdAt: '2026-04-18T10:02:01.000Z'
        }
      ]
    }

    const text = renderBlock(turn)

    expect(text).toContain('Called NodeReplJs')
  })

  it('renders dynamicToolCall items in the tool run', () => {
    const turn: ConversationTurn = {
      id: 'turn-dynamic',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:03:00.000Z',
      items: [
        {
          id: 'dynamic-tool-1',
          type: 'dynamicToolCall',
          status: 'completed',
          toolCallId: 'dynamic-call-1',
          toolName: 'ListBoardItems',
          arguments: { status: 'todo' },
          result: '2 board items',
          success: true,
          createdAt: '2026-04-18T10:03:01.000Z'
        }
      ]
    }

    const text = renderBlock(turn)

    expect(text).toContain('Called ListBoardItems')
  })

  it('renders NodeReplJs image output after the tool row without expanding the card', () => {
    const turn: ConversationTurn = {
      id: 'turn-node-repl-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:04:00.000Z',
      items: [
        {
          id: 'node-repl-tool-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'node-repl-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: 'await nodeRepl.emitImage(image)' },
          result: 'screenshot emitted',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
          ],
          createdAt: '2026-04-18T10:04:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(container.querySelector('[data-testid="tool-expanded-content"]')).toBeNull()
    expect(screen.getByTestId('tool-output-image-gallery')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Tool output image 1' }))
      .toHaveAttribute('src', 'data:image/png;base64,abc123')

    fireEvent.click(screen.getByRole('button', { name: 'Preview tool output image 1' }))
    expect(screen.getByRole('dialog', { name: 'Image preview' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Zoom out' })).toBeInTheDocument()
    expect(screen.getByText('100%')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Zoom in' }))
    expect(screen.getByText('125%')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Zoom out' }))
    expect(screen.getByText('100%')).toBeInTheDocument()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(screen.queryByRole('dialog', { name: 'Image preview' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Preview tool output image 1' }))
    fireEvent.click(screen.getByRole('dialog', { name: 'Image preview' }))
    expect(screen.queryByRole('dialog', { name: 'Image preview' })).not.toBeInTheDocument()
  })

  it('renders multiple tool output images as one gallery', () => {
    const turn: ConversationTurn = {
      id: 'turn-node-repl-images',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:05:00.000Z',
      items: [
        {
          id: 'node-repl-tool-2',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'node-repl-call-2',
          toolName: 'NodeReplJs',
          arguments: { code: 'emit two images' },
          result: 'screenshots emitted',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'first' },
            { type: 'image', mediaType: 'image/jpeg', dataBase64: 'second' }
          ],
          createdAt: '2026-04-18T10:05:01.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const images = screen.getAllByTestId('tool-output-image')
    expect(images).toHaveLength(2)
    expect(images[0]).toHaveAttribute('src', 'data:image/png;base64,first')
    expect(images[1]).toHaveAttribute('src', 'data:image/jpeg;base64,second')
  })

  it('renders dynamicToolCall image output after the tool row', () => {
    const turn: ConversationTurn = {
      id: 'turn-dynamic-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:06:00.000Z',
      items: [
        {
          id: 'dynamic-tool-image-1',
          type: 'dynamicToolCall',
          status: 'completed',
          toolCallId: 'dynamic-call-image-1',
          toolName: 'RenderPreview',
          arguments: { id: 'preview-1' },
          result: 'preview rendered',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'preview' }
          ],
          createdAt: '2026-04-18T10:06:01.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByRole('img', { name: 'Tool output image 1' }))
      .toHaveAttribute('src', 'data:image/png;base64,preview')
  })

  it('renders ReadFile image output from a hydrated toolResult after the tool row', () => {
    const turn: ConversationTurn = {
      id: 'turn-readfile-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:06:10.000Z',
      items: [
        {
          ...makeToolCallItem('read-image-tool-1', 'read-image-call-1', 'ReadFile', '2026-04-18T10:06:11.000Z', EXPLORE),
          arguments: { path: 'docs/diagram.png' }
        },
        {
          id: 'read-image-result-1',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'read-image-call-1',
          result: 'Image: diagram.png (3 bytes, image/png)',
          success: true,
          contentItems: [
            { type: 'text', text: 'Image: diagram.png (3 bytes, image/png)' },
            { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
          ],
          createdAt: '2026-04-18T10:06:12.000Z',
          completedAt: '2026-04-18T10:06:12.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByTestId('tool-output-image-gallery')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: 'Tool output image 1' }))
      .toHaveAttribute('src', 'data:image/png;base64,AQID')

    fireEvent.click(screen.getByRole('button', { name: 'Preview tool output image 1' }))
    expect(screen.getByRole('dialog', { name: 'Image preview' })).toBeInTheDocument()
  })

  it('shows the tool output image context menu and selects all', () => {
    const execCommand = vi.fn()
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: execCommand
    })
    const turn: ConversationTurn = {
      id: 'turn-node-repl-context-menu',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:06:30.000Z',
      items: [
        {
          id: 'node-repl-context-menu-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'node-repl-context-menu-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: 'emit image' },
          result: 'screenshot emitted',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
          ],
          createdAt: '2026-04-18T10:06:31.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.contextMenu(screen.getByRole('button', { name: 'Preview tool output image 1' }), {
      clientX: 12,
      clientY: 24
    })

    expect(screen.getByRole('menuitem', { name: 'Select All' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Copy Image' })).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Copy message' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Select All' }))
    expect(execCommand).toHaveBeenCalledWith('selectAll')
  })

  it('copies tool output images with ClipboardItem when supported', async () => {
    const write = vi.fn(async () => undefined)
    const writeText = vi.fn(async () => undefined)
    class MockClipboardItem {
      items: Record<string, Blob>

      constructor(items: Record<string, Blob>) {
        this.items = items
      }
    }
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { write, writeText }
    })
    Object.defineProperty(globalThis, 'ClipboardItem', {
      configurable: true,
      value: MockClipboardItem
    })
    const turn: ConversationTurn = {
      id: 'turn-node-repl-copy-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:06:40.000Z',
      items: [
        {
          id: 'node-repl-copy-image-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'node-repl-copy-image-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: 'emit image' },
          result: 'screenshot emitted',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
          ],
          createdAt: '2026-04-18T10:06:41.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.contextMenu(screen.getByRole('button', { name: 'Preview tool output image 1' }), {
      clientX: 12,
      clientY: 24
    })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy Image' }))

    await waitFor(() => expect(write).toHaveBeenCalledTimes(1))
    expect(writeText).not.toHaveBeenCalled()
    const clipboardItem = write.mock.calls[0][0][0] as { items: Record<string, Blob> }
    expect(clipboardItem.items['image/png']).toBeInstanceOf(Blob)
    expect(clipboardItem.items['image/png'].type).toBe('image/png')
  })

  it('falls back to copying the tool output image data URL as text', async () => {
    const writeText = vi.fn(async () => undefined)
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText }
    })
    Object.defineProperty(globalThis, 'ClipboardItem', {
      configurable: true,
      value: undefined
    })
    const turn: ConversationTurn = {
      id: 'turn-node-repl-copy-image-fallback',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:06:50.000Z',
      items: [
        {
          id: 'node-repl-copy-image-fallback-1',
          type: 'pluginFunctionCall',
          status: 'completed',
          toolCallId: 'node-repl-copy-image-fallback-call-1',
          toolName: 'NodeReplJs',
          arguments: { code: 'emit image' },
          result: 'screenshot emitted',
          success: true,
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
          ],
          createdAt: '2026-04-18T10:06:51.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.contextMenu(screen.getByRole('button', { name: 'Preview tool output image 1' }), {
      clientX: 12,
      clientY: 24
    })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Copy Image' }))

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('data:image/png;base64,AQID'))
  })

  it('renders grouped tool images once after the group row', () => {
    const turn: ConversationTurn = {
      id: 'turn-grouped-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:07:00.000Z',
      items: [
        {
          ...makeToolCallItem('read-tool-1', 'read-call-1', 'ReadFile', '2026-04-18T10:07:01.000Z', EXPLORE),
          contentItems: [
            { type: 'image', mediaType: 'image/png', dataBase64: 'grouped' }
          ]
        },
        makeToolCallItem('read-tool-2', 'read-call-2', 'ReadFile', '2026-04-18T10:07:02.000Z', EXPLORE)
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getAllByTestId('tool-output-image')).toHaveLength(1)
    fireEvent.click(screen.getAllByRole('button')[0])
    expect(screen.getAllByTestId('tool-output-image')).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: 'Preview tool output image 1' }))
    expect(screen.getByRole('dialog', { name: 'Image preview' })).toBeInTheDocument()
  })
})

describe('AgentResponseBlock stream retry signal rendering', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        initialLocale: 'zh-Hans',
        settings: {
          get: async () => ({ locale: 'zh-Hans' })
        }
      }
    })
  })

  it('renders localized retry rows before later live thinking content', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-05-22T10:00:00.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-05-22T10:00:03.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          activeItemIdOverride="reasoning-1"
          streamRetrySignals={[
            {
              id: 'retry-1',
              turnId: 'turn-1',
              rawMessage: 'Reconnecting... 1/1',
              attempt: 1,
              max: 1,
              createdAt: '2026-05-22T10:00:02.000Z'
            }
          ]}
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('status', { name: '正在重新连接… 1/1' })).toBeInTheDocument()
    const text = container.textContent ?? ''
    expect(text.indexOf('正在重新连接… 1/1')).toBeGreaterThanOrEqual(0)
    expect(text.indexOf('正在重新连接… 1/1')).toBeLessThan(text.indexOf('正在思考'))
  })

  it('does not render retry rows after the turn is no longer running', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-05-22T10:00:00.000Z',
      completedAt: '2026-05-22T10:00:04.000Z',
      items: []
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          streamRetrySignals={[
            {
              id: 'retry-1',
              turnId: 'turn-1',
              rawMessage: 'Reconnecting... 1/1',
              attempt: 1,
              max: 1,
              createdAt: '2026-05-22T10:00:02.000Z'
            }
          ]}
        />
      </LocaleProvider>
    )

    expect(screen.queryByTestId('stream-retry-row')).toBeNull()
  })

  it('falls back to the raw message when attempt parsing is unavailable', () => {
    const turn: ConversationTurn = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-05-22T10:00:00.000Z',
      items: []
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          streamRetrySignals={[
            {
              id: 'retry-raw',
              turnId: 'turn-1',
              rawMessage: 'Provider connection lost; retrying',
              attempt: null,
              max: null,
              createdAt: '2026-05-22T10:00:02.000Z'
            }
          ]}
        />
      </LocaleProvider>
    )

    expect(screen.getByRole('status', { name: 'Provider connection lost; retrying' })).toBeInTheDocument()
  })
})

describe('AgentResponseBlock tail tool aggregation timing', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('keeps trailing completed tool run as single cards while the turn is still running', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:00:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:00:01.000Z', EXPLORE),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:00:02.000Z', EXPLORE)
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(screen.queryByText('Explored 2 files')).toBeNull()
    expect(screen.getAllByText('Explored files')).toHaveLength(2)
  })

  it('aggregates the same tool run once reasoning starts after it', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-unlocked',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:05:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:05:01.000Z', EXPLORE),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:05:02.000Z', EXPLORE),
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:05:03.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning activeItemIdOverride="reasoning-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('Explored 2 files')).toBeInTheDocument()
  })

  it('keeps a settled restored explore run grouped before later live tools and approvals', () => {
    const turn: ConversationTurn = {
      id: 'turn-restored-parallel-approvals',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:06:00.000Z',
      items: [
        makeToolCallItem('tool-profile', 'call-profile', 'ReadFile', '2026-04-18T11:06:01.000Z', EXPLORE),
        makeToolCallItem('tool-notes', 'call-notes', 'ReadFile', '2026-04-18T11:06:02.000Z', EXPLORE),
        makeToolCallItem('tool-assets', 'call-assets', 'FindFiles', '2026-04-18T11:06:03.000Z', EXPLORE),
        {
          id: 'approval-profile',
          type: 'approvalCard',
          status: 'completed',
          approvalRequestId: 'req-profile',
          approvalType: 'file',
          approvalOperation: 'read',
          approvalTarget: 'docs/profile.md',
          approvalState: 'accepted',
          createdAt: '2026-04-18T11:06:04.000Z'
        },
        {
          id: 'tool-next-find',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-next-find',
          toolName: 'FindFiles',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'FindFiles' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'docs' },
          createdAt: '2026-04-18T11:06:05.000Z'
        },
        {
          id: 'approval-next',
          type: 'approvalCard',
          status: 'completed',
          approvalRequestId: 'req-next',
          approvalType: 'file',
          approvalOperation: 'read',
          approvalTarget: 'docs',
          approvalState: 'pending',
          createdAt: '2026-04-18T11:06:06.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(screen.getByText('Explored 3 files')).toBeInTheDocument()
    expect(screen.queryByText(/Reading file/)).toBeNull()
  })

  it('aggregates trailing tool run after the turn completes', () => {
    const turn: ConversationTurn = {
      id: 'turn-tail-completed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:10:00.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'ReadFile', '2026-04-18T11:10:01.000Z', EXPLORE),
        makeToolCallItem('tool-2', 'call-2', 'FindFiles', '2026-04-18T11:10:02.000Z', EXPLORE)
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Explored 2 files')).toBeInTheDocument()
  })

  it('keeps consecutive aggregated tool rows in a compact tool-run stack', () => {
    const turn: ConversationTurn = {
      id: 'turn-mixed-tool-stack',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        makeToolCallItem('shell-1', 'call-shell-1', 'RunCommand', '2026-04-18T11:11:01.000Z', SHELL),
        makeToolCallItem('shell-2', 'call-shell-2', 'Exec', '2026-04-18T11:11:02.000Z', SHELL),
        makeToolCallItem('file-1', 'call-file-1', 'ReadFile', '2026-04-18T11:11:03.000Z', EXPLORE),
        makeToolCallItem('file-2', 'call-file-2', 'FindFiles', '2026-04-18T11:11:04.000Z', EXPLORE)
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const stack = container.querySelector('[data-testid="tool-run-stack"]') as HTMLElement
    expect(stack).toBeTruthy()
    expect(stack.style.gap).toBe('var(--conversation-tool-run-gap)')
    expect(stack).toHaveTextContent('Ran 2 commands')
    expect(stack).toHaveTextContent('Explored 2 files')
  })

  it('does not redden an aggregated shell group when an exec command fails', () => {
    const turn: ConversationTurn = {
      id: 'turn-shell-failed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        makeToolCallItem('shell-1', 'call-shell-1', 'Exec', '2026-04-18T11:11:01.000Z', SHELL),
        { ...makeToolCallItem('shell-2', 'call-shell-2', 'Exec', '2026-04-18T11:11:02.000Z', SHELL), success: false, exitCode: 1 }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
    expect(titleGroup).toBeTruthy()
    expect(titleGroup).toHaveTextContent('Ran 2 commands')
    // Mirrors the individual ToolCallCard, which never reddens shell tools.
    expect(titleGroup.style.color).toBe('var(--text-dimmed)')
  })

  it('still reddens an aggregated non-shell group when a tool fails', () => {
    const turn: ConversationTurn = {
      id: 'turn-explore-failed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        makeToolCallItem('file-1', 'call-file-1', 'ReadFile', '2026-04-18T11:11:01.000Z', EXPLORE),
        { ...makeToolCallItem('file-2', 'call-file-2', 'FindFiles', '2026-04-18T11:11:02.000Z', EXPLORE), success: false }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
    expect(titleGroup).toBeTruthy()
    expect(titleGroup.style.color).toBe('var(--error)')
  })

  it('keeps adjacent tool stacks compact when hidden reasoning splits the raw items', () => {
    useUIStore.getState().setShowThinkingContent(false)

    const turn: ConversationTurn = {
      id: 'turn-hidden-reasoning-tool-stack',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        makeToolCallItem('shell-1', 'call-shell-1', 'RunCommand', '2026-04-18T11:11:01.000Z', SHELL),
        makeToolCallItem('shell-2', 'call-shell-2', 'Exec', '2026-04-18T11:11:02.000Z', SHELL),
        {
          id: 'reasoning-hidden',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'hidden reasoning',
          createdAt: '2026-04-18T11:11:03.000Z'
        },
        makeToolCallItem('shell-3', 'call-shell-3', 'RunCommand', '2026-04-18T11:11:04.000Z', SHELL),
        makeToolCallItem('shell-4', 'call-shell-4', 'Exec', '2026-04-18T11:11:05.000Z', SHELL)
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const toolFlowItems = Array.from(
      container.querySelectorAll('[data-testid="conversation-flow-item"][data-kind="tool"]')
    ) as HTMLElement[]
    expect(toolFlowItems).toHaveLength(2)
    expect(toolFlowItems[1].style.marginTop).toBe('var(--conversation-tool-run-gap)')
    expect(container.querySelectorAll('[data-testid="tool-run-stack"]')).toHaveLength(2)
    expect(screen.getAllByText('Ran 2 commands')).toHaveLength(2)
  })

  it('does not let empty agent messages create visible gaps between tool stacks', () => {
    const turn: ConversationTurn = {
      id: 'turn-empty-agent-message-tool-stack',
      threadId: 'thread-1',
      status: 'failed',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        makeToolCallItem('shell-1', 'call-shell-1', 'RunCommand', '2026-04-18T11:11:01.000Z', SHELL),
        makeToolCallItem('shell-2', 'call-shell-2', 'Exec', '2026-04-18T11:11:02.000Z', SHELL),
        {
          id: 'empty-agent-message',
          type: 'agentMessage',
          status: 'completed',
          text: '   \n',
          createdAt: '2026-04-18T11:11:03.000Z'
        },
        makeToolCallItem('shell-3', 'call-shell-3', 'RunCommand', '2026-04-18T11:11:04.000Z', SHELL),
        makeToolCallItem('shell-4', 'call-shell-4', 'Exec', '2026-04-18T11:11:05.000Z', SHELL)
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const flowItems = Array.from(
      container.querySelectorAll('[data-testid="conversation-flow-item"]')
    ) as HTMLElement[]
    const toolFlowItems = flowItems.filter((item) => item.dataset.kind === 'tool')

    expect(flowItems.every((item) => item.dataset.kind === 'tool')).toBe(true)
    expect(toolFlowItems).toHaveLength(2)
    expect(toolFlowItems[1].style.marginTop).toBe('var(--conversation-tool-run-gap)')
    expect(screen.getAllByText('Ran 2 commands')).toHaveLength(2)
  })

  it('uses the same compact gap when tool runs sit beside text and thinking rows', () => {
    const turn: ConversationTurn = {
      id: 'turn-text-tool-thinking-gap',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:11:00.000Z',
      items: [
        {
          id: 'assistant-before-tool',
          type: 'agentMessage',
          status: 'completed',
          text: 'Let me search from a few angles.',
          createdAt: '2026-04-18T11:11:00.500Z'
        },
        makeToolCallItem('web-1', 'call-web-1', 'WebSearch', '2026-04-18T11:11:01.000Z', WEB_SEARCH),
        makeToolCallItem('web-2', 'call-web-2', 'WebFetch', '2026-04-18T11:11:02.000Z', WEB_FETCH),
        {
          id: 'thinking-after-tool',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:11:03.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const flowItems = Array.from(
      container.querySelectorAll('[data-testid="conversation-flow-item"]')
    ) as HTMLElement[]

    expect(flowItems.map((item) => item.dataset.kind)).toEqual(['assistant', 'tool', 'assistant'])
    expect(flowItems[1].style.marginTop).toBe('var(--conversation-tool-run-gap)')
    expect(flowItems[2].style.marginTop).toBe('var(--conversation-tool-run-gap)')
  })

  it('renders agent message time and copy action in a separate footer row', () => {
    const turn: ConversationTurn = {
      id: 'turn-agent-message-footer',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:04:00.000Z',
      items: [
        {
          id: 'assistant-message-with-footer',
          type: 'agentMessage',
          status: 'completed',
          text: 'Final answer text.',
          createdAt: '2026-04-18T10:05:00.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const footer = container.querySelector('[data-testid="agent-message-footer"]') as HTMLElement
    const copyButton = screen.getByRole('button', { name: /copy/i })

    expect(footer).toBeTruthy()
    expect(footer.style.minHeight).toBe('24px')
    expect(footer.style.justifyContent).toBe('flex-start')
    expect(footer).toContainElement(screen.getByTestId('agent-message-time'))
    expect(footer).toContainElement(copyButton)
    expect(footer.firstElementChild).toBe(copyButton.parentElement)
    expect(footer.lastElementChild).toContainElement(screen.getByTestId('agent-message-time'))
  })

  it('renders turn artifacts and file changes before the final agent footer', () => {
    useConversationStore.setState({
      workspacePath: 'F:/workspace',
      changedFiles: new Map([
        ['site/index.html', makeDiff('site/index.html', 'turn-agent-artifacts-before-footer')]
      ])
    })

    const turn: ConversationTurn = {
      id: 'turn-agent-artifacts-before-footer',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:04:00.000Z',
      completedAt: '2026-04-18T10:05:00.000Z',
      items: [
        {
          id: 'assistant-message-with-artifacts',
          type: 'agentMessage',
          status: 'completed',
          text: 'Final answer with artifacts.',
          createdAt: '2026-04-18T10:05:00.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    const artifactTitle = screen.getByText('index.html')
    const fileChanges = screen.getByText('1 file changed')
    const footer = container.querySelector('[data-testid="agent-message-footer"]') as HTMLElement

    expect(footer).toBeTruthy()
    expect(artifactTitle.compareDocumentPosition(footer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(fileChanges.compareDocumentPosition(footer) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('does not render agent message footers while the turn is still running', () => {
    const turn: ConversationTurn = {
      id: 'turn-running-agent-message-footer',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T10:04:00.000Z',
      items: [
        {
          id: 'assistant-streaming-message',
          type: 'agentMessage',
          status: 'streaming',
          text: 'Still working...',
          createdAt: '2026-04-18T10:05:00.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          activeItemIdOverride="assistant-streaming-message"
          streamingMessage="Still working..."
        />
      </LocaleProvider>
    )

    expect(screen.getByText('Still working...')).toBeInTheDocument()
    expect(container.querySelector('[data-testid="agent-message-footer"]')).toBeNull()
  })

  it('renders the agent footer only on the final agent message in a completed turn', () => {
    const turn: ConversationTurn = {
      id: 'turn-multiple-agent-message-footer',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T10:04:00.000Z',
      items: [
        {
          id: 'assistant-first',
          type: 'agentMessage',
          status: 'completed',
          text: 'First answer.',
          createdAt: '2026-04-18T10:05:00.000Z'
        },
        {
          id: 'assistant-last',
          type: 'agentMessage',
          status: 'completed',
          text: 'Last answer.',
          createdAt: '2026-04-18T10:06:00.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.queryByText('First answer.')).toBeNull()
    expect(screen.getByText('Last answer.')).toBeInTheDocument()
    expect(container.querySelectorAll('[data-testid="agent-message-footer"]')).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: /Processed in/ }))

    expect(screen.getByText('First answer.')).toBeInTheDocument()
    expect(container.querySelectorAll('[data-testid="agent-message-footer"]')).toHaveLength(1)
  })

  it('renders completed parallel tool results as settled while unmatched tools stay running', () => {
    const turn: ConversationTurn = {
      id: 'turn-parallel-mixed',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:12:00.000Z',
      items: [
        {
          id: 'tool-done',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-done',
          toolName: 'FollowupTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:01.000Z'
        },
        {
          id: 'tool-pending',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-pending',
          toolName: 'PendingTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:02.000Z'
        },
        {
          id: 'result-done',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'call-done',
          result: 'done',
          success: true,
          createdAt: '2026-04-18T11:12:03.000Z',
          completedAt: '2026-04-18T11:12:03.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    const completedLabel = screen.getByText('Called FollowupTool')
    expect(completedLabel).toBeInTheDocument()
    expect(screen.getByText(/PendingTool/)).toBeInTheDocument()
    expect(screen.queryByText('done')).toBeNull()
  })

  it('hydrates completed parallel tools from toolExecution while unmatched tools stay running', () => {
    const turn: ConversationTurn = {
      id: 'turn-parallel-tool-execution',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:12:00.000Z',
      items: [
        {
          id: 'tool-done',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-done',
          toolName: 'FollowupTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:01.000Z'
        },
        {
          id: 'tool-pending',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-pending',
          toolName: 'PendingTool',
          arguments: {},
          createdAt: '2026-04-18T11:12:02.000Z'
        },
        {
          id: 'execution-done',
          type: 'toolExecution',
          status: 'completed',
          toolCallId: 'call-done',
          toolName: 'FollowupTool',
          resultPreview: 'agent done',
          success: true,
          duration: 1200,
          createdAt: '2026-04-18T11:12:01.000Z',
          completedAt: '2026-04-18T11:12:03.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(screen.getByText('Called FollowupTool')).toBeInTheDocument()
    expect(screen.getByText(/PendingTool/)).toBeInTheDocument()
    expect(screen.queryByText('agent done')).toBeNull()
  })

  it('keeps WebSearch child tool headers but removes duplicate expanded copy above the table', () => {
    const makeSearchItem = (
      id: string,
      query: string,
      title: string,
      url: string,
      createdAt: string
    ): ConversationItem => ({
      id,
      type: 'toolCall',
      status: 'completed',
      toolCallId: id,
      toolName: 'WebSearch',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WebSearch' },
          presentation: { presentationId: 'core.web', options: { operation: 'search' } },
      arguments: { query },
      result: JSON.stringify({
        query,
        results: [{ title, url }]
      }),
      success: true,
      createdAt
    })

    const turn: ConversationTurn = {
      id: 'turn-web-group',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:15:00.000Z',
      items: [
        makeSearchItem('web-1', 'large graph visualization', 'First result', 'https://example.com/first', '2026-04-18T11:15:01.000Z'),
        makeSearchItem('web-2', 'react flow performance', 'Second result', 'https://example.com/second', '2026-04-18T11:15:02.000Z')
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: /Searched web 2 times/ }))

    const firstToolTitle = screen.getByRole('button', { name: 'Searched "large graph visualization"' })
    const secondToolTitle = screen.getByRole('button', { name: 'Searched "react flow performance"' })
    expect(firstToolTitle).toBeInTheDocument()
    expect(secondToolTitle).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Title' })).toBeNull()

    fireEvent.click(firstToolTitle)

    expect(screen.getAllByRole('columnheader', { name: 'Title' })).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'First result' })).toBeInTheDocument()
    expect(screen.queryByText('Web search')).toBeNull()
    expect(screen.getAllByText('Searched "large graph visualization"')).toHaveLength(1)
  })
})

describe('AgentResponseBlock reasoning timeline rendering', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders reasoning items as separate timeline rows around tool output', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-timeline',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:18:00.000Z',
      items: [
        {
          id: 'reasoning-before',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'first thought',
          elapsedSeconds: 3,
          createdAt: '2026-04-18T11:18:01.000Z'
        },
        {
          id: 'tool-between',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-between',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:18:02.000Z'
        },
        {
          id: 'reasoning-after',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'second thought',
          elapsedSeconds: 5,
          createdAt: '2026-04-18T11:18:03.000Z'
        },
        {
          id: 'assistant-after',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-18T11:18:04.000Z'
        }
      ]
    }

    const text = renderBlock(turn, { isRunning: true })
    const firstThought = text.indexOf('Thought 3s')
    const tool = text.indexOf('Read main.ts')
    const secondThought = text.indexOf('Thought 5s')
    const finalMessage = text.indexOf('final response')

    expect(firstThought).toBeGreaterThan(-1)
    expect(tool).toBeGreaterThan(-1)
    expect(secondThought).toBeGreaterThan(-1)
    expect(finalMessage).toBeGreaterThan(-1)
    expect(firstThought).toBeLessThan(tool)
    expect(tool).toBeLessThan(secondThought)
    expect(secondThought).toBeLessThan(finalMessage)
  })

  it('uses the shared disclosure icon and keeps expanded reasoning as italic quote content', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-style',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:19:00.000Z',
      items: [
        {
          id: 'reasoning-style',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'quoted reasoning',
          elapsedSeconds: 7,
          createdAt: '2026-04-18T11:19:01.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )
    const button = screen.getByRole('button', { name: 'Thought 7s' })

    expect(screen.getByText('Thought 7s')).toBeInTheDocument()

    fireEvent.click(button)

    expect(screen.getByText('quoted reasoning')).toBeInTheDocument()
  })

  it('allows streaming reasoning with text to expand using the same row layout', () => {
    const turn: ConversationTurn = {
      id: 'turn-reasoning-streaming',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:19:30.000Z',
      items: [
        {
          id: 'reasoning-streaming',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:19:31.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          activeItemIdOverride="reasoning-streaming"
          streamingReasoning="live reasoning"
        />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Thinking' })
    expect(screen.getByText('Thinking')).toBeInTheDocument()
    expectDisclosureInsideTitleGroup(container)
    fireEvent.click(button)

    expect(screen.getByText('live reasoning')).toBeInTheDocument()
  })

  it('hides completed reasoning rows when thinking content is disabled', () => {
    useUIStore.getState().setShowThinkingContent(false)
    const turn: ConversationTurn = {
      id: 'turn-reasoning-hidden',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:19:40.000Z',
      completedAt: '2026-04-18T11:19:45.000Z',
      items: [
        {
          id: 'reasoning-hidden',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'private reasoning',
          elapsedSeconds: 4,
          createdAt: '2026-04-18T11:19:41.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final answer',
          createdAt: '2026-04-18T11:19:44.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.queryByText('Thought 4s')).toBeNull()
    expect(screen.queryByText('private reasoning')).toBeNull()
    expect(screen.queryByText(/Processed in/)).toBeNull()
    expect(screen.getByText('final answer')).toBeInTheDocument()
  })

  it('shows only a non-expandable live thinking row when thinking content is disabled', () => {
    useUIStore.getState().setShowThinkingContent(false)
    const turn: ConversationTurn = {
      id: 'turn-reasoning-streaming-hidden',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:19:50.000Z',
      items: [
        {
          id: 'reasoning-streaming-hidden',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:19:51.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          activeItemIdOverride="reasoning-streaming-hidden"
          streamingReasoning="hidden live reasoning"
        />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Thinking' })
    expect(screen.getByText('Thinking')).toBeInTheDocument()
    expect(container.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()

    fireEvent.click(button)

    expect(screen.queryByText('hidden live reasoning')).toBeNull()
  })
})

describe('AgentResponseBlock idle running fallback', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders a non-expandable Thinking row for a silent running turn', () => {
    const turn: ConversationTurn = {
      id: 'turn-idle-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:21:00.000Z',
      items: [
        {
          id: 'assistant-static',
          type: 'agentMessage',
          status: 'completed',
          text: 'Still reviewing the context.',
          createdAt: '2026-04-18T11:21:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          showIdleThinkingFallback
        />
      </LocaleProvider>
    )

    const button = screen.getByRole('button', { name: 'Thinking' })
    expect(screen.getByText('Thinking')).toBeInTheDocument()
    expect(container.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()

    fireEvent.click(button)

    expect(container.textContent).not.toContain('undefined')
  })

  it('does not duplicate the fallback when live reasoning is visible', () => {
    const turn: ConversationTurn = {
      id: 'turn-live-reasoning',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:22:00.000Z',
      items: [
        {
          id: 'reasoning-live',
          type: 'reasoningContent',
          status: 'streaming',
          reasoning: '',
          createdAt: '2026-04-18T11:22:01.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          showIdleThinkingFallback
          activeItemIdOverride="reasoning-live"
          streamingReasoning="live reasoning"
        />
      </LocaleProvider>
    )

    expect(screen.getAllByText('Thinking')).toHaveLength(1)
    expect(screen.getByText('Thinking')).toBeInTheDocument()
  })

  it('does not render the fallback when non-empty assistant text streamed within the stall threshold', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-18T11:23:02.000Z'))
    const turn: ConversationTurn = {
      id: 'turn-live-message',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:23:00.000Z',
      items: [
        {
          id: 'assistant-live',
          type: 'agentMessage',
          status: 'streaming',
          text: '',
          createdAt: '2026-04-18T11:23:01.000Z'
        }
      ]
    }

    renderBlock(turn, {
      isRunning: true,
      showIdleThinkingFallback: true,
      activeItemIdOverride: 'assistant-live',
      streamingMessage: 'Streaming answer',
      streamingMessageLastDeltaAt: Date.now()
    })

    expect(screen.queryByText('Thinking')).toBeNull()
    expect(screen.getByText('Streaming answer')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(1999)
    })

    expect(screen.queryByText('Thinking')).toBeNull()
  })

  it('renders the fallback below non-empty assistant text after the stream stalls', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-18T11:23:10.000Z'))
    const turn: ConversationTurn = {
      id: 'turn-stalled-message',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:23:00.000Z',
      items: [
        {
          id: 'assistant-live',
          type: 'agentMessage',
          status: 'streaming',
          text: '',
          createdAt: '2026-04-18T11:23:01.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          showIdleThinkingFallback
          activeItemIdOverride="assistant-live"
          streamingMessage="Streaming answer"
          streamingMessageLastDeltaAt={Date.now()}
        />
      </LocaleProvider>
    )

    expect(screen.queryByText('Thinking')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(2000)
    })

    expect(screen.getByText('Streaming answer')).toBeInTheDocument()
    expect(screen.getByText('Thinking')).toBeInTheDocument()
    const text = container.textContent ?? ''
    expect(text.indexOf('Streaming answer')).toBeLessThan(text.indexOf('Thinking'))
  })

  it('hides the stalled fallback when a new delta appends to the same streaming message', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-18T11:23:20.000Z'))
    const turn: ConversationTurn = {
      id: 'turn-resumed-message',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:23:00.000Z',
      items: [
        {
          id: 'assistant-live',
          type: 'agentMessage',
          status: 'streaming',
          text: '',
          createdAt: '2026-04-18T11:23:01.000Z'
        }
      ]
    }

    const renderLive = (message: string, lastDeltaAt: number): JSX.Element => (
      <LocaleProvider>
        <AgentResponseBlock
          turn={turn}
          isRunning
          showIdleThinkingFallback
          activeItemIdOverride="assistant-live"
          streamingMessage={message}
          streamingMessageLastDeltaAt={lastDeltaAt}
        />
      </LocaleProvider>
    )
    const { rerender } = render(renderLive('Streaming answer', Date.now()))

    act(() => {
      vi.advanceTimersByTime(2000)
    })

    expect(screen.getByText('Thinking')).toBeInTheDocument()

    rerender(renderLive('Streaming answer continued', Date.now()))
    // Appended text reveals via the typewriter cadence; advance well under the
    // 2000ms stall threshold so it finishes without re-triggering the fallback.
    act(() => {
      vi.advanceTimersByTime(500)
    })

    expect(screen.queryByText('Thinking')).toBeNull()
    expect(screen.getByText('Streaming answer continued')).toBeInTheDocument()
    expect(screen.queryByText('Streaming answer', { exact: true })).toBeNull()
  })

  it('does not render the fallback while a tool row is live', () => {
    const turn: ConversationTurn = {
      id: 'turn-live-tool',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:24:00.000Z',
      items: [
        {
          id: 'tool-live',
          type: 'toolCall',
          status: 'started',
          toolCallId: 'call-live',
          toolName: 'FollowupTool',
          arguments: {},
          createdAt: '2026-04-18T11:24:01.000Z'
        }
      ]
    }

    renderBlock(turn, { isRunning: true, showIdleThinkingFallback: true })

    expect(screen.queryByText('Thinking')).toBeNull()
    expect(screen.getByText(/FollowupTool/)).toBeInTheDocument()
  })

  it('renders the fallback when a ReadFile tool has already settled in a running turn', () => {
    const turn: ConversationTurn = {
      id: 'turn-settled-read',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:24:10.000Z',
      items: [
        {
          id: 'tool-read-settled',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-read-settled',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'docs/readme.md' },
          result: 'file contents',
          success: true,
          createdAt: '2026-04-18T11:24:11.000Z',
          completedAt: '2026-04-18T11:24:12.000Z'
        }
      ]
    }

    renderBlock(turn, { isRunning: true, showIdleThinkingFallback: true })

    expect(screen.getByText('Thinking')).toBeInTheDocument()
    expect(screen.queryByText(/Reading file/i)).toBeNull()
  })

  it('does not render the fallback after terminal turn statuses', () => {
    const statuses: Array<ConversationTurn['status']> = ['completed', 'failed', 'cancelled']

    for (const status of statuses) {
      const turn: ConversationTurn = {
        id: `turn-${status}`,
        threadId: 'thread-1',
        status,
        startedAt: '2026-04-18T11:25:00.000Z',
        completedAt: '2026-04-18T11:25:02.000Z',
        error: status === 'failed' ? 'failed' : undefined,
        cancelReason: status === 'cancelled' ? 'cancelled' : undefined,
        items: []
      }

      const { unmount } = render(
        <LocaleProvider>
          <AgentResponseBlock
            turn={turn}
            isRunning={false}
            showIdleThinkingFallback
          />
        </LocaleProvider>
      )

      expect(screen.queryByText('Thinking')).toBeNull()
      unmount()
    }
  })
})

describe('AgentResponseBlock image generation', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders in-progress image generation with a running status and skeleton', () => {
    const turn: ConversationTurn = {
      id: 'turn-image-running',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-18T11:00:00.000Z',
      items: [
        makeImageGenerationItem('image-running', 'inProgress', '2026-04-18T11:00:01.000Z')
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} isRunning />
      </LocaleProvider>
    )

    expect(screen.getByRole('status', { name: 'Generating image' })).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Generating image')).toBeInTheDocument()
    expect(container.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
    expect(screen.getByTestId('image-generation-skeleton')).toBeInTheDocument()
    expect(screen.queryByTestId('tool-output-image-gallery')).toBeNull()
  })

  it('renders completed image generation output without the running placeholder', () => {
    const turn: ConversationTurn = {
      id: 'turn-image-completed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:05:00.000Z',
      completedAt: '2026-04-18T11:05:03.000Z',
      items: [
        makeImageGenerationItem('image-completed', 'completed', '2026-04-18T11:05:02.000Z')
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Generated image')).toBeInTheDocument()
    expect(screen.getByTestId('tool-output-image')).toHaveAttribute(
      'src',
      `data:image/png;base64,${TEST_IMAGE_BASE64}`
    )
    expect(screen.queryByTestId('image-generation-skeleton')).toBeNull()
    expect(container.querySelector('.tool-running-gradient-text')).toBeNull()
  })
})

describe('AgentResponseBlock completed turn folding', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('collapses intermediate items into processed summary and keeps final message visible', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:20:00.000Z',
      completedAt: '2026-04-18T11:20:10.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'intermediate reasoning',
          elapsedSeconds: 2,
          createdAt: '2026-04-18T11:20:01.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:20:02.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-18T11:20:05.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Processed in 5s')).toBeInTheDocument()
    expect(screen.getByText('final response')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()
    const summaryButton = screen.getByRole('button', { name: /Processed in 5s/ })

    fireEvent.click(summaryButton)

    expect(screen.getByText('Thought 2s')).toBeInTheDocument()
    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
    const expandedText = container.textContent ?? ''
    expect(expandedText.indexOf('Thought 2s')).toBeLessThan(expandedText.indexOf('Read main.ts'))
  })

  it('omits agent message footers inside processed summaries', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-agent-message-footer',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:20:00.000Z',
      completedAt: '2026-04-18T11:20:10.000Z',
      items: [
        {
          id: 'assistant-intermediate',
          type: 'agentMessage',
          status: 'completed',
          text: 'intermediate response',
          createdAt: '2026-04-18T11:20:02.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:20:03.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-18T11:20:06.000Z'
        }
      ]
    }

    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(container.querySelectorAll('[data-testid="agent-message-footer"]')).toHaveLength(1)
    expect(screen.queryByText('intermediate response')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 6s/ }))

    expect(screen.getByText('intermediate response')).toBeInTheDocument()
    expect(container.querySelectorAll('[data-testid="agent-message-footer"]')).toHaveLength(1)
  })

  it('keeps the final CreatePlan visible while folding earlier intermediate work', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-plan',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:30:00.000Z',
      completedAt: '2026-04-18T11:30:10.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'intermediate reasoning',
          elapsedSeconds: 2,
          createdAt: '2026-04-18T11:30:01.000Z'
        },
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:30:02.000Z'
        },
        makeCreatePlanItem('plan-final', 'Visible Plan', '2026-04-18T11:30:04.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after plan',
          createdAt: '2026-04-18T11:30:06.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Processed in 6s')).toBeInTheDocument()
    expect(screen.getByText('Visible Plan')).toBeInTheDocument()
    expect(screen.getByText('final response after plan')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 6s/ }))

    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
  })

  it('pins only the latest CreatePlan before the final message', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-two-plans',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:40:00.000Z',
      completedAt: '2026-04-18T11:40:12.000Z',
      items: [
        makeCreatePlanItem('plan-first', 'First Plan', '2026-04-18T11:40:01.000Z'),
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:40:02.000Z'
        },
        makeCreatePlanItem('plan-latest', 'Latest Plan', '2026-04-18T11:40:05.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after latest plan',
          createdAt: '2026-04-18T11:40:08.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByText('Latest Plan')).toBeInTheDocument()
    expect(screen.queryByText('First Plan')).toBeNull()
    expect(screen.getByText('final response after latest plan')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 8s/ }))

    expect(screen.getByText('First Plan')).toBeInTheDocument()
  })

  it('keeps the latest completed image generation result visible while folding earlier work', () => {
    const turn: ConversationTurn = {
      id: 'turn-folded-image',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T11:50:00.000Z',
      completedAt: '2026-04-18T11:50:09.000Z',
      items: [
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T11:50:01.000Z'
        },
        makeImageGenerationItem('image-latest', 'completed', '2026-04-18T11:50:05.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after image',
          createdAt: '2026-04-18T11:50:07.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getByRole('button', { name: /Processed in 7s/ })).toBeInTheDocument()
    expect(screen.getByText('Generated image')).toBeInTheDocument()
    expect(screen.getByTestId('tool-output-image')).toBeInTheDocument()
    expect(screen.getByText('final response after image')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in 7s/ }))

    expect(screen.getByText('Read main.ts')).toBeInTheDocument()
  })

  it('pins only the latest completed image generation result before the final message', () => {
    const firstImage = 'AQID'
    const latestImage = 'BAUG'
    const turn: ConversationTurn = {
      id: 'turn-folded-two-images',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T12:10:00.000Z',
      completedAt: '2026-04-18T12:10:10.000Z',
      items: [
        makeImageGenerationItem('image-first', 'completed', '2026-04-18T12:10:01.000Z', firstImage),
        {
          id: 'tool-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T12:10:03.000Z'
        },
        makeImageGenerationItem('image-latest', 'completed', '2026-04-18T12:10:06.000Z', latestImage),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response after latest image',
          createdAt: '2026-04-18T12:10:08.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.getAllByTestId('tool-output-image')).toHaveLength(1)
    expect(screen.getByTestId('tool-output-image')).toHaveAttribute(
      'src',
      `data:image/png;base64,${latestImage}`
    )

    fireEvent.click(screen.getByRole('button', { name: /Processed in 8s/ }))

    const images = screen.getAllByTestId('tool-output-image')
    expect(images).toHaveLength(2)
    expect(images.some((image) => image.getAttribute('src') === `data:image/png;base64,${firstImage}`)).toBe(true)
    expect(images.some((image) => image.getAttribute('src') === `data:image/png;base64,${latestImage}`)).toBe(true)
  })
})

describe('AgentResponseBlock interactive card pinning', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        // InteractiveToolView clears its model-context block on unmount (teardown).
        appServer: {
          sendRequest: vi.fn().mockResolvedValue({})
        },
        shell: {
          openExternal: vi.fn().mockResolvedValue(undefined)
        }
      }
    })
    // ToolCallCard reads the active thread to build the iframe src.
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

  function makeMcpAppItem(success: boolean, available: boolean): ConversationItem {
    return {
      id: `mcp-app-${success ? 'success' : 'failure'}-${available ? 'available' : 'unavailable'}`,
      type: 'mcpToolCall',
      status: 'completed',
      toolCallId: 'mcp-app-call',
      toolName: 'issue_write',
      arguments: { method: 'create' },
      result: 'The interactive form awaits user submission.',
      structuredResult: { status: 'awaiting_user_submission' },
      success,
      mcpAppAvailable: available,
      createdAt: '2026-07-18T11:30:03.000Z'
    }
  }

  it.each([
    ['successful', true],
    ['failed', false]
  ])('pins an available MCP App for a %s tool result', (_label, success) => {
    const turn: ConversationTurn = {
      id: `turn-mcp-app-${success ? 'success' : 'failure'}`,
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-07-18T11:30:00.000Z',
      completedAt: '2026-07-18T11:30:06.000Z',
      items: [
        makeMcpAppItem(success, true),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'The form is ready.',
          createdAt: '2026-07-18T11:30:05.000Z'
        }
      ]
    }

    render(<LocaleProvider><AgentResponseBlock turn={turn} /></LocaleProvider>)

    expect(screen.getByTestId('mcp-app-view')).toHaveTextContent('issue_write')
  })

  it('uses the generic failed tool fallback when the MCP App is unavailable', () => {
    const turn: ConversationTurn = {
      id: 'turn-mcp-app-unavailable',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-07-18T11:30:00.000Z',
      completedAt: '2026-07-18T11:30:06.000Z',
      items: [makeMcpAppItem(false, false)]
    }

    render(<LocaleProvider><AgentResponseBlock turn={turn} /></LocaleProvider>)

    expect(screen.queryByTestId('mcp-app-view')).toBeNull()
    expect(screen.getByText(/Failed: Called issue_write/)).toBeInTheDocument()
  })

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
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
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
    // The non-UI tool call stays collapsed until the summary is expanded.
    expect(screen.getByText(/Processed in/)).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: /Processed in/ }))
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
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
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

    fireEvent.click(screen.getByRole('button', { name: /Processed in/ }))
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
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
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

    // Both pinned out of the summary; the plain ReadFile stays collapsed.
    expect(screen.getByText('My Plan')).toBeInTheDocument()
    expect(container.querySelector('.interactive-tool-view__frame')).toBeNull()
    expect(screen.queryByText('Read main.ts')).toBeNull()
  })
})

describe('AgentResponseBlock historical tool trimming', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('hides historical tool details and artifacts while preserving plans and assistant text', () => {
    useConversationStore.setState({
      workspacePath: 'F:/workspace',
      changedFiles: new Map([
        ['docs/old-artifact.md', makeDiff('docs/old-artifact.md', 'turn-trimmed')]
      ])
    })

    const turn: ConversationTurn = {
      id: 'turn-trimmed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-18T12:00:00.000Z',
      completedAt: '2026-04-18T12:00:10.000Z',
      items: [
        {
          id: 'reasoning-1',
          type: 'reasoningContent',
          status: 'completed',
          reasoning: 'private reasoning',
          elapsedSeconds: 2,
          createdAt: '2026-04-18T12:00:01.000Z'
        },
        {
          id: 'read-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'read-call-1',
          toolName: 'ReadFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
          presentation: { presentationId: 'core.read-file' },
          arguments: { path: 'src/main.ts' },
          success: true,
          createdAt: '2026-04-18T12:00:02.000Z'
        },
        {
          id: 'write-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'write-call-1',
          toolName: 'WriteFile',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WriteFile' },
          presentation: { presentationId: 'core.file-write', options: { operation: 'write' } },
          arguments: { path: 'docs/old-artifact.md', content: 'new\n' },
          result: 'Wrote docs/old-artifact.md',
          success: true,
          createdAt: '2026-04-18T12:00:03.000Z'
        },
        {
          id: 'shell-1',
          type: 'toolCall',
          status: 'completed',
          toolCallId: 'shell-call-1',
          toolName: 'Exec',
          source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
          presentation: { presentationId: 'core.shell' },
          arguments: { command: 'npm test' },
          result: 'all green',
          success: true,
          createdAt: '2026-04-18T12:00:04.000Z'
        },
        {
          id: 'tool-result-1',
          type: 'toolResult',
          status: 'completed',
          toolCallId: 'read-call-1',
          result: 'raw tool result',
          success: true,
          createdAt: '2026-04-18T12:00:04.500Z'
        },
        {
          id: 'approval-1',
          type: 'approvalCard',
          status: 'completed',
          approvalType: 'shell',
          approvalOperation: 'npm test',
          approvalTarget: 'F:/workspace',
          approvalState: 'accepted',
          createdAt: '2026-04-18T12:00:05.000Z'
        },
        makeCreatePlanItem('plan-1', 'Visible Plan', '2026-04-18T12:00:06.000Z'),
        makeImageGenerationItem('image-trimmed', 'completed', '2026-04-18T12:00:07.000Z'),
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response stays visible',
          createdAt: '2026-04-18T12:00:08.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} historicalToolContentMode="trimmed" />
      </LocaleProvider>
    )

    expect(screen.getByText('Visible Plan')).toBeInTheDocument()
    expect(screen.getByText('Generated image')).toBeInTheDocument()
    expect(screen.getByTestId('tool-output-image')).toBeInTheDocument()
    expect(screen.getByText('final response stays visible')).toBeInTheDocument()
    const processedSummary = screen.getByRole('button', { name: /Processed in 8s/ })
    expect(processedSummary).toBeInTheDocument()
    expect(screen.queryByText('private reasoning')).toBeNull()
    expect(screen.queryByText('Thought 2s')).toBeNull()
    expect(screen.queryByText('Read main.ts')).toBeNull()
    expect(screen.queryByText(/Ran npm test/)).toBeNull()
    expect(screen.queryByText(/Shell/)).toBeNull()
    expect(screen.queryByText('raw tool result')).toBeNull()
    expect(screen.queryByText('old-artifact.md')).toBeNull()
    expect(screen.queryByText(/file changed/)).toBeNull()

    fireEvent.click(processedSummary)

    expect(screen.getByText('Thought 2s')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Thought 2s/ }))
    expect(screen.getByText('private reasoning')).toBeInTheDocument()
    expect(screen.queryByText('Read main.ts')).toBeNull()
    expect(screen.queryByText(/Ran npm test/)).toBeNull()
    expect(screen.queryByText(/Shell/)).toBeNull()
    expect(screen.queryByText('raw tool result')).toBeNull()
  })
})

describe('AgentResponseBlock guidance user messages', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders guidance user messages inline after the preceding tool call', () => {
    const turn: ConversationTurn = {
      id: 'turn-guidance',
      threadId: 'thread-1',
      status: 'running',
      startedAt: '2026-04-25T10:00:00.000Z',
      items: [
        {
          id: 'initial-user',
          type: 'userMessage',
          status: 'completed',
          text: 'initial request',
          createdAt: '2026-04-25T10:00:00.000Z'
        },
        makeToolCallItem('tool-1', 'call-1', 'FollowupTool', '2026-04-25T10:00:01.000Z'),
        {
          id: 'guidance-user',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'guidance',
          text: 'guide the active turn',
          createdAt: '2026-04-25T10:00:02.000Z'
        },
        {
          id: 'assistant-1',
          type: 'agentMessage',
          status: 'completed',
          text: 'continuing after guidance',
          createdAt: '2026-04-25T10:00:03.000Z'
        }
      ]
    }

    const text = renderBlock(turn)
    const initialIndex = text.indexOf('initial request')
    const toolIndex = text.indexOf('Called FollowupTool')
    const markerIndex = text.indexOf('Steered conversation')
    const guidanceIndex = text.indexOf('guide the active turn')
    const assistantIndex = text.indexOf('continuing after guidance')

    expect(initialIndex).toBe(-1)
    expect(toolIndex).toBeGreaterThan(-1)
    expect(markerIndex).toBeGreaterThan(-1)
    expect(guidanceIndex).toBeGreaterThan(-1)
    expect(assistantIndex).toBeGreaterThan(-1)
    expect(toolIndex).toBeLessThan(guidanceIndex)
    expect(toolIndex).toBeLessThan(markerIndex)
    expect(markerIndex).toBeLessThan(guidanceIndex)
    expect(guidanceIndex).toBeLessThan(assistantIndex)

    const guidanceFlowItem = screen.getByText('guide the active turn').closest('[data-testid="conversation-flow-item"]')
    expect(guidanceFlowItem).toHaveAttribute('data-kind', 'user')
  })

  it('does not fold completed turns that contain guidance user messages', () => {
    const turn: ConversationTurn = {
      id: 'turn-guidance-completed',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2026-04-25T10:00:00.000Z',
      completedAt: '2026-04-25T10:00:10.000Z',
      items: [
        makeToolCallItem('tool-1', 'call-1', 'FollowupTool', '2026-04-25T10:00:01.000Z'),
        {
          id: 'guidance-user',
          type: 'userMessage',
          status: 'completed',
          deliveryMode: 'guidance',
          text: 'guide the active turn',
          createdAt: '2026-04-25T10:00:02.000Z'
        },
        {
          id: 'assistant-final',
          type: 'agentMessage',
          status: 'completed',
          text: 'final response',
          createdAt: '2026-04-25T10:00:05.000Z'
        }
      ]
    }

    render(
      <LocaleProvider>
        <AgentResponseBlock turn={turn} />
      </LocaleProvider>
    )

    expect(screen.queryByText(/Processed in/)).toBeNull()
    expect(screen.getByText('Called FollowupTool')).toBeInTheDocument()
    expect(screen.getByText('Steered conversation')).toBeInTheDocument()
    expect(screen.getByText('guide the active turn')).toBeInTheDocument()
    expect(screen.getByText('final response')).toBeInTheDocument()
  })
})
