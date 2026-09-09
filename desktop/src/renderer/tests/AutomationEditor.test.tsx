import { beforeEach, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AutomationEditor } from '../components/automations/AutomationEditor'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'
import type { AutomationDefinition } from '../types/automation'
const automation: AutomationDefinition = { id: 'a', version: 1, name: 'Check CI', prompt: 'Check changes', status: 'active', executionMode: 'thread', targetThreadId: 't', workspaceMode: 'project', approvalPolicy: 'workspaceScope', notificationPolicy: 'important', schedule: { kind: 'every', everyMs: 60000 }, createdAt: '2026-09-08T00:00:00Z', updatedAt: '2026-09-08T00:00:00Z' }
let request = vi.fn()
beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = vi.fn()
  request = vi.fn(async (method: string) => method === 'automation/runs/list' ? { runs: [] } : {})
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, appServer: { onNotification: () => () => {}, sendRequest: request } })
})
const props = { initial: automation, automation, onClose: vi.fn(), onSaved: vi.fn(), onDirtyChange: vi.fn() }
it('preserves edited text and exposes a failed save', async () => {
  request.mockImplementation(async (method: string) => { if (method === 'automation/update') throw new Error('Connection lost'); return { runs: [] } })
  render(<LocaleProvider><AutomationEditor {...props} /></LocaleProvider>)
  expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Automation name'), { target: { value: 'Keep this draft' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save' }))
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Connection lost'))
  expect(screen.getByLabelText('Automation name')).toHaveValue('Keep this draft')
})
it('syncs clean external updates but protects dirty drafts from conflicts', async () => {
  const view = render(<LocaleProvider><AutomationEditor {...props} /></LocaleProvider>)
  view.rerender(<LocaleProvider><AutomationEditor {...props} automation={{ ...automation, version: 2, name: 'Updated elsewhere' }} /></LocaleProvider>)
  await waitFor(() => expect(screen.getByLabelText('Automation name')).toHaveValue('Updated elsewhere'))
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Automation name'), { target: { value: 'My draft' } })
  view.rerender(<LocaleProvider><AutomationEditor {...props} automation={{ ...automation, version: 3, name: 'New version' }} /></LocaleProvider>)
  expect(screen.getByLabelText('Automation name')).toHaveValue('My draft')
  expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  expect(screen.getByRole('alert')).toHaveTextContent('changed elsewhere')
})
it('keeps lifecycle actions separate and omits the retired complete action', async () => {
  const onAction = vi.fn()
  render(<LocaleProvider><AutomationEditor {...props} onAction={onAction} onDelete={vi.fn()} /></LocaleProvider>)
  await screen.findByText('No runs yet')
  fireEvent.click(screen.getByRole('button', { name: 'Automation actions' }))
  expect(screen.getByText('Run now')).toBeInTheDocument()
  expect(screen.getByText('Delete')).toBeInTheDocument()
  expect(screen.queryByText('Completed automation')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Pause' }))
  expect(onAction).toHaveBeenCalledWith('pause')
})
it('renders completed definitions as read-only history that can still run', async () => {
  const onAction = vi.fn()
  const completed = { ...automation, status: 'completed' as const }
  render(<LocaleProvider><AutomationEditor {...props} automation={completed} initial={completed} onAction={onAction} onDelete={vi.fn()} /></LocaleProvider>)
  await screen.findByText('No runs yet')
  expect(screen.getByLabelText('Automation name')).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Pause' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Automation actions' }))
  fireEvent.click(screen.getByText('Run now'))
  expect(onAction).toHaveBeenCalledWith('run')
})
it('uses a menu time picker, retains off-grid values, and hides the time zone field', async () => {
  const daily = { ...automation, schedule: { kind: 'daily' as const, hour: 9, minute: 7, timeZone: 'America/New_York' } }
  render(<LocaleProvider><AutomationEditor {...props} automation={daily} initial={daily} /></LocaleProvider>)
  await screen.findByText('No runs yet')
  expect(screen.queryByLabelText('Time zone')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('combobox', { name: 'At' }))
  const options = screen.getAllByRole('option')
  expect(options).toHaveLength(97)
  expect(options.find((option) => option.getAttribute('data-value') === '09:07')).toHaveAttribute('aria-selected', 'true')
  fireEvent.click(options.find((option) => option.getAttribute('data-value') === '09:15')!)
  expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
})
