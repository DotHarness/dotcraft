import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { SummaryPanel } from '../components/conversation/summary/SummaryPanel'
import { SourcesDetail } from '../components/conversation/summary/SourcesTab'
import { desktopSummaryServices, type SummaryServices } from '../components/conversation/summary/summaryServices'
import { disposeSummaryFeeds } from '../components/conversation/summary/store'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { installDesktopApiMock } from './desktopApiMock'

const threadId = 'thread-sources'
const turnId = 'turn-sources'

beforeEach(() => {
  installDesktopApiMock({
    settings: { get: async () => ({ environmentSetupTipDismissed: true }) }
  })
  useDetailPanelStore.getState().onWorkspaceSwitched('/project')
  useDetailPanelStore.getState().onThreadSwitched(threadId)
})

afterEach(() => {
  cleanup()
  disposeSummaryFeeds()
  useDetailPanelStore.getState().reset()
})

it('opens the right detail tab from a single historical MCP source', async () => {
  const call = {
    id: 'call',
    type: 'mcpToolCall',
    status: 'completed',
    createdAt: '2026-11-23T09:00:00Z',
    payload: { callId: 'call', server: 'tracker:issues', toolName: 'lookup', status: 'completed' }
  }
  const request = vi.fn(async (method: string) => {
    if (method === 'thread/turns/list') return { data: [{ id: turnId, items: [], itemCount: 1 }] }
    if (method === 'thread/items/list') return { data: [{ turnId, item: call }], nextCursor: 'unused-more-items' }
    return {}
  })
  const services: SummaryServices = {
    ...desktopSummaryServices,
    appServer: {
      request: request as SummaryServices['appServer']['request'],
      connectionKey: () => 'sources-fixture',
      onConnectionChange: () => () => {},
      onNotification: () => () => {}
    }
  }
  render(<LocaleProvider loadSettings={false}>
    <SummaryPanel services={services} threadId={threadId} workspacePath={null} busy={false} variant="popover" />
  </LocaleProvider>)

  const preview = await screen.findByRole('region', { name: 'Sources' })
  const open = await within(preview).findByRole('button', { name: 'View all' })
  expect(within(preview).queryByRole('button', { name: 'tracker:issues' })).toBeNull()
  fireEvent.click(open)
  expect(useDetailPanelStore.getState().getCurrentPanel()).toMatchObject({
    activeDetailTab: { kind: 'system', id: 'sources' },
    detailPanelPreferredVisible: true,
    openSystemTabs: ['sources']
  })
  expect(request.mock.calls.filter(([method]) => method === 'thread/items/list')).toHaveLength(1)
})

it('expands every recorded call of a tool without turning the server name into a link', () => {
  const openExternal = vi.fn(async () => undefined)
  const services: SummaryServices = {
    ...desktopSummaryServices,
    navigation: { ...desktopSummaryServices.navigation, openExternal }
  }
  render(<LocaleProvider loadSettings={false}>
    <SourcesDetail
      services={services}
      threadId={threadId}
      ready
      webSearch={{ searches: 1, queries: ['issue triage'], pages: [{ url: 'https://example.com/triage', title: 'Triage guide' }] }}
      sources={[{
        kind: 'mcp',
        id: 'tracker:issues',
        title: 'tracker:issues',
        target: null,
        toolGroups: [{
          name: 'lookup',
          calls: [
            { turnId, callId: 'one', status: 'completed', input: { id: 7 }, output: 'Open' },
            { turnId, callId: 'two', status: 'failed', output: 'Not found' }
          ]
        }]
      }]}
    />
  </LocaleProvider>)

  const detail = screen.getByRole('tabpanel', { name: 'Sources' })
  expect(within(detail).queryByRole('button', { name: /tracker:issues/ })).toBeNull()
  const tool = within(detail).getByRole('button', { name: /lookup/ })
  expect(tool.getAttribute('aria-expanded')).toBe('false')
  expect(within(detail).queryByText('Open')).toBeNull()
  fireEvent.click(tool)
  expect(tool.getAttribute('aria-expanded')).toBe('true')
  expect(within(detail).getByText(/"id": 7/)).toBeTruthy()
  expect(within(detail).getByText('Open')).toBeTruthy()
  expect(within(detail).getByText('Not found')).toBeTruthy()

  fireEvent.click(within(detail).getByRole('button', { name: /Opened 1 page/ }))
  fireEvent.click(within(detail).getByRole('button', { name: 'Triage guide' }))
  expect(openExternal).toHaveBeenCalledWith('https://example.com/triage')
})
