import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ReactElement } from 'react'
import { BackgroundTasksSection } from '../components/conversation/summary/BackgroundTasksSection'
import { startSummaryFeed, type SummaryState } from '../components/conversation/summary/feed'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { LocaleProvider } from '../contexts/LocaleContext'

const renderWithLocale = (ui: ReactElement) => render(ui, {
  wrapper: ({ children }) => <LocaleProvider loadSettings={false}>{children}</LocaleProvider>
})

describe('Summary background tasks section', () => {
  it('stops only the listed terminals, leaving a command of the active turn running', async () => {
    const request = vi.fn(async (method: string): Promise<unknown> => {
      if (method === 'thread/read') return { thread: { plan: null, runtime: { activeTurnId: 'turn-active' } } }
      if (method === 'terminal/list') {
        return {
          terminals: [
            { sessionId: 'dev-server', threadId: 't1', turnId: 'turn-old', command: 'npm run dev', status: 'running' },
            { sessionId: 'build', threadId: 't1', turnId: 'turn-active', command: 'npm run build', status: 'running' }
          ]
        }
      }
      if (method === 'terminal/stop') return { terminal: {} }
      return {}
    })
    const services = {
      appServer: { request, onNotification: vi.fn(() => () => {}) },
      subagents: { list: () => [], onChange: () => () => {} },
      ui: { showError: vi.fn() }
    } as unknown as SummaryServices
    let state: SummaryState | null = null
    const stop = startSummaryFeed(services, 't1', (next) => { state = next })
    await waitFor(() => expect(state!.terminals).toHaveLength(1))

    renderWithLocale(<BackgroundTasksSection services={services} threadId="t1" terminals={state!.terminals} workflows={[]} workflowCalls={new Map()} />)
    fireEvent.click(screen.getByRole('button', { name: 'Stop all background terminals' }))

    await waitFor(() => expect(request).toHaveBeenCalledWith('terminal/stop', { sessionId: 'dev-server' }))
    expect(request).not.toHaveBeenCalledWith('terminal/stop', { sessionId: 'build' })
    expect(request.mock.calls.some(([method]) => method === 'terminal/clean')).toBe(false)
    stop()
  })

  it('reveals the command that started a terminal when its row is clicked', async () => {
    const revealToolCall = vi.fn()
    const services = {
      appServer: { request: vi.fn(), onNotification: vi.fn(() => () => {}) },
      navigation: { revealToolCall },
      ui: { showError: vi.fn() }
    } as unknown as SummaryServices
    renderWithLocale(<BackgroundTasksSection
      services={services}
      threadId="t1"
      terminals={[{ sessionId: 'dev-server', command: 'npm run dev', callId: 'call-dev' }]}
      workflows={[]}
      workflowCalls={new Map()}

    />)
    fireEvent.click(screen.getByRole('button', { name: /npm run dev/ }))
    expect(revealToolCall).toHaveBeenCalledWith('t1', 'call-dev')
  })

  it('lists a running workflow that reveals its call and stops only that run', async () => {
    const revealToolCall = vi.fn()
    const request = vi.fn(async () => ({ run: {} }))
    const services = {
      appServer: { request, onNotification: vi.fn(() => () => {}) },
      navigation: { revealToolCall },
      ui: { showError: vi.fn() }
    } as unknown as SummaryServices
    renderWithLocale(<BackgroundTasksSection
      services={services}
      threadId="t1"
      terminals={[]}
      workflows={[{ runId: 'run_1', name: 'review-pr', paused: true }]}
      workflowCalls={new Map([['run_1', 'call-wf']])}

    />)
    fireEvent.click(screen.getByRole('button', { name: /review-pr/ }))
    expect(revealToolCall).toHaveBeenCalledWith('t1', 'call-wf')
    fireEvent.click(screen.getByRole('button', { name: 'Stop workflow' }))
    await waitFor(() => expect(request).toHaveBeenCalledWith('workflow/run/stop', { threadId: 't1', runId: 'run_1' }))
  })
})
