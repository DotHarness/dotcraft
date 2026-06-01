import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ToolCallCard } from '../components/conversation/ToolCallCard'
import { useConversationStore } from '../stores/conversationStore'
import { usePluginStore } from '../stores/pluginStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { getSubAgentAccent } from '../utils/subAgentPresentation'
import type { ConversationItem } from '../types/conversation'
import type { FileDiff } from '../types/toolCall'

function renderWithLocale(node: JSX.Element): ReturnType<typeof render> {
  return render(<LocaleProvider>{node}</LocaleProvider>)
}

function expectRunningGradientText(text: string | RegExp): HTMLElement {
  const label = screen.getByText(text)
  expect(label).toHaveClass('tool-running-gradient-text')
  return label
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

const collapseAnimationMs = 200

describe('ToolCallCard plugin function rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn(async () => ({}))
        }
      }
    })
  })

  it('renders plugin text and image content when expanded', () => {
    const item: ConversationItem = {
      id: 'plugin-tool-1',
      type: 'pluginFunctionCall',
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

    expect(screen.getByText((content) => content.includes('rendered'))).toBeInTheDocument()
    const image = screen.getByRole('img', { name: 'Plugin output 1' })
    expect(image).toHaveAttribute('src', 'data:image/png;base64,abc123')
  })
})

describe('ToolCallCard RequestUserInput rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn(async () => ({}))
        }
      }
    })
  })

  it('renders answers as a question-answer list instead of raw JSON', () => {
    const item: ConversationItem = {
      id: 'request-user-input-tool',
      type: 'toolCall',
      status: 'completed',
      toolName: 'RequestUserInput',
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

    expect(container).toHaveTextContent('Which mode should DotCraft use?: Auto')
    expect(container).toHaveTextContent('Anything to adjust?: Prefer the lighter UI')
    expect(container).not.toHaveTextContent('"answers"')
    expect(container).not.toHaveTextContent('user_note:')
  })
})

