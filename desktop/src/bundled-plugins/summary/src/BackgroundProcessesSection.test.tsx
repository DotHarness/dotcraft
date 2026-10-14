import '../../../renderer/tests/setupPluginRuntime'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { BackgroundProcessesSection } from './BackgroundProcessesSection'
import { startSummaryFeed, type SummaryState } from './feed'
import { stringsFor } from './i18n'

describe('Summary background processes section', () => {
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
    const host = {
      appServer: { request, onNotification: vi.fn(() => () => {}) },
      subagents: { list: () => [], onChange: () => () => {} },
      ui: { showToast: vi.fn() }
    } as unknown as DesktopPluginHost
    let state: SummaryState | null = null
    const stop = startSummaryFeed(host, 't1', (next) => { state = next })
    await waitFor(() => expect(state!.terminals).toHaveLength(1))

    render(<BackgroundProcessesSection host={host} terminals={state!.terminals} strings={stringsFor('en')} />)
    fireEvent.click(screen.getByRole('button', { name: 'Stop all background terminals' }))

    await waitFor(() => expect(request).toHaveBeenCalledWith('terminal/stop', { sessionId: 'dev-server' }))
    expect(request).not.toHaveBeenCalledWith('terminal/stop', { sessionId: 'build' })
    expect(request.mock.calls.some(([method]) => method === 'terminal/clean')).toBe(false)
    stop()
  })
})
