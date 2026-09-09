import { beforeEach, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { AutomationRunHistory } from '../components/automations/AutomationRunHistory'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAutomationsStore } from '../stores/automationsStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'
import type { AutomationRun } from '../types/automation'

let runs: AutomationRun[]
let archived: Set<string>
let failArchive: Set<string>
const request = vi.fn()
beforeEach(() => {
  runs = ['one', 'two', 'three'].map((id, index) => ({ id, automationId: 'a', definitionVersion: 1,
    status: 'succeeded', threadId: index === 2 ? 'other' : 'shared', turnId: id,
    createdAt: `2026-09-09T0${index}:00:00Z`, deliveryStatus: 'sent' }))
  archived = new Set(); failArchive = new Set()
  useAutomationsStore.setState({ runs: {}, pendingActions: {} })
  useThreadStore.setState({ runtimeSnapshots: new Map(), threadList: [], activeThreadId: null })
  request.mockReset()
  request.mockImplementation(async (method, params) => {
    if (method === 'automation/runs/list') return { runs }
    if (method === 'thread/read') return { thread: { id: params.threadId, displayName: params.threadId, status: archived.has(params.threadId) ? 'archived' : 'active', turns: [] } }
    if (method === 'thread/archive') { if (failArchive.has(params.threadId)) throw Error('offline'); archived.add(params.threadId); return {} }
    if (method === 'thread/unarchive') { archived.delete(params.threadId); return {} }
    if (method === 'automation/runs/read') {
      runs = runs.map(run => params.runIds.includes(run.id) ? { ...run, readAt: params.read ? new Date().toISOString() : null } : run)
      return { runs: runs.filter(run => params.runIds.includes(run.id)) }
    }
    return {}
  })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, appServer: { onNotification: () => () => {}, sendRequest: request } })
})
const mount = () => render(<LocaleProvider><AutomationRunHistory automationId="a" automationName="Task" /></LocaleProvider>)

it('marks one record without changing its siblings and offers the inverse action', async () => {
  mount()
  const rows = await screen.findAllByRole('listitem')
  fireEvent.contextMenu(rows[0])
  fireEvent.click(screen.getByText('Mark as read'))
  await waitFor(() => expect(useAutomationsStore.getState().runs.a.find(run => run.id === 'one')?.readAt).toBeTruthy())
  expect(useAutomationsStore.getState().runs.a.find(run => run.id === 'two')?.readAt).toBeFalsy()
  fireEvent.contextMenu(rows[0])
  fireEvent.click(screen.getByText('Mark as unread'))
  await waitFor(() => expect(useAutomationsStore.getState().runs.a.find(run => run.id === 'one')?.readAt).toBeNull())
})

it('restores archived chats without navigating or marking their runs read', async () => {
  archived.add('shared'); mount()
  const buttons = await screen.findAllByRole('button', { name: 'Unarchive' })
  fireEvent.click(screen.getAllByRole('button', { name: /shared, Succeeded/ })[0])
  expect(useThreadStore.getState().activeThreadId).toBeNull()
  fireEvent.click(buttons[0])
  await waitFor(() => expect(screen.queryByRole('button', { name: 'Unarchive' })).not.toBeInTheDocument())
  expect(useThreadStore.getState().threadList.some(thread => thread.id === 'shared')).toBe(true)
  expect(request.mock.calls.some(([method]) => method === 'automation/runs/read')).toBe(false)
})

it('deduplicates bulk archive and preserves failed chats for retry', async () => {
  failArchive.add('other'); mount()
  await screen.findAllByRole('button', { name: /shared, Succeeded/ })
  fireEvent.click(screen.getAllByRole('button', { name: 'Previous run actions' })[0])
  fireEvent.click(screen.getByText('Archive all'))
  const dialog = screen.getByRole('dialog')
  expect(dialog).toHaveTextContent('2 associated chats')
  fireEvent.click(within(dialog).getByRole('button', { name: 'Archive' }))
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Archived 1 chats; could not archive 1'))
  expect(request.mock.calls.filter(([method, params]) => method === 'thread/archive' && params.threadId === 'shared')).toHaveLength(1)
  expect(archived.has('other')).toBe(false)
})

it('does not offer archiving a chat with an active turn', async () => {
  useThreadStore.setState({ runtimeSnapshots: new Map([['shared', { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }]]) })
  mount()
  const row = (await screen.findAllByRole('button', { name: /shared, Succeeded/ }))[0].closest('article')!
  fireEvent.contextMenu(row)
  expect(screen.getByText('Archive').closest('[role="menuitem"]')).toBeDisabled()
})