describe('ToolCallCard subagent result rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn(async () => ({}))
        }
      }
    })
  })

  it('renders SpawnAgent result with role, external profile, and prompt without raw JSON', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SpawnAgent',
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

    expect(container).toHaveTextContent('Spawned Popper')
    expect(screen.getByText('Popper')).toHaveStyle({ color: getSubAgentAccent('thread_child') })
    expect(screen.getByText('(worker · cursor-cli)')).toBeInTheDocument()
    expect(screen.getByText('Prompt: Create hatch pet')).toBeInTheDocument()
    expect(container.querySelector('span[style*="width: 7px"]')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Open' })).toBeNull()
    expect(screen.queryByText(/childThreadId/)).toBeNull()
    expect(screen.queryByText(/thread_child/)).toBeNull()
  })

  it('renders streaming SpawnAgent from argument preview without raw JSON', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-streaming',
      type: 'toolCall',
      status: 'streaming',
      toolName: 'SpawnAgent',
      toolCallId: 'call-streaming',
      argumentsPreview: '{"agentPrompt":"Review the API surface","agentNickname":"Reviewer"}',
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

    expectRunningGradientText('Spawning agent: Reviewer...')
    expect(container).not.toHaveTextContent('agentPrompt')
    expect(container).not.toHaveTextContent('Review the API surface')
  })

  it('folds WaitAgent message behind an expandable result body', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
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

    expect(container).toHaveTextContent('Received result from Reviewer')
    expect(screen.queryByText('Reviewer completed')).toBeNull()
    expect(screen.queryByText(/thread_child/)).toBeNull()
    expect(screen.queryByText('Detailed child agent result')).toBeNull()
    const disclosureIcon = expectDisclosureInsideTitleGroup(container)
    const button = screen.getByRole('button', { name: 'Expand subagent result' })
    expect(disclosureIcon).toHaveStyle({ opacity: '0' })
    fireEvent.mouseEnter(button)
    expect(disclosureIcon).toHaveStyle({ opacity: '1' })
    fireEvent.click(button)
    expect(screen.getByText('Detailed child agent result')).toBeInTheDocument()
  })

  it('renders running WaitAgent with the shared running gradient label', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-running-wait',
      type: 'toolCall',
      status: 'started',
      toolName: 'WaitAgent',
      toolCallId: 'call-running-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expectRunningGradientText('Waiting for Reviewer')
    expect(document.querySelector('.animate-spin-custom')).toBeNull()
    expect(screen.queryByText(/thread_child/)).toBeNull()
  })

  it('keeps WaitAgent running after toolCall completion until the tool result arrives', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-pending-wait-result',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      toolCallId: 'call-pending-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

    expectRunningGradientText('Waiting for Reviewer')
    expect(screen.queryByText('Received result from Reviewer')).toBeNull()
    expect(screen.queryByText(/thread_child/)).toBeNull()
  })

  it('does not show a stale running state for historical WaitAgent calls without a result', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-historical-missing-result',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      toolCallId: 'call-historical-wait',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.queryByText('Waiting for Reviewer')).toBeNull()
    expect(screen.queryByText('Received result from Reviewer')).toBeNull()
  })

  it('uses subagent store names for WaitAgent and does not fall back to thread ids', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'thread_child',
        parentThreadId: 'parent-1',
        nickname: 'Bench reviewer',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: false
      }
    ])
    const item: ConversationItem = {
      id: 'subagent-tool-store-name',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      toolCallId: 'call-store-name',
      arguments: { childThreadId: 'thread_child' },
      result: JSON.stringify({
        childThreadId: 'thread_child',
        status: 'completed',
        message: 'Done'
      }),
      success: true,
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(container).toHaveTextContent('Received result from Bench reviewer')
    expect(screen.queryByText(/thread_child/)).toBeNull()
  })

  it('renders WaitAgent timeout as a wait timeout rather than a subagent failure', () => {
    const item: ConversationItem = {
      id: 'subagent-tool-timeout',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WaitAgent',
      toolCallId: 'call-timeout',
      arguments: { childThreadId: 'thread_child', agentNickname: 'Reviewer' },
      result: JSON.stringify({
        childThreadId: 'thread_child',
        agentNickname: 'Reviewer',
        status: 'timeout',
        message: 'Timed out waiting for subagent; it may still be running.'
      }),
      success: true,
      createdAt: '2026-05-03T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(container).toHaveTextContent('Timed out waiting for Reviewer')
    expect(screen.queryByRole('button', { name: 'Open' })).toBeNull()
    expect(screen.queryByText(/failed/i)).toBeNull()
    expect(screen.queryByText(/thread_child/)).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Expand subagent result' }))
    expect(screen.getByText('Timed out waiting for subagent; it may still be running.')).toBeInTheDocument()
  })
})

