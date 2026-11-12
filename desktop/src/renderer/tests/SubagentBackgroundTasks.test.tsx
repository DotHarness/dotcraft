import { act, cleanup, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { BackgroundTasksSection } from '../components/conversation/summary/BackgroundTasksSection'
import { SubagentRunningTasks } from '../components/detail/SubagentRunningTasks'
import { reportLayout, setPopoverOpen, useSummaryView } from '../components/conversation/summary/view'
import { disposeSummaryFeeds, useRunningTaskCount } from '../components/conversation/summary/store'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { useSubAgentStore, isSubAgentChildWorking } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../plugins/desktopPluginSubAgents'

const source = { connectionKey: 'workspace', connectionId: 'connection', workspaceKey: 'workspace',
  capabilities: { subAgentSessions: true } }
const listeners = new Set<(payload: unknown) => void>()
const terminal = { sessionId: 'term-grandchild', threadId: 'grandchild', turnId: 'done-turn', callId: 'exec-call',
  command: 'build child project', status: 'running' }
const run = { runId: 'run-child', threadId: 'child', name: 'Child work', status: 'running', createdAt: '2026-11-12T00:00:00Z' }
const request = vi.fn()

function notify(method: string, params: unknown) {
  for (const listener of listeners) listener({ ...source, method, params })
}

const services = {
  subagents: { list: listDesktopPluginSubAgents, onChange: onDesktopPluginSubAgentsChange },
  appServer: { connectionKey: () => source.connectionKey, onConnectionChange: () => () => {},
    request: (method: string, params: unknown) => request(source.connectionKey, method, params),
    onNotification: () => () => {} },
  ui: { showError: vi.fn() }
} as unknown as SummaryServices

function Tasks() {
  return <BackgroundTasksSection services={services} threadId="root" terminals={[]} workflows={[]} workflowCalls={new Map()} />
}

function Count() {
  const count = useRunningTaskCount(services, 'root')
  return <output aria-label="Task count">{count}</output>
}

beforeEach(() => {
  listeners.clear()
  reportLayout(null)
  setPopoverOpen(false)
  request.mockReset()
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  useConnectionStore.setState({ status: 'connected', ...source })
  registerThreadConnection('root', source)
  vi.stubGlobal('window', Object.assign(window, { api: { appServer: {
    sendConnectionRequest: request,
    onNotificationRaw: (listener: (payload: unknown) => void) => { listeners.add(listener); return () => listeners.delete(listener) }
  } } }))
  request.mockImplementation(async (_connection, method, params) => {
    if (method === 'subagent/children/list') {
      const id = params.parentThreadId === 'root' ? 'child' : params.parentThreadId === 'child' ? 'grandchild' : null
      return { data: id ? [{ edge: { parentThreadId: params.parentThreadId, childThreadId: id, status: 'open', agentNickname: id },
        thread: { id, runtime: { running: false } } }] : [] }
    }
    if (method === 'terminal/list') return { terminals: params.threadId === 'grandchild' ? [terminal] : [] }
    if (method === 'terminal/stop') return { terminal: { ...terminal, status: 'killed' } }
    if (method === 'workflow/run/list') return { runs: params.threadId === 'child' ? [run] : [] }
    if (method === 'workflow/run/read') return { run }
    if (method === 'workflow/run/stop') return { run: { ...run, status: 'stopped' } }
    return { data: [] }
  })
})

afterEach(() => {
  cleanup()
  disposeSummaryFeeds()
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  useConnectionStore.getState().reset()
  releaseThreadConnection('root')
  vi.unstubAllGlobals()
})

describe('Subagent background task tree', () => {
  it('discovers unopened descendants and routes individual controls to their real owners', async () => {
    render(<LocaleProvider loadSettings={false}><Count /><Tasks /></LocaleProvider>)
    const stop = await screen.findByRole('button', { name: 'Stop background terminal' })
    await waitFor(() => expect(screen.getByRole('status', { name: 'Task count' })).toHaveTextContent('2'))
    const child = useSubAgentStore.getState().childrenByParent.get('root')![0]
    expect(isSubAgentChildWorking(child)).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: /build child project/ }))
    expect(useSubAgentStore.getState().selectedChildByParent.get('root')).toBe('grandchild')
    expect(useDetailPanelStore.getState().getCurrentPanel().activeDetailTab).toEqual({ kind: 'system', id: 'subagents' })
    fireEvent.click(stop)
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Stop background terminal' })).toBeNull())
    expect(request).toHaveBeenCalledWith(source.connectionKey, 'terminal/stop', { sessionId: terminal.sessionId }, undefined, { connectionId: source.connectionId })
    expect(screen.getByRole('status', { name: 'Task count' })).toHaveTextContent('1')

    fireEvent.click(screen.getByRole('button', { name: 'Stop workflow' }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Stop workflow' })).toBeNull())
    expect(request).toHaveBeenCalledWith(source.connectionKey, 'workflow/run/stop', { threadId: 'child', runId: run.runId }, undefined, { connectionId: source.connectionId })
    expect(screen.getByRole('status', { name: 'Task count' })).toHaveTextContent('0')
    expect(isSubAgentChildWorking(child)).toBe(false)
  })

  it('opens the shared Summary from the child entry and follows the same live task state', async () => {
    const summary = renderHook(useSummaryView)
    const view = render(<LocaleProvider loadSettings={false}><Tasks /></LocaleProvider>)
    await screen.findByRole('button', { name: 'Stop background terminal' })
    view.rerender(<LocaleProvider loadSettings={false}>
      <Tasks />
      <section aria-label="Child detail"><SubagentRunningTasks threadId="child" /></section>
    </LocaleProvider>)
    const detail = within(screen.getByRole('region', { name: 'Child detail' }))
    fireEvent.click(await detail.findByRole('button', { name: '2 running tasks' }))
    expect(summary.result.current.popoverOpen).toBe(true)
    act(() => { notify('terminal/completed', { terminal: { ...terminal, status: 'completed' } }) })
    expect(detail.getByRole('button', { name: '1 running task' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Stop workflow' }))
    await waitFor(() => expect(detail.queryByRole('button')).toBeNull())
  })
})
