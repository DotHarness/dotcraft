import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { BrowserDownloadHistory } from '../components/settings/BrowserDownloadHistory'
import { BrowserDownloadsSettings } from '../components/settings/BrowserDownloadsSettings'
import { installDesktopApiMock } from './desktopApiMock'
import { useUIStore } from '../stores/uiStore'
import type { BrowserDownloadRecord, BrowserFeedbackEvent } from '../../shared/viewer/browserFeedback'

let records: BrowserDownloadRecord[]
let listener: (event: BrowserFeedbackEvent) => void
const api = {
  downloads: vi.fn(async () => records),
  onFeedback: (callback: typeof listener) => { listener = callback; return () => {} },
  cancelDownload: vi.fn(async () => {}),
  openDownload: vi.fn(async () => {}),
  removeDownload: vi.fn(async ({ id }: { id?: string }) => { records = records.filter(record => record.state === 'progressing' || (id !== undefined && record.id !== id)) }),
  downloadLocation: vi.fn(async () => '/downloads'),
  changeDownloadLocation: vi.fn(async () => '/chosen')
}
beforeEach(() => {
  vi.clearAllMocks()
  records = ['complete', 'active'].map((id, index) => ({ id, tabId: 'tab', filename: `${id}.txt`, url: `https://example.com/${id}`,
    path: `/downloads/${id}.txt`, receivedBytes: 10, totalBytes: 20, state: index ? 'progressing' : 'completed', startedAt: 1 }))
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) }, workspace: { viewer: { browser: api } } })
})
it('searches real records and routes open, cancel and clear actions by ID', async () => {
  render(<LocaleProvider><BrowserDownloadHistory /></LocaleProvider>)
  await screen.findByText('complete.txt')
  fireEvent.click(screen.getByRole('button', { name: 'Open' }))
  await waitFor(() => expect(api.openDownload).toHaveBeenCalledWith({ id: 'complete' }))
  fireEvent.change(screen.getByRole('textbox', { name: 'Search download history' }), { target: { value: 'active' } })
  expect(screen.queryByText('complete.txt')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(api.cancelDownload).toHaveBeenCalledWith({ id: 'active' }))
  fireEvent.click(screen.getByRole('button', { name: 'Clear all' }))
  await waitFor(() => expect(api.removeDownload).toHaveBeenCalledWith({}))
  expect(records.map(record => record.id)).toEqual(['active'])
  await act(async () => listener({ type: 'downloads', records: [] }))
  expect(screen.getByText('No matching downloads')).toBeTruthy()
})
it('changes the saved location and opens the history subpage', async () => {
  render(<LocaleProvider><BrowserDownloadsSettings /></LocaleProvider>)
  await screen.findByText('/downloads')
  fireEvent.click(screen.getByRole('button', { name: 'Change' }))
  await screen.findByText('/chosen')
  fireEvent.click(screen.getByRole('button', { name: 'Manage' }))
  expect(useUIStore.getState().browserDownloadHistoryOpen).toBe(true)
})