describe('ToolCallCard shell rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: vi.fn(async () => ({}))
        }
      }
    })
  })

  it('keeps running Exec collapsed by default and reveals live output when expanded', () => {
    const item: ConversationItem = {
      id: 'tool-1',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      toolCallId: 'exec-1',
      arguments: { command: 'npm test' },
      aggregatedOutput: 'line 1\nline 2\n',
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.queryByText('line 1')).toBeNull()
    expectRunningGradientText(/Ran npm test/)
    expect(document.querySelector('.animate-spin-custom')).toBeNull()
    expectDisclosureInsideTitleGroup(container)

    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText((content) => content.includes('line 1'))).toBeInTheDocument()
  })

  it('renders ANSI shell output without raw escape markers', () => {
    const item: ConversationItem = {
      id: 'tool-ansi-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
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
    expect(pre?.textContent).toContain(' RUN v3.2.4')
    expect(pre?.textContent).not.toContain('\u001b')
  })

  it('renders failed shell commands with neutral styling and no Failed prefix', () => {
    const item: ConversationItem = {
      id: 'tool-failed-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-failed-1',
      arguments: { command: 'ping 10.8.8.8 -n 1' },
      aggregatedOutput: 'Request timed out.\nExit code: 1',
      executionStatus: 'failed',
      exitCode: 1,
      createdAt: new Date().toISOString()
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByRole('button', { name: /Ran ping 10\.8\.8\.8 -n 1/ })).toBeInTheDocument()
    expect(screen.queryByText(/Failed:/)).toBeNull()
    const titleGroup = container.querySelector('[data-testid="tool-row-title-group"]') as HTMLElement
    expect(titleGroup).toHaveStyle({ color: 'var(--text-dimmed)' })

    fireEvent.click(screen.getByRole('button'))

    const pre = document.querySelector('pre')
    expect(pre?.textContent).toContain('Exit code: 1')
    expect(pre).toHaveStyle({ color: 'var(--text-secondary)' })
  })

  it('renders completed empty shell output as a non-expandable row', () => {
    const item: ConversationItem = {
      id: 'tool-empty-shell',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-empty-1',
      arguments: { command: 'true' },
      result: '',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button', { name: /Ran true/ }))

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByText('Waiting for output...')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders streaming InlineDiffView for running WriteFile tool calls', () => {
    const item: ConversationItem = {
      id: 'tool-write-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'WriteFile',
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

    const filename = screen.getByText('live.ts')
    expect(filename).toBeInTheDocument()
    expect(filename).toHaveAttribute('title', 'src/live.ts')
    expect(screen.getByText(/Created live\.ts \+1/)).toBeInTheDocument()
    expect(screen.queryByText('src/live.ts')).toBeNull()
    expect(screen.queryByText('streaming')).toBeNull()
    expect(screen.queryByText('Waiting for content...')).toBeNull()
  })

  it('keeps running WriteFile without streamed content non-expandable', () => {
    const item: ConversationItem = {
      id: 'tool-write-empty-streaming',
      type: 'toolCall',
      status: 'streaming',
      toolName: 'WriteFile',
      toolCallId: 'write-empty-streaming-1',
      argumentsPreview: '{"path":"src/empty.ts"',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)
    fireEvent.click(screen.getByRole('button', { name: /Writing to empty\.ts/ }))

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByText('Waiting for content...')).toBeNull()
    expect(screen.queryByTestId('inline-diff-view')).toBeNull()
  })

  it('keeps running EditFile tool labels as Edited even when the streaming diff looks new', () => {
    const item: ConversationItem = {
      id: 'tool-edit-streaming',
      type: 'toolCall',
      status: 'started',
      toolName: 'EditFile',
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

    expect(screen.getByText(/Edited README\.md \+2/)).toBeInTheDocument()
    expect(screen.queryByText(/Created README\.md/)).toBeNull()
    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('new line one')).toBeInTheDocument()
    expect(screen.queryByText('Waiting for content...')).toBeNull()
  })

  it('renders completed file diffs embedded with compact filename and stats', () => {
    const item: ConversationItem = {
      id: 'tool-edit-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'EditFile',
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
      itemDiffs: new Map([[item.id, diff]])
    })

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button', { name: /Edited Target\.cs \+1 -1/ }))

    expect(screen.getByTestId('tool-expanded-content')).toHaveStyle({ padding: '0px' })
    expect(screen.getByTestId('inline-diff-view').style.borderStyle).toBe('none')
    const filename = screen.getByText('Target.cs')
    expect(filename).toHaveAttribute('title', 'src/Target.cs')
    expect(screen.queryByText('src/Target.cs')).toBeNull()
  })

  it('keeps showing the running timer for Exec after the toolCall item is completed but command execution is still in progress', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item: ConversationItem = {
      id: 'tool-2',
      type: 'toolCall',
      status: 'completed',
      toolName: 'Exec',
      toolCallId: 'exec-2',
      arguments: { command: 'ping -n 10 8.8.8.8' },
      executionStatus: 'inProgress',
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('1.5s')).toBeInTheDocument()
    expectRunningGradientText(/Ran ping -n 10 8\.8\.8\.8/)
    expect(screen.queryByText('Calling')).not.toBeInTheDocument()

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
      toolCallId: 'exec-3',
      arguments: { command: 'slow-cmd' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" turnRunning />)

    expect(screen.getByText('2.0s')).toBeInTheDocument()

    vi.useRealTimers()
  })

  it('shows Ran + command while shell is running (same style as completed, not Calling Exec)', () => {
    const item: ConversationItem = {
      id: 'tool-ran',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
      toolCallId: 'exec-ran',
      arguments: { command: 'echo hello' },
      executionStatus: 'inProgress',
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expectRunningGradientText(/Ran echo hello/)
    expect(screen.queryByText('Calling')).not.toBeInTheDocument()
  })

  it('treats legacy executionStatus started as running (mis-mapped wire lifecycle)', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-04-13T10:00:01.500Z'))

    const item = {
      id: 'tool-legacy',
      type: 'toolCall' as const,
      status: 'completed' as const,
      toolName: 'Exec',
      toolCallId: 'exec-legacy',
      arguments: { command: 'ping' },
      executionStatus: 'started' as ConversationItem['executionStatus'],
      createdAt: '2026-04-13T10:00:00.000Z'
    } as ConversationItem

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('1.5s')).toBeInTheDocument()

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

    expect(screen.queryByText('Running...')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(screen.getByText((content) => content.includes('booting'))).toBeInTheDocument()
    expect(screen.queryByText('Waiting for output...')).toBeNull()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('ok')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(screen.queryByText('ok')).toBeNull()
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
      toolCallId: 'exec-empty-auto-open-1',
      arguments: { command: 'sleep 10' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={runningItem} turnId="turn-1" />)

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    expect(screen.queryByText('Waiting for output...')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Ran sleep 10/ }))
    expect(screen.queryByText('Waiting for output...')).toBeNull()
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
      toolCallId: 'webfetch-2',
      arguments: { url: 'https://dotcraft.ai' },
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={runningItem} turnId="turn-1" />)

    expect(screen.getByText('0.0s')).toBeInTheDocument()
    expectRunningGradientText('Fetched https://dotcraft.ai')

    act(() => {
      vi.advanceTimersByTime(450)
    })

    expect(screen.queryByText('Running...')).toBeNull()
    vi.useRealTimers()
  })

  it('keeps user-selected expansion state after running completes', () => {
    vi.useFakeTimers()
    const runningItem: ConversationItem = {
      id: 'tool-user-open',
      type: 'toolCall',
      status: 'started',
      toolName: 'Exec',
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
    expect(screen.getByText((content) => content.includes('searching'))).toBeInTheDocument()

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('done')).toBeInTheDocument()
    vi.useRealTimers()
  })

  it('renders completed WebSearch results as a clickable table that opens the internal browser', () => {
    const item: ConversationItem = {
      id: 'tool-web-search-table',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebSearch',
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

    useConversationStore.setState({ workspacePath: 'F:\\dotcraft' })
    useViewerTabStore.getState().onThreadSwitched('thread-1')
    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button', { name: /Searched "dotcraft docs"/ }))

    expect(screen.getByRole('columnheader', { name: 'Title' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Link' })).toBeInTheDocument()
    expect(screen.queryByText('Web search')).toBeNull()
    expect(screen.getAllByText('Searched "dotcraft docs"')).toHaveLength(1)
    expect(screen.getByTestId('tool-expanded-content')).toHaveStyle({ padding: '0px' })
    expect(screen.getByRole('button', { name: 'DotCraft Docs' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'docs.dotcraft.ai' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'DotCraft Docs' }))

    const threadState = useViewerTabStore.getState().getThreadState('thread-1')
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.tabs[0]).toMatchObject({
      kind: 'browser',
      currentUrl: 'https://docs.dotcraft.ai/start'
    })
    expect(threadState.activeTabId).toBe(threadState.tabs[0]?.id)
  })

  it('renders completed native tool_search with count and discovered tools', () => {
    const item: ConversationItem = {
      id: 'tool-native-tool-search',
      type: 'toolCall',
      status: 'completed',
      toolName: 'tool_search',
      toolCallId: 'tool-search-1',
      arguments: { query: 'board task' },
      result: 'Found 1 matching tool(s):\n- oratorio.CreateBoardTask: Create an Oratorio board task.',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const row = screen.getByRole('button', { name: /Searched 1 tool/ })
    expect(row).toBeInTheDocument()
    fireEvent.click(row)

    expect(screen.getByText('Search tools')).toBeInTheDocument()
    expect(screen.getByText('Searched tools: "board task"')).toBeInTheDocument()
    expect(screen.getByText('- oratorio.CreateBoardTask: Create an Oratorio board task.')).toBeInTheDocument()
  })

  it('renders completed WebFetch as a non-expandable title row', () => {
    const item: ConversationItem = {
      id: 'tool-web-fetch-summary',
      type: 'toolCall',
      status: 'completed',
      toolName: 'WebFetch',
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
    const row = screen.getByRole('button', { name: /Fetched https:\/\/dotcraft\.ai/ })

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(row)

    expect(screen.queryByText('200 · 12,345 chars · readability · truncated')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders successful SkillManage create as a skill card and opens skill detail', async () => {
    const iconDataUrl = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg"></svg>'
    const item: ConversationItem = {
      id: 'skill-create-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
      toolCallId: 'skill-create-call-1',
      arguments: {
        action: 'create',
        name: 'demo-skill',
        content: '---\nname: demo-skill\ndescription: Demo\n---\n# Demo\n'
      },
      result: JSON.stringify({
        success: true,
        message: "Skill 'demo-skill' created.",
        path: 'F:\\dotcraft\\.craft\\skills\\demo-skill\\SKILL.md'
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
              path: 'F:\\dotcraft\\.craft\\skills\\demo-skill\\SKILL.md',
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: async () => ({ locale: 'en' }) },
        appServer: { sendRequest }
      }
    })
    useSkillsStore.setState({
      skills: [
        {
          name: 'demo-skill',
          description: 'Demo',
          source: 'workspace',
          available: true,
          enabled: true,
          path: 'F:\\dotcraft\\.craft\\skills\\demo-skill\\SKILL.md',
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

    expect(screen.getByText('Skill')).toBeInTheDocument()
    expect(screen.getByText('Created')).toBeInTheDocument()
    expect(screen.getByText('demo-skill')).toBeInTheDocument()
    expect(container.querySelector('img')).toHaveAttribute('src', iconDataUrl)
    expect(screen.getByTestId('inline-diff-view')).toBeInTheDocument()
    expect(screen.queryByText(/"success"/)).toBeNull()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'View in Skills' }))
    })

    expect(useUIStore.getState().activeMainView).toBe('skills')
    expect(useUIStore.getState().pluginCatalogSurface).toBe('skills')
    expect(usePluginStore.getState().selectedPlugin).toBeNull()
    expect(useSkillsStore.getState().selectedSkillName).toBe('demo-skill')
    expect(sendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    expect(sendRequest).toHaveBeenCalledWith('skills/view', { name: 'demo-skill' })
  })

  it('renders successful SkillManage patch as a skill card with an embedded diff', () => {
    const item: ConversationItem = {
      id: 'skill-patch-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
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
          path: 'F:\\dotcraft\\.craft\\skills\\demo-skill\\SKILL.md'
        }
      ]
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Variant updated')).toBeInTheDocument()
    expect(screen.getByText('Variant')).toBeInTheDocument()
    expect(container.querySelector('img')).toBeNull()
    const filename = screen.getByText('SKILL.md')
    expect(filename).toHaveAttribute('title', 'demo-skill/SKILL.md')
    expect(screen.getByText('Follow these steps.')).toBeInTheDocument()
    expect(screen.getByText('Follow these updated steps.')).toBeInTheDocument()
    expect(screen.queryByText(/"replacementCount"/)).toBeNull()
  })

  it('renders successful SkillManage delete as a non-expandable title row', () => {
    const item: ConversationItem = {
      id: 'skill-delete-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
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
    const row = screen.getByRole('button', { name: /Deleted skill old-skill/ })

    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(row)

    expect(screen.queryByText(/Skill 'old-skill' deleted/)).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders failed SkillManage results without exposing raw JSON output', () => {
    const item: ConversationItem = {
      id: 'skill-fail-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillManage',
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

    expect(screen.getByText(/Failed: Patched skill demo-skill/)).toBeInTheDocument()
    expect(screen.getByText(/The requested text was not found/)).toBeInTheDocument()
    expect(screen.queryByText(/"success"/)).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
  })

  it('renders successful SkillView as a non-expandable skill card and opens skill detail', async () => {
    const iconDataUrl = 'data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg"></svg>'
    const item: ConversationItem = {
      id: 'skill-view-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillView',
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
              path: 'F:\\dotcraft\\.craft\\skills\\browser\\SKILL.md',
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: async () => ({ locale: 'en' }) },
        appServer: { sendRequest }
      }
    })

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Skill')).toBeInTheDocument()
    expect(screen.getByText('Loaded')).toBeInTheDocument()
    expect(screen.getByText('browser')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Variant')).toBeInTheDocument())
    await waitFor(() => expect(container.querySelector('img')).toHaveAttribute('src', iconDataUrl))
    expect(screen.getByText('Skill instructions loaded.')).toBeInTheDocument()
    expect(screen.queryByText('Browser workflow')).toBeNull()
    expect(screen.queryByText('Loaded instructions')).toBeNull()
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()

    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'View in Skills' }))
    })

    expect(useUIStore.getState().activeMainView).toBe('skills')
    expect(useUIStore.getState().pluginCatalogSurface).toBe('skills')
    expect(useSkillsStore.getState().selectedSkillName).toBe('browser')
    expect(sendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    expect(sendRequest).toHaveBeenCalledWith('skills/view', { name: 'browser' })
  })

  it('renders SkillView not found as a non-expandable failed row', () => {
    const item: ConversationItem = {
      id: 'skill-view-not-found-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'SkillView',
      toolCallId: 'skill-view-not-found-call-1',
      arguments: { name: 'missing-skill' },
      result: "Skill 'missing-skill' not found.",
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText(/Failed: Loaded skill missing-skill/)).toBeInTheDocument()
    expect(screen.getByText(/Skill 'missing-skill' not found/)).toBeInTheDocument()
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
      toolCallId: 'exec-manual-collapse-1',
      arguments: { command: 'echo hello' },
      result: 'hello',
      success: true,
      duration: 120,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    renderWithLocale(<ToolCallCard item={completedItem} turnId="turn-1" />)

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('hello')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button'))
    expect(screen.getByText('hello')).toBeInTheDocument()

    act(() => {
      vi.advanceTimersByTime(collapseAnimationMs)
    })

    expect(screen.queryByText('hello')).toBeNull()
    vi.useRealTimers()
  })

  it('hides success glyph and duration for completed rows, and only shows chevron on hover', () => {
    const item: ConversationItem = {
      id: 'tool-style-completed',
      type: 'toolCall',
      status: 'completed',
      toolName: 'ReadFile',
      toolCallId: 'call-style-1',
      arguments: { path: 'src/main.ts' },
      result: 'ok',
      success: true,
      duration: 350,
      createdAt: '2026-04-13T10:00:00.000Z'
    }

    const { container } = renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    const label = screen.getByText('Read main.ts')
    expect(label).toBeInTheDocument()
    expect(document.querySelector('.tool-running-gradient-text')).toBeNull()
    expect(screen.queryByText('✓')).toBeNull()
    expect(screen.queryByText('350ms')).toBeNull()

    const button = screen.getByRole('button')
    const wrapper = button.parentElement as HTMLElement
    const chevron = expectDisclosureInsideTitleGroup(container)
    expect(label).toHaveStyle({ color: 'var(--text-dimmed)' })
    expect(chevron).toHaveStyle({ opacity: '0' })

    fireEvent.mouseEnter(wrapper)
    expect(label).toHaveStyle({ color: 'var(--text-secondary)' })
    expect(chevron).toHaveStyle({ opacity: '1' })
  })

})

