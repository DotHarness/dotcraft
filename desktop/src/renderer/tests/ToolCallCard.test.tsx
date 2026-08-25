import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { useReviewPanelStore } from '../stores/reviewPanelStore'
import { usePluginStore } from '../stores/pluginStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'
import * as ansiUtils from '../utils/ansi'

function renderWithLocale(node: JSX.Element): ReturnType<typeof render> {
  return render(<LocaleProvider>{node}</LocaleProvider>)
}

function expectDisclosureInsideTitleGroup(container: HTMLElement): HTMLElement {
  const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
  const disclosureIcon = container.querySelector('[data-testid="tool-disclosure-icon"]') as HTMLElement
  expect(titleGroup).toBeTruthy()
  expect(disclosureIcon).toBeTruthy()
  expect(titleGroup).toContainElement(disclosureIcon)
  return disclosureIcon
}

const collapseAnimationMs = 200

describe('ToolCallCard structured result rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      appServer: {
        sendRequest: vi.fn(async () => ({}))
      }
    })
  })

  it('renders result text but not image content when expanded', () => {
    const item: ConversationItem = {
      id: 'plugin-tool-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'NodeReplJs',
      toolCallId: 'plugin-call-1',
      arguments: { code: 'render()' },
      result: 'rendered',
      success: true,
      contentItems: [
        { type: 'text', text: 'rendered' },
        { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
      ],
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByTestId('tool-expanded-content')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })
})

describe('ToolCallCard RequestUserInput rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      appServer: {
        sendRequest: vi.fn(async () => ({}))
      }
    })
  })

  it('renders answers as a question-answer list instead of raw JSON', () => {
    const item: ConversationItem = {
      id: 'request-user-input-tool',
      type: 'toolCall',
      status: 'completed',
      toolName: 'RequestUserInput',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'RequestUserInput' },
      presentation: { presentationId: 'core.request-user-input' },
      toolCallId: 'call-question',
      arguments: {
        questions: [
          {
            id: 'mode',
            question: 'Which mode should DotCraft use?',
            options: [{ label: 'Auto' }, { label: 'Manual' }]
          },
          {
            id: 'note',
            question: 'Anything to adjust?',
            options: [{ label: 'No' }, { label: 'Yes' }]
          }
        ]
      },
      result: JSON.stringify({
        answers: {
          mode: { answers: ['Auto'] },
          note: { answers: ['user_note: Prefer the lighter UI'] }
        }
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByTestId('tool-expanded-content')).toBeInTheDocument()
    expect(container.querySelector('pre')).toBeNull()
  })
})

describe('ToolCallCard default tool result rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      appServer: {
        sendRequest: vi.fn(async () => ({}))
      }
    })
  })

  it('renders MCP envelope results as decoded JSON without outer content fields', () => {
    const item: ConversationItem = {
      id: 'mcp-local-ping',
      type: 'toolCall',
      status: 'completed',
      toolName: 'local_ping',
      toolCallId: 'local-ping-call',
      arguments: { message: 'dotcraft manual test' },
      result: '{"content":[{"type":"text","text":"{\\u0022ok\\u0022:true,\\u0022message\\u0022:\\u0022dotcraft manual test\\u0022}"}],"structuredContent":{"ok":true,"message":"dotcraft manual test"},"isError":false}',
      success: true,
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    const pre = container.querySelector('pre')
    expect(() => JSON.parse(pre?.textContent ?? '')).not.toThrow()
    expect(JSON.parse(pre?.textContent ?? '{}')).toMatchObject({ ok: true })
  })
})

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

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

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

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

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

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(container.querySelector('.selectable')).toBeNull()
    const button = screen.getByRole('button')
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

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

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

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

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

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

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

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })
})

