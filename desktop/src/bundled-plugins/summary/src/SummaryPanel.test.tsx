import '../../../renderer/tests/setupPluginRuntime'
import type { DesktopPluginHost, DesktopPluginSubAgent } from '@dotcraft/plugin'
import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { SummaryPanel } from './SummaryPanel'

const agent = (name: string, state: DesktopPluginSubAgent['state']): DesktopPluginSubAgent => ({
  parentThreadId: 't1', childThreadId: 'child-' + name, agentPath: null, nickname: name, state
})

function renderPanel(subagents: DesktopPluginSubAgent[]): void {
  const host = {
    environment: { locale: 'en' },
    appServer: { request: vi.fn(async () => ({ thread: { plan: null, runtime: {} }, data: [], terminals: [], runs: [], automations: [] })), onNotification: vi.fn(() => () => {}) },
    subagents: { list: () => subagents, onChange: () => () => {} },
    navigation: { openDetailPanel: vi.fn() },
    ui: { showToast: vi.fn() }
  } as unknown as DesktopPluginHost
  render(<SummaryPanel host={host} threadId="t1" workspacePath={null} variant="pinned" />)
}

describe('Summary subagents row', () => {
  it('counts working children and follows with the finished count', async () => {
    renderPanel([agent('a', 'working'), agent('b', 'waiting'), agent('c', 'done')])
    expect(await screen.findByRole('button', { name: 'Open subagents: 2 working, 1 done' })).toBeInTheDocument()
  })

  it('counts only the finished children once none work', async () => {
    renderPanel([agent('a', 'done'), agent('b', 'failed')])
    expect(await screen.findByRole('button', { name: 'Open subagents: 2 done' })).toBeInTheDocument()
  })
})