describe('ToolCallCard todo rendering safety', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders TodoWrite without crashing when plan is null', () => {
    const item: ConversationItem = {
      id: 'todo-write-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'TodoWrite',
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

    expect(screen.getByText(/Create to-do/)).toBeInTheDocument()
    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /Create to-do/ }))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    expect(screen.queryByText('Plan updated')).toBeNull()
  })

  it('renders UpdateTodos fallback label when plan is unavailable', () => {
    const item: ConversationItem = {
      id: 'todo-update-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'UpdateTodos',
      toolCallId: 'todo-update-call-1',
      arguments: {
        updates: [{ id: 't1', status: 'completed' }]
      },
      result: 'Plan updated',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Updated to-do')).toBeInTheDocument()
    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Updated to-do' }))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    expect(screen.queryByText('Plan updated')).toBeNull()
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
      toolCallId: 'todo-update-call-2',
      arguments: {
        updates: [{ id: '123', status: 'in_progress' }]
      },
      result: 'Plan updated',
      success: true,
      createdAt: new Date().toISOString()
    }

    renderWithLocale(<ToolCallCard item={item} turnId="turn-1" />)

    expect(screen.getByText('Started to-do')).toBeInTheDocument()
    expect(document.querySelector('[data-testid="tool-disclosure-icon"]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Started to-do' }))
    expect(screen.queryByTestId('tool-expanded-content')).toBeNull()
    expect(screen.queryByText('Plan updated')).toBeNull()
  })
})

