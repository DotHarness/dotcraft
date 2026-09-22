import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

describe('turn activity disclosure lifecycle', () => {
  beforeEach(() => { installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } }) })

  it('collapses activity when final streaming begins, preserves guidance, and restores partial content on stop', () => {
    const turn: ConversationTurn = {
      id: 'turn', threadId: 'thread', status: 'running', startedAt: '2026-01-01T12:00:00Z',
      items: [
        { id: 'progress', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'Inspecting the example.', createdAt: '2026-01-01T12:00:01Z' },
        { id: 'guidance', type: 'userMessage', status: 'completed', deliveryMode: 'guidance', text: 'Keep existing behavior.', createdAt: '2026-01-01T12:00:02Z' }
      ]
    }
    const ui = (value: ConversationTurn, running: boolean) => <LocaleProvider loadSettings={false}><AgentResponseBlock turn={value} isRunning={running} /></LocaleProvider>
    const { rerender } = render(ui(turn, true))
    expect(screen.getByText('Inspecting the example.')).toBeInTheDocument()
    const answering: ConversationTurn = { ...turn, items: [...turn.items,
      { id: 'final', type: 'agentMessage', phase: 'final', status: 'streaming', text: 'Here is the partial answer.', createdAt: '2026-01-01T12:00:12Z' }
    ] }
    rerender(ui(answering, true))
    expect(screen.queryByText('Inspecting the example.')).toBeNull()
    expect(screen.getByText('Keep existing behavior.')).toBeInTheDocument()
    expect(screen.getByText('Here is the partial answer.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    expect(screen.getByText('Inspecting the example.')).toBeInTheDocument()
    rerender(ui({ ...answering, status: 'cancelled', completedAt: '2026-01-01T12:00:20Z' }, false))
    expect(screen.getByText('Inspecting the example.')).toBeInTheDocument()
    expect(screen.getByText('Here is the partial answer.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { expanded: false })).toBeNull()
    expect(screen.getAllByRole('status')).toHaveLength(1)
  })
})