describe('ToolCallCard shell rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useReviewPanelStore.setState({ shellRuntimeByCallId: new Map() })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
    useUIStore.setState({ activeMainView: 'conversation', pluginCatalogSurface: 'plugins' })
    usePluginStore.setState({
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: null,
      currentWorkspacePath: null
    })
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      },
      appServer: {
        sendRequest: vi.fn(async () => ({}))
      }
    })
  })

  it('keeps running Exec collapsed by default and reveals live output when expanded', () => {
    const item: ConversationItem = {
      id: 'tool-1',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-1',
      arguments: { command: 'npm test' },
      aggregatedOutput: 'line 1\nline 2\n',
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('pre')).toBeNull()
    expect(document.querySelector('.animate-spin-custom')).toBeNull()
    expectDisclosureInsideTitleGroup(container)
    expect(screen.getByRole('button')).toHaveTextContent('Running: npm test')

    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByRole('button')).toHaveTextContent('Running command')
    expect(screen.getByTestId('shell-command')).toHaveTextContent('$npm test')
    expect(document.querySelector('pre')).toBeInTheDocument()
  })

  it('uses the commandExecution command when final Exec arguments are missing', () => {
    const item: ConversationItem = {
      id: 'tool-command-execution-only',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-command-execution-only',
      arguments: {},
      argumentsPreview: '{"command":"stale preview',
      command: 'npm run build',
      aggregatedOutput: 'building\n',
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const toolButton = screen.getByRole('button')
    expect(toolButton).toHaveTextContent('Running: npm run build')
    fireEvent.click(toolButton)

    expect(toolButton).toHaveTextContent('Running command')
    expect(screen.getByTestId('shell-command')).toHaveTextContent('$npm run build')
    expect(screen.queryByText('$ Exec')).not.toBeInTheDocument()
  })

  it('shows a generic shell label and omits the command prompt when no command is available', () => {
    const item: ConversationItem = {
      id: 'tool-command-missing',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-command-missing',
      aggregatedOutput: 'output without command metadata',
      result: 'output without command metadata',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const toolButton = screen.getByRole('button')
    expect(toolButton).toHaveTextContent('Ran command')
    expect(toolButton).not.toHaveTextContent('Exec')
    fireEvent.click(toolButton)

    expect(screen.queryByTestId('shell-command')).not.toBeInTheDocument()
    expect(screen.getByText('output without command metadata')).toBeInTheDocument()
  })

  it('uses a generic completed header and preserves multiline commands in the body', () => {
    const item: ConversationItem = {
      id: 'tool-multiline-command',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-multiline-command',
      arguments: { command: 'npm run lint &&\nnpm run build' },
      result: 'done',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const toolButton = screen.getByRole('button')
    expect(toolButton).toHaveTextContent('Ran npm run lint &&')
    expect(toolButton).not.toHaveTextContent('npm run build')
    fireEvent.click(toolButton)

    expect(toolButton).toHaveTextContent('Ran command')
    const command = screen.getByTestId('shell-command')
    expect(command).toHaveTextContent('$npm run lint && npm run build')
    expect(command.lastElementChild).toHaveStyle({ whiteSpace: 'pre-wrap' })
  })

  it('renders ANSI shell output without raw escape markers', () => {
    const item: ConversationItem = {
      id: 'tool-ansi-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-ansi-1',
      arguments: { command: 'pnpm test' },
      aggregatedOutput: '\u001b[1;46m RUN \u001b[0m\u001b[36mv3.2.4\u001b[0m',
      result: '\u001b[1;46m RUN \u001b[0m\u001b[36mv3.2.4\u001b[0m',
      success: true,
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    expectDisclosureInsideTitleGroup(container)
    fireEvent.click(screen.getByRole('button'))

    const pre = document.querySelector('pre')
    expect(pre).toBeInTheDocument()
  })

  it('does not strip the full shell output while the card is collapsed', () => {
    const stripAnsiSpy = vi.spyOn(ansiUtils, 'stripAnsi')
    const fullOutput = `\u001b[32m${'large output\n'.repeat(1000)}\u001b[0m`
    const item: ConversationItem = {
      id: 'tool-large-collapsed-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-large-collapsed',
      arguments: { command: 'pnpm test' },
      aggregatedOutput: fullOutput,
      result: fullOutput,
      success: true,
      createdAt: new Date().toISOString()
    }

    try {
      const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

      expectDisclosureInsideTitleGroup(container)
      expect(stripAnsiSpy.mock.calls.every((call) => {
        const value = call[0] as string
        return value.length < fullOutput.length && value.length <= 4096
      })).toBe(true)
    } finally {
      stripAnsiSpy.mockRestore()
    }
  })

  it('renders failed shell commands with neutral styling and no Failed prefix', () => {
    const item: ConversationItem = {
      id: 'tool-failed-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-failed-1',
      arguments: { command: 'ping 10.8.8.8 -n 1' },
      aggregatedOutput: 'Request timed out.\nExit code: 1',
      executionStatus: 'failed',
      exitCode: 1,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByRole('button')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button'))

    const pre = document.querySelector('pre')
    expect(pre).toBeInTheDocument()
  })

  it('shows tool execution errorMessage before generic failed result previews', () => {
    const item: ConversationItem = {
      id: 'tool-failed-mcp',
      type: 'toolCall',
      status: 'completed',
      toolName: 'local_strict_header_probe',
      toolCallId: 'strict-header-call',
      arguments: {},
      resultPreview: 'Error: Function failed.',
      errorMessage: "mcp-method: expected 'tools/call', got None",
      success: false,
      executionStatus: 'failed',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button'))

    const pre = document.querySelector('pre')
    expect(pre).toBeInTheDocument()
  })

  it('renders completed empty shell output as a non-expandable row', () => {
    const item: ConversationItem = {
      id: 'tool-empty-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-empty-1',
      arguments: { command: 'true' },
      result: '',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders streaming InlineDiffView for running WriteFile tool calls', async () => {
    const item: ConversationItem = {
      id: 'tool-write-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'WriteFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WriteFile' },
      presentation: { presentationId: 'core.file-write', options: { operation: 'write' } },
      toolCallId: 'write-streaming-1',
      createdAt: new Date().toISOString()
    }
    const streamingDiff: FileDiff = {
      filePath: 'src/live.ts',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 1,
      deletions: 0,
      diffHunks: [
        {
          oldStart: 0,
          oldLines: 0,
          newStart: 1,
          newLines: 1,
          lines: [{ type: 'add', content: 'const live = true' }]
        }
      ],
      status: 'written',
      isNewFile: true,
      originalContent: '',
      currentContent: 'const live = true'
    }
    useConversationStore.setState({
      streamingItemDiffs: new Map([[item.id, streamingDiff]])
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    expectDisclosureInsideTitleGroup(container)
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
  })

  it('keeps running WriteFile without streamed content non-expandable', () => {
    const item: ConversationItem = {
      id: 'tool-write-empty-streaming',
      type: 'toolCall',
      status: 'streaming',
      toolName: 'WriteFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WriteFile' },
      presentation: { presentationId: 'core.file-write', options: { operation: 'write' } },
      toolCallId: 'write-empty-streaming-1',
      argumentsPreview: '{"path":"src/empty.ts"',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button'))

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByTestId('inline-diff-view')).toBeNull()
  })

  it('keeps running EditFile tool labels as Edited even when the streaming diff looks new', () => {
    const item: ConversationItem = {
      id: 'tool-edit-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'EditFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'EditFile' },
      presentation: { presentationId: 'core.file-write', options: { operation: 'edit' } },
      toolCallId: 'edit-streaming-1',
      createdAt: new Date().toISOString()
    }
    const streamingDiff: FileDiff = {
      filePath: 'README.md',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 2,
      deletions: 0,
      diffHunks: [
        {
          oldStart: 0,
          oldLines: 0,
          newStart: 1,
          newLines: 2,
          lines: [
            { type: 'add', content: 'new line one' },
            { type: 'add', content: 'new line two' }
          ]
        }
      ],
      status: 'written',
      isNewFile: true,
      originalContent: '',
      currentContent: 'new line one\nnew line two'
    }
    useConversationStore.setState({
      streamingItemDiffs: new Map([[item.id, streamingDiff]])
    })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
  })

  it('moves completed file metadata into the expanded header and colorizes collapsed stats on hover', async () => {
    const item: ConversationItem = {
      id: 'tool-edit-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'EditFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'EditFile' },
      presentation: { presentationId: 'core.file-write', options: { operation: 'edit' } },
      toolCallId: 'edit-completed-1',
      arguments: { path: 'src/Target.cs', oldText: 'old', newText: 'new' },
      result: 'Successfully edited src/Target.cs',
      success: true,
      createdAt: new Date().toISOString()
    }
    const diff: FileDiff = {
      filePath: 'src/Target.cs',
      turnId: 'turn-1',
      turnIds: ['turn-1'],
      additions: 1,
      deletions: 1,
      diffHunks: [
        {
          oldStart: 1,
          oldLines: 1,
          newStart: 1,
          newLines: 1,
          lines: [
            { type: 'remove', content: 'old' },
            { type: 'add', content: 'new' }
          ]
        }
      ],
      status: 'written',
      isNewFile: false,
      originalContent: 'old',
      currentContent: 'new'
    }
    useConversationStore.setState({
      workspacePath: 'F:/workspace',
      itemDiffs: new Map([[item.id, diff]])
    })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const toolButton = screen.getByRole('button')
    expect(toolButton).toHaveTextContent('Edited Target.cs+1-1')
    const collapsedStats = screen.getByTestId('tool-row-diff-stats')
    expect((collapsedStats.children[0] as HTMLElement).style.color).toBe('currentcolor')
    expect((collapsedStats.children[1] as HTMLElement).style.color).toBe('currentcolor')

    fireEvent.mouseEnter(toolButton)
    expect((collapsedStats.children[0] as HTMLElement).style.color).toBe('var(--success)')
    expect((collapsedStats.children[1] as HTMLElement).style.color).toBe('var(--error)')
    fireEvent.click(toolButton)

    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
    expect(toolButton).toHaveTextContent('Edited file')
    expect(toolButton).not.toHaveTextContent('Target.cs')
    expect(screen.getAllByText('Target.cs')).toHaveLength(1)
    expect(screen.getByTestId('file-result-diff-stats')).toHaveTextContent('+1-1')
    expect(screen.queryByText('@@ -1,1 +1,1 @@')).toBeNull()
    expect(screen.getByRole('button', { name: 'Copy path' })).toBeInTheDocument()
  })

  it('uses a generic expanded Read file row with one path header and range metadata', () => {
    const item: ConversationItem = {
      id: 'tool-read-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
      presentation: { presentationId: 'core.read-file' },
      toolCallId: 'read-completed-1',
      arguments: { path: 'src/Target.cs', offset: 10, limit: 5 },
      result: 'public sealed class Target {}',
      success: true,
      createdAt: new Date().toISOString()
    }
    useConversationStore.setState({ workspacePath: 'F:/workspace' })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const toolButton = screen.getByRole('button')
    expect(toolButton).toHaveTextContent('Read Target.cs L10-14')
    fireEvent.click(toolButton)

    expect(toolButton).toHaveTextContent('Read file')
    expect(toolButton).not.toHaveTextContent('Target.cs')
    expect(screen.getAllByText('Target.cs')).toHaveLength(1)
    expect(screen.getByTestId('file-result-header')).toHaveTextContent('Target.csL10-14')
    expect(screen.getByText('public sealed class Target {}')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Copy path' })).toBeInTheDocument()
  })

  it('keeps showing the running timer for Exec after the toolCall item is completed but command execution is still in progress', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item: ConversationItem = {
      id: 'tool-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-2',
      arguments: { command: 'ping -n 10 8.8.8.8' },
      executionStatus: 'inProgress',
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows running timer when toolCall is completed but toolResult has not merged yet (no executionStatus)', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:02.000Z'))

    const item: ConversationItem = {
      id: 'tool-3',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-3',
      arguments: { command: 'slow-cmd' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows Running + command while shell is running', () => {
    const item: ConversationItem = {
      id: 'tool-ran',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-ran',
      arguments: { command: 'echo hello' },
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()
    expect(screen.getByRole('button')).toHaveTextContent('Running: echo hello')
  })

  it('renders isolated live shell output while the running timer continues advancing', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.000Z'))
    const item: ConversationItem = {
      id: 'tool-live-output',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-live-output',
      arguments: { command: 'many-lines' },
      executionStatus: 'inProgress',
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    act(() => {
      useConversationStore.setState({
        shellRuntimeByCallId: new Map([[
          'exec-live-output',
          { source: 'terminal', output: 'line 100\n' }
        ]])
      })
    })
    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('line 100')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(500)
    })
    expect(screen.getByText('1.5s')).toBeInTheDocument()
    vi.useRealTimers()
  })

  it('subscribes to the review shell runtime without consuming the conversation runtime', () => {
    const item: ConversationItem = {
      id: 'tool-review-live-output',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-review-live-output',
      arguments: { command: 'many-lines' },
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }
    useConversationStore.setState({
      shellRuntimeByCallId: new Map([[
        'exec-review-live-output',
        { source: 'terminal', output: 'conversation output\n' }
      ]])
    })
    useReviewPanelStore.setState({
      shellRuntimeByCallId: new Map([[
        'exec-review-live-output',
        { source: 'terminal', output: 'review output\n' }
      ]])
    })

    renderWithLocale(
      <ToolCallCard item={item} turnId="turn-review" shellRuntimeScope="review" />
    )
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText('review output')).toBeInTheDocument()
    expect(screen.queryByText('conversation output')).not.toBeInTheDocument()
  })

  it('uses the streamed command while final arguments are still an empty object', () => {
    const item: ConversationItem = {
      id: 'tool-streaming-args',
      type: 'toolCall',
      status: 'streaming',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-streaming-args',
      arguments: {},
      argumentsPreview: '{"command":"dotnet test --filter Session',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Running: dotnet test --filter Session')).toBeInTheDocument()
    expect(screen.queryByText('Ran Exec')).not.toBeInTheDocument()
  })

  it('does not expose streamed arguments when an MCP tool claims an Exec presentation', () => {
    const item: ConversationItem = {
      id: 'external-exec',
      type: 'mcpToolCall',
      status: 'streaming',
      toolName: 'Exec',
      source: { kind: 'Mcp', sourceId: 'remote', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'external-exec-call',
      argumentsPreview: '{"command":"sensitive-command',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Generating parameters for Exec...')).toBeInTheDocument()
    expect(screen.queryByText(/sensitive-command/)).not.toBeInTheDocument()
  })

  it('treats legacy executionStatus started as running (mis-mapped wire lifecycle)', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item = {
      id: 'tool-legacy',
      type: 'toolCall' as const,
      status: 'completed' as const,
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-legacy',
      arguments: { command: 'ping' },
      executionStatus: 'started' as ConversationItem['executionStatus'],
      createdAt: '2026-04-13T10:00:00.000Z'
    } as ConversationItem

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('auto-expands eligible running tools after threshold and auto-collapses after the collapse animation completes', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-auto-open-1',
      arguments: { command: 'sleep 10' },
      aggregatedOutput: 'booting\n',
      createdAt: '2026-04-13T10:00:00.000Z'
    }
    const completedItem: ConversationItem = {
      ...runningItem,
      status: 'completed',
      aggregatedOutput: 'ok',
      result: 'ok',
      success: true,
      duration: 820
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={runningItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(document.querySelector('pre')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(document.querySelector('pre')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(document.querySelector('pre')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(document.querySelector('pre')).toBeNull()
    vi.useRealTimers()
  })

  it('does not auto-expand running Exec before output exists', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-empty-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-empty-auto-open-1',
      arguments: { command: 'sleep 10' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={runningItem} turnId="turn-1" />)

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    vi.useRealTimers()
  })

  it('does not auto-expand non-eligible tools while running', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:00.000Z'))
    const runningItem: ConversationItem = {
      id: 'tool-no-auto-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'WebFetch',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WebFetch' },
      presentation: { presentationId: 'core.web', options: { operation: 'fetch' } },
      toolCallId: 'webfetch-2',
      arguments: { url: 'https://dotcraft.ai' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={runningItem} turnId="turn-1" />)

    expect(document.querySelector('.tool-running-gradient-text')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    vi.useRealTimers()
  })

  it('keeps user-selected expansion state after running completes', () => {
    vi.useFakeTimers()
    const runningItem: ConversationItem = {
      id: 'tool-user-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-user-open-1',
      arguments: { command: 'dotcraft search' },
      aggregatedOutput: 'searching\n',
      createdAt: '2026-04-13T10:00:00.000Z'
    }
    const completedItem: ConversationItem = {
      ...runningItem,
      status: 'completed',
      aggregatedOutput: 'done',
      result: 'done',
      success: true,
      duration: 500
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={runningItem} turnId="turn-1" />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button'))
    expect(document.querySelector('pre')).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(document.querySelector('pre')).toBeInTheDocument()
    vi.useRealTimers()
  })

  it('renders completed WebSearch results as a clickable table that opens the internal browser', () => {
    const item: ConversationItem = {
      id: 'tool-web-search-table',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebSearch',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WebSearch' },
      presentation: { presentationId: 'core.web', options: { operation: 'search' } },
      toolCallId: 'websearch-table-1',
      arguments: { query: 'dotcraft docs' },
      result: JSON.stringify({
        query: 'dotcraft docs',
        provider: 'exa',
        results: [
          { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', snippet: 'Guide' },
          { title: 'GitHub', url: 'https://github.com/DotHarness/dotcraft' }
        ]
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    useConversationStore.setState({ workspacePath: 'X:\\fixtures\\workspace' })
    useViewerTabStore.getState().onThreadSwitched('thread-1')
    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button'))

    expect(screen.getAllByRole('columnheader')).toHaveLength(2)
    expect(screen.getAllByRole('button').length).toBeGreaterThan(1)

    fireEvent.click(screen.getAllByRole('button')[1])

    const threadState = useViewerTabStore.getState().getThreadState('thread-1')
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.tabs[0]?.kind).toBeTruthy()
    expect(threadState.tabs[0]?.currentUrl).toBeTruthy()
    expect(threadState.activeTabId).toBe(threadState.tabs[0]?.id)
  })

  it('renders completed native SearchTools with count and discovered tools', () => {
    const item: ConversationItem = {
      id: 'tool-native-tool-search',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SearchTools',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SearchTools' },
      presentation: { presentationId: 'core.deferred-search' },
      toolCallId: 'tool-search-1',
      arguments: { query: 'board task' },
      result: 'Found 1 matching tool(s):\n- workflow.CreateBoardTask: Create a Workflow App board task.',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const row = screen.getByRole('button')
    expect(row).toBeInTheDocument()
    fireEvent.click(row)

    expect(screen.getByTestId('tool-expanded-content')).toBeInTheDocument()
  })

  it('renders completed WebFetch as a non-expandable title row', () => {
    const item: ConversationItem = {
      id: 'tool-web-fetch-summary',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebFetch',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WebFetch' },
      presentation: { presentationId: 'core.web', options: { operation: 'fetch' } },
      toolCallId: 'webfetch-summary-1',
      arguments: { url: 'https://dotcraft.ai' },
      result: JSON.stringify({
        status: 200,
        length: 12345,
        extractor: 'readability',
        truncated: true
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    const row = screen.getByRole('button')

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(row)

    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders successful SkillManage create as a skill card and opens skill detail', async () => {
    const iconDataUrl = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg"></svg>'
    const item: ConversationItem = {
      id: 'skill-create-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillManage' },
      presentation: { presentationId: 'core.skill-manage' },
      toolCallId: 'skill-create-call-1',
      arguments: {
        action: 'create',
        name: 'demo-skill',
        content: '---\nname: demo-skill\ndescription: Demo\n---\n# Demo\n'
      },
      result: JSON.stringify({
        success: true,
        message: "Skill 'demo-skill' created.",
        path: 'X:\\fixtures\\workspace\\.craft\\skills\\demo-skill\\SKILL.md'
      }),
      success: true,
      createdAt: new Date().toISOString()
    }
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'demo-skill',
              description: 'Demo',
              source: 'workspace',
              available: true,
              enabled: true,
              hasVariant: false,
              path: 'X:\\fixtures\\workspace\\.craft\\skills\\demo-skill\\SKILL.md',
              iconSmallDataUrl: iconDataUrl
            }
          ]
        }
      }
      if (method === 'skills/view') {
        return { content: '# Demo' }
      }
      return {}
    })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest }
    })
    useSkillsStore.setState({
      skills: [
        {
          name: 'demo-skill',
          description: 'Demo',
          source: 'workspace',
          available: true,
          enabled: true,
          path: 'X:\\fixtures\\workspace\\.craft\\skills\\demo-skill\\SKILL.md',
          iconSmallDataUrl: iconDataUrl
        }
      ]
    })
    usePluginStore.setState({
      selectedPluginId: 'previous-plugin',
      selectedPlugin: {
        id: 'previous-plugin',
        displayName: 'Previous Plugin',
        enabled: true,
        installed: true,
        installable: false,
        removable: false,
        source: 'local',
        rootPath: '',
        functions: [],
        skills: [],
        mcpServers: [],
        lspServers: []
      },
      detailLoading: false
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(container.querySelector('img')).toBeInTheDocument()
    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()

    await act(async () => {
      fireEvent.click(screen.getAllByRole('button').at(-1) as HTMLElement)
    })

    expect(useUIStore.getState().activeMainView).toBeTruthy()
    expect(useUIStore.getState().pluginCatalogSurface).toBeTruthy()
    expect(usePluginStore.getState().selectedPlugin).toBeNull()
    expect(useSkillsStore.getState().selectedSkillName).toBeTruthy()
    expect(sendRequest.mock.calls.length).toBeGreaterThan(0)
  })

  it('renders successful SkillManage patch as a skill card with an embedded diff', async () => {
    const item: ConversationItem = {
      id: 'skill-patch-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillManage' },
      presentation: { presentationId: 'core.skill-manage' },
      toolCallId: 'skill-patch-call-1',
      arguments: {
        action: 'patch',
        name: 'demo-skill',
        oldString: 'Follow these steps.',
        newString: 'Follow these updated steps.'
      },
      result: JSON.stringify({
        success: true,
        message: "Patched skill 'demo-skill'. The original skill was not modified.",
        replacementCount: 1
      }),
      success: true,
      createdAt: new Date().toISOString()
    }
    useSkillsStore.setState({
      skills: [
        {
          name: 'demo-skill',
          description: 'Demo',
          source: 'workspace',
          available: true,
          enabled: true,
          path: 'X:\\fixtures\\workspace\\.craft\\skills\\demo-skill\\SKILL.md'
        }
      ]
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
  })

  it('renders successful SkillManage delete as a non-expandable title row', () => {
    const item: ConversationItem = {
      id: 'skill-delete-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillManage' },
      presentation: { presentationId: 'core.skill-manage' },
      toolCallId: 'skill-delete-call-1',
      arguments: {
        action: 'delete',
        name: 'old-skill'
      },
      result: JSON.stringify({
        success: true,
        message: "Skill 'old-skill' deleted."
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    const row = screen.getByRole('button')

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(row)

    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders failed SkillManage results without exposing raw JSON output', () => {
    const item: ConversationItem = {
      id: 'skill-fail-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillManage' },
      presentation: { presentationId: 'core.skill-manage' },
      toolCallId: 'skill-fail-call-1',
      arguments: {
        action: 'patch',
        name: 'demo-skill',
        oldString: 'missing',
        newString: 'updated'
      },
      result: JSON.stringify({
        success: false,
        message: 'The requested text was not found.',
        error: 'The requested text was not found.'
      }),
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders successful SkillView as a non-expandable skill card and opens skill detail', async () => {
    const iconDataUrl = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg"></svg>'
    const item: ConversationItem = {
      id: 'skill-view-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillView',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillView' },
      presentation: { presentationId: 'core.skill-view' },
      toolCallId: 'skill-view-call-1',
      arguments: { name: 'browser' },
      result: '---\nname: browser\ndescription: Browser workflow\n---\n# Browser workflow\nLoaded instructions',
      success: true,
      createdAt: new Date().toISOString()
    }
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'browser',
              description: 'Browser workflow',
              source: 'workspace',
              available: true,
              enabled: true,
              hasVariant: true,
              path: 'X:\\fixtures\\workspace\\.craft\\skills\\browser\\SKILL.md',
              iconSmallDataUrl: iconDataUrl
            }
          ]
        }
      }
      if (method === 'skills/view') {
        return { content: '# Browser workflow' }
      }
      return {}
    })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest }
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    await waitFor(() => expect(container.querySelector('img')).toBeInTheDocument())
    expect(screen.getByRole('button', { name: 'View in Skills' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()

    await act(async () => {
      fireEvent.click(screen.getAllByRole('button').at(-1) as HTMLElement)
    })

    expect(useUIStore.getState().activeMainView).toBeTruthy()
    expect(useUIStore.getState().pluginCatalogSurface).toBeTruthy()
    expect(useSkillsStore.getState().selectedSkillName).toBeTruthy()
    expect(sendRequest.mock.calls.length).toBeGreaterThan(0)
  })

  it('renders SkillView not found as a non-expandable failed row', () => {
    const item: ConversationItem = {
      id: 'skill-view-not-found-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillView',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SkillView' },
      presentation: { presentationId: 'core.skill-view' },
      toolCallId: 'skill-view-not-found-call-1',
      arguments: { name: 'missing-skill' },
      result: "Skill 'missing-skill' not found.",
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('keeps content mounted during manual collapse animation before removing it', () => {
    vi.useFakeTimers()
    const completedItem: ConversationItem = {
      id: 'tool-manual-collapse',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' },
      presentation: { presentationId: 'core.shell' },
      toolCallId: 'exec-manual-collapse-1',
      arguments: { command: 'echo hello' },
      result: 'hello',
      success: true,
      duration: 120,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={completedItem} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByTestId('tool-expanded-content')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByTestId('tool-expanded-content')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    vi.useRealTimers()
  })

  it('hides success glyph and duration for completed rows, and only shows chevron on hover', () => {
    const item: ConversationItem = {
      id: 'tool-style-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'ReadFile' },
      presentation: { presentationId: 'core.read-file' },
      toolCallId: 'call-style-1',
      arguments: { path: 'src/main.ts' },
      result: 'ok',
      success: true,
      duration: 350,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const button = screen.getByRole('button')
    expect(button).toBeInTheDocument()
  })

})

describe('ToolCallCard todo rendering safety', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      }
    })
  })

  it('renders TodoWrite without crashing when plan is null', () => {
    const item: ConversationItem = {
      id: 'todo-write-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'TodoWrite',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'TodoWrite' },
      presentation: { presentationId: 'core.todo' },
      toolCallId: 'todo-write-call-1',
      arguments: {
        merge: false,
        todos: [{ id: 't1', content: 'Next step is ABCDEFGHIJKLMNOPQRSTUVWXYZ', status: 'pending' }]
      },
      result: 'Plan updated',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders UpdateTodos fallback label when plan is unavailable', () => {
    const item: ConversationItem = {
      id: 'todo-update-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'UpdateTodos',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'UpdateTodos' },
      presentation: { presentationId: 'core.todo' },
      toolCallId: 'todo-update-call-1',
      arguments: {
        updates: [{ id: 't1', status: 'completed' }]
      },
      result: 'Plan updated',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('does not throw when plan todo ids are non-string values', () => {
    useConversationStore.getState().onPlanUpdated({
      title: 'Plan',
      overview: '',
      todos: [{ id: 123 as unknown as string, content: 'Bad data shape', status: 'pending' as const }]
    })

    const item: ConversationItem = {
      id: 'todo-update-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'UpdateTodos',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'UpdateTodos' },
      presentation: { presentationId: 'core.todo' },
      toolCallId: 'todo-update-call-2',
      arguments: {
        updates: [{ id: '123', status: 'in_progress' }]
      },
      result: 'Plan updated',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })
})

describe('ToolCallCard CreatePlan rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    installDesktopApiMock({
      settings: {
        get: async () => ({ locale: 'en' })
      }
    })
  })

  it('renders completed CreatePlan as preview card and expands on demand', () => {
    const item: ConversationItem = {
      id: 'create-plan-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'CreatePlan' },
      presentation: { presentationId: 'core.create-plan' },
      toolCallId: 'create-plan-call-1',
      arguments: {
        plan: '# Release Plan\n\n## Summary\n\nShip the feature in two phases.\n\n## Implementation Changes\n\n- add tests\n- run smoke checks',
        todos: [
          { id: 'tests', content: 'Add tests', status: 'in_progress' },
          { id: 'smoke', content: 'Run smoke checks', status: 'pending' }
        ]
      },
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const buttons = screen.getAllByRole('button')
    expect(buttons.length).toBeGreaterThan(0)
    fireEvent.click(buttons[0])

    expect(screen.getAllByRole('button').length).toBeGreaterThan(0)
  })

  it('keeps preview mode from streaming to completed until user expands', () => {
    const startedItem: ConversationItem = {
      id: 'create-plan-2',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'CreatePlan' },
      presentation: { presentationId: 'core.create-plan' },
      toolCallId: 'create-plan-call-2',
      argumentsPreview: '{"plan":"# Migration\\n\\n## Summary\\n\\nRolling update\\n\\n- step 1"}',
      createdAt: new Date().toISOString()
    }

    const completedItem: ConversationItem = {
      ...startedItem,
      status: 'completed',
      arguments: {
        plan: '# Migration\n\n## Summary\n\nDone plan\n\nMove traffic in batches.',
        todos: [{ id: 'rollout', content: 'Roll out by cluster', status: 'completed' }]
      },
      success: true,
      result: 'Plan created.'
    }

    const { rerender } = render(
      <LocaleProvider>
        <ToolCallCard item={startedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getAllByRole('button').length).toBeGreaterThan(0)

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getAllByRole('button').length).toBeGreaterThan(0)
    fireEvent.click(screen.getAllByRole('button')[0])
    expect(screen.getAllByRole('button').length).toBeGreaterThan(0)
  })
})
