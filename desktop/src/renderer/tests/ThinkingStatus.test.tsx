import { beforeEach, describe, expect, it } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationStore } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { translate } from '../../shared/locales'

const reasoningId = 'reasoning-live-title'

function makeTurn(completed = false): ConversationTurn {
  return {
    id: 'turn-live-title',
    threadId: 'thread-live-title',
    status: completed ? 'completed' : 'running',
    startedAt: '2026-09-28T10:00:00.000Z',
    items: [{
      id: reasoningId,
      type: 'reasoningContent',
      status: completed ? 'completed' : 'streaming',
      reasoning: completed ? 'Hidden body\n**Checking configuration**' : '',
      elapsedSeconds: completed ? 3 : undefined,
      createdAt: '2026-09-28T10:00:00.000Z'
    }]
  }
}

function response(turn: ConversationTurn, reasoning = ''): JSX.Element {
  return (
    <LocaleProvider loadSettings={false}>
      <AgentResponseBlock
        turn={turn}
        isRunning={turn.status === 'running'}
        activeItemIdOverride={reasoningId}
        streamingReasoning={reasoning}
      />
    </LocaleProvider>
  )
}

beforeEach(() => {
  installDesktopApiMock({ initialLocale: 'en' })
  useConversationStore.getState().reset()
  useUIStore.getState().setShowThinkingContent(false)
})

describe('hidden thinking status', () => {
  it('updates the accessible status without exposing a body, then disappears on completion', () => {
    const turn = makeTurn()
    const { rerender } = render(response(turn, 'Hidden body\n**Checking configuration**'))
    const status = screen.getByRole('button', { name: 'Checking configuration' })
    fireEvent.click(status)
    expect(status).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(/Hidden body/)).toBeNull()

    rerender(response(turn, 'Hidden body\n**Checking configuration**\nInspecting settings'))
    expect(screen.getByRole('button', { name: 'Inspecting settings' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('Checking configuration')).toBeNull()
    expect(screen.queryByText(/Hidden body/)).toBeNull()

    rerender(response(makeTurn(true)))
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.queryByText(/Hidden body/)).toBeNull()
  })

  it('falls back to localized Thinking when no status is readable', () => {
    installDesktopApiMock({ initialLocale: 'zh-Hans' })
    render(response(makeTurn(), '<!-- unfinished'))
    expect(screen.getByRole('button', { name: translate('zh-Hans', 'conversation.thinking.streaming') })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(/unfinished/)).toBeNull()
  })

  it('keeps the existing expandable labels when enabled and hides an open body when disabled', () => {
    useUIStore.getState().setShowThinkingContent(true)
    const turn = makeTurn()
    const reasoning = 'Hidden body\n**Checking configuration**'
    const { rerender } = render(response(turn, reasoning))
    fireEvent.click(screen.getByRole('button', { name: 'Thinking' }))
    expect(screen.getByRole('button', { name: 'Thinking' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText(/Hidden body/)).toBeInTheDocument()

    act(() => useUIStore.getState().setShowThinkingContent(false))
    expect(screen.getByRole('button', { name: 'Checking configuration' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText(/Hidden body/)).toBeNull()

    act(() => useUIStore.getState().setShowThinkingContent(true))
    rerender(response(makeTurn(true)))
    expect(screen.getByRole('button', { name: 'Thought 3s' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText(/Hidden body/)).toBeInTheDocument()
  })
})
