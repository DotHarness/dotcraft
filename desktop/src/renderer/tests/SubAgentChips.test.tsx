import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentChips } from '../components/conversation/SubAgentChips'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationItem } from '../types/conversation'

// SubAgentControlResult marks ChildThreadId [JsonIgnore], so a real spawn result
// identifies the agent by agentPath only.
function spawnItem(id: string, nickname: string): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName: 'SpawnAgent',
    source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
    presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
    toolCallId: `${id}-call`,
    arguments: { agentNickname: nickname, prompt: `Work on ${nickname}` },
    result: JSON.stringify({
      agentPath: `agent/${nickname}`,
      taskName: nickname,
      agentNickname: nickname,
      status: 'running'
    }),
    success: true,
    createdAt: '2026-05-03T10:00:00.000Z'
  } as ConversationItem
}

/** The store is the source of truth for what an agent is doing; the result only names it. */
function seedChild(nickname: string, overrides: Record<string, unknown> = {}): void {
  useSubAgentStore.setState({
    childrenByParent: new Map([
      [
        'parent-1',
        [
          {
            childThreadId: `thread-${nickname.toLowerCase()}`,
            nickname,
            agentPath: `agent/${nickname}`,
            status: 'running',
            isCompleted: false,
            supportsClose: true,
            runtime: { running: true },
            ...overrides
          }
        ]
      ]
    ])
  } as never)
}

function renderChips(items: ConversationItem[]): void {
  render(
    <LocaleProvider>
      <SubAgentChips items={items} parentThreadId="parent-1" />
    </LocaleProvider>
  )
}

describe('SubAgentChips', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
    useSubAgentStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'parent-1' } as never)
  })

  it('opens the child thread from the chip', () => {
    seedChild('Kepler')
    renderChips([spawnItem('spawn-1', 'Kepler')])

    fireEvent.click(screen.getByRole('button', { name: /Kepler/ }))

    expect(useThreadStore.getState().activeThreadId).toBe('thread-kepler')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('reads as finished once every spawned child is terminal', () => {
    seedChild('Kepler', { status: 'completed', isCompleted: true, runtime: { running: false } })
    renderChips([spawnItem('spawn-1', 'Kepler')])

    expect(screen.getByText('finished')).toBeInTheDocument()
  })

  it('shows the running status while a spawned child is still running', () => {
    seedChild('Kepler')
    renderChips([spawnItem('spawn-1', 'Kepler')])

    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('keeps working while the child is only reachable by agent path', () => {
    // The spawn result carries no thread id, so agentPath is the only join key.
    seedChild('Kepler', { childThreadId: 'thread-unrelated-id' })
    renderChips([spawnItem('spawn-1', 'Kepler')])

    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('does not claim completion when the child is not known yet', () => {
    renderChips([spawnItem('spawn-1', 'Kepler')])

    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('reads as interrupted when a spawn failed', () => {
    const failed = {
      ...spawnItem('spawn-1', 'Kepler'),
      success: false,
      executionStatus: 'failed'
    } as ConversationItem
    renderChips([failed])

    expect(screen.getByText('interrupted')).toBeInTheDocument()
  })

  it('folds chips past the third behind a single expand control', () => {
    renderChips([
      spawnItem('spawn-1', 'Kepler'),
      spawnItem('spawn-2', 'Lagrange'),
      spawnItem('spawn-3', 'Euler'),
      spawnItem('spawn-4', 'Gauss')
    ])

    expect(screen.queryByRole('button', { name: /Gauss/ })).toBeNull()

    fireEvent.click(screen.getByText('+1 more'))

    expect(screen.getByRole('button', { name: /Gauss/ })).toBeInTheDocument()
  })

  it('renders nothing for items that are not a recognisable spawn', () => {
    const { container } = render(
      <LocaleProvider>
        <SubAgentChips items={[]} parentThreadId="parent-1" />
      </LocaleProvider>
    )

    expect(container.firstChild).toBeNull()
  })
})
