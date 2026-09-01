import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentChips } from '../components/conversation/SubAgentChips'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationItem } from '../types/conversation'

function spawnItem(id: string, nickname: string, childThreadId: string): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName: 'SpawnAgent',
    source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
    presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
    toolCallId: `${id}-call`,
    arguments: { agentNickname: nickname, prompt: `Work on ${nickname}` },
    result: JSON.stringify({ childThreadId, agentNickname: nickname, agentPath: `agent/${nickname}` }),
    success: true,
    createdAt: '2026-05-03T10:00:00.000Z'
  } as ConversationItem
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
    renderChips([spawnItem('spawn-1', 'Kepler', 'thread-kepler')])

    fireEvent.click(screen.getByRole('button', { name: /Kepler/ }))

    expect(useThreadStore.getState().activeThreadId).toBe('thread-kepler')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('reads as finished once no spawned child is still running', () => {
    renderChips([spawnItem('spawn-1', 'Kepler', 'thread-kepler')])

    expect(screen.getByText('finished')).toBeInTheDocument()
  })

  it('shows the running status while a spawned child is still running', () => {
    useSubAgentStore.setState({
      childrenByParent: new Map([
        [
          'parent-1',
          [
            {
              childThreadId: 'thread-kepler',
              nickname: 'Kepler',
              status: 'running',
              isCompleted: false,
              agentPath: 'agent/Kepler',
              supportsClose: true,
              runtime: { running: true }
            }
          ]
        ]
      ])
    } as never)

    renderChips([spawnItem('spawn-1', 'Kepler', 'thread-kepler')])

    expect(screen.getByText('started working')).toBeInTheDocument()
  })

  it('folds chips past the third behind a single expand control', () => {
    renderChips([
      spawnItem('spawn-1', 'Kepler', 'thread-1'),
      spawnItem('spawn-2', 'Lagrange', 'thread-2'),
      spawnItem('spawn-3', 'Euler', 'thread-3'),
      spawnItem('spawn-4', 'Gauss', 'thread-4')
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
