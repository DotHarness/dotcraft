import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { useConversationStore } from '../stores/conversationStore'
import { useTurnActivityDisclosureStore } from '../stores/turnActivityDisclosureStore'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const asked: ConversationTurn = {
  id: 'turn-asked',
  threadId: 'thread-1',
  status: 'completed',
  startedAt: '2026-06-10T11:00:00.000Z',
  completedAt: '2026-06-10T11:00:10.000Z',
  items: [
    { id: 'prompt', type: 'userMessage', status: 'completed', text: 'clean up', createdAt: '2026-06-10T11:00:00.000Z' },
    { id: 'progress', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'waiting for the build', createdAt: '2026-06-10T11:00:05.000Z' }
  ]
}

const woken: ConversationTurn = {
  id: 'turn-woken',
  threadId: 'thread-1',
  status: 'completed',
  startedAt: '2026-06-10T11:05:00.000Z',
  completedAt: '2026-06-10T11:05:20.000Z',
  items: [
    { id: 'ended', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm run build', triggerStatus: 'completed', triggerRefId: 'call-build' },
    { id: 'answer', type: 'agentMessage', status: 'completed', phase: 'final', text: 'the build passed', createdAt: '2026-06-10T11:05:15.000Z' }
  ]
}

beforeEach(() => {
  useConversationStore.getState().reset()
  useTurnActivityDisclosureStore.setState({ expanded: new Map() })
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: vi.fn().mockResolvedValue({}) }
  })
})

describe('AgentResponseBlock responses spanning turns', () => {
  it('folds the earlier turns of a response into the one process of its last turn', () => {
    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={woken} leadingTurns={[asked]} />
      </LocaleProvider>
    )

    expect(screen.getByText('the build passed')).toBeInTheDocument()
    expect(screen.queryByText('waiting for the build')).toBeNull()
    const toggles = container.querySelectorAll('button[aria-expanded]')
    expect(toggles).toHaveLength(1)

    fireEvent.click(toggles[0])

    expect(toggles[0]).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('waiting for the build').closest('[data-turn-id]')).toHaveAttribute('data-turn-id', 'turn-asked')
    expect(container.querySelectorAll('button[aria-expanded]')).toHaveLength(1)
  })

  it('names a subagent that woke a continuing turn with the row its ending shows mid-turn', () => {
    const reviewed: ConversationTurn = {
      ...woken,
      items: [
        { id: 'reviewed', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'subagent', triggerLabel: 'reviewer', triggerStatus: 'completed', triggerRefId: '/root/reviewer' },
        woken.items[1]
      ]
    }
    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={reviewed} leadingTurns={[asked]} />
      </LocaleProvider>
    )

    fireEvent.click(container.querySelector('button[aria-expanded]')!)

    expect(screen.getByTestId('subagent-ending-chips')).toHaveTextContent('reviewer')
  })
})
