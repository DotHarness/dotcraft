import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { QueuedInputDock } from '../components/conversation/QueuedInputDock'
import { useSubAgentStore } from '../stores/subAgentStore'
import { buildComposerInputParts } from '../utils/composeInputParts'

describe('QueuedInputDock', () => {
  it('summarizes the request without showing encoded attachment metadata', () => {
    const text = 'Review the attached notes.'
    const { inputParts } = buildComposerInputParts({ text, contexts: [{ kind: 'pastedText', id: 'paste', path: '/fixture/paste.txt', fileName: 'paste.txt', preview: 'review', characterCount: 5000 }] })
    render(<LocaleProvider><QueuedInputDock queuedInputs={[{ id: 'queued', threadId: 'task', status: 'queued', createdAt: '', displayText: text, nativeInputParts: inputParts }]} /></LocaleProvider>)
    expect(screen.getByText('Review the attached notes.')).toBeInTheDocument()
    expect(screen.queryByText(/dotcraft-context/)).toBeNull()
  })
  beforeEach(() => {
    vi.clearAllMocks()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) }
    })
    useSubAgentStore.getState().reset()
  })

  it('uses the running shimmer for queued guidance that is pending', () => {
    render(
      <LocaleProvider>
        <QueuedInputDock
          queuedInputs={[
            {
              id: 'queued-guidance',
              threadId: 'parent-1',
              displayText: 'Check the final spacing.',
              status: 'guidancePending',
              createdAt: '2026-05-03T00:02:00.000Z'
            }
          ]}
          onQueueSteer={vi.fn()}
        />
      </LocaleProvider>
    )

    const steeringButton = screen.getByRole('button', { name: 'Steering' })
    expect(steeringButton).toHaveAttribute('aria-pressed', 'true')
    expect(steeringButton.querySelector('.tool-running-gradient-text')).toHaveTextContent('Steering')
  })

  it('renders nothing without queued input, since running subagents are chips in the turn', () => {
    useSubAgentStore.setState({
      childrenByParent: new Map([
        [
          'parent-1',
          [
            {
              childThreadId: 'thread-child',
              nickname: 'Reviewer',
              status: 'running',
              isCompleted: false,
              agentPath: 'agent/reviewer',
              supportsClose: true
            }
          ]
        ]
      ])
    } as never)

    const { container } = render(
      <LocaleProvider>
        <QueuedInputDock queuedInputs={[]} />
      </LocaleProvider>
    )

    expect(container.firstChild).toBeNull()
  })
})