describe('ToolCallCard CreatePlan rendering', () => {
  beforeEach(() => {
    useConversationStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        }
      }
    })
  })

  it('renders completed CreatePlan as preview card and expands on demand', () => {
    const item: ConversationItem = {
      id: 'create-plan-1',
      type: 'toolCall',
      status: 'completed',
      toolName: 'CreatePlan',
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

    expect(screen.getByText('Plan')).toBeInTheDocument()
    expect(screen.getByText('Release Plan')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Expand plan/ }).length).toBeGreaterThan(0)
    fireEvent.click(screen.getAllByRole('button', { name: /Expand plan/ })[0])

    expect(screen.getAllByText('Ship the feature in two phases.').length).toBeGreaterThan(0)
    expect(screen.getByText('add tests')).toBeInTheDocument()
    expect(screen.getByText('Add tests')).toBeInTheDocument()
    expect(screen.getByText('Run smoke checks')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Collapse/ })).toBeInTheDocument()
  })

  it('keeps preview mode from streaming to completed until user expands', () => {
    const startedItem: ConversationItem = {
      id: 'create-plan-2',
      type: 'toolCall',
      status: 'started',
      toolName: 'CreatePlan',
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

    expect(screen.getByText('Migration')).toBeInTheDocument()
    expect(screen.getAllByText('Rolling update').length).toBeGreaterThan(0)

    rerender(
      <LocaleProvider>
        <ToolCallCard item={completedItem} turnId="turn-1" />
      </LocaleProvider>
    )

    expect(screen.getByText('Migration')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /Expand plan/ }).length).toBeGreaterThan(0)
    fireEvent.click(screen.getAllByRole('button', { name: /Expand plan/ })[0])
    expect(screen.getAllByText('Done plan').length).toBeGreaterThan(0)
    expect(screen.getByText('Roll out by cluster')).toBeInTheDocument()
  })
})
