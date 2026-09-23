import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ImportSettings } from '@dotcraft/sdk/contracts'

import { ImportPanel } from '../components/settings/panels/ImportPanel'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { translate } from '../../shared/locales'
import { installDesktopApiMock } from './desktopApiMock'

type NotificationHandler = (payload: { method: string; params: unknown; foreground?: boolean }) => void

const sendRequest = vi.fn()
let notify: NotificationHandler | null = null
let settingsFixture: ImportSettings
let runFailure: Error | null = null

const en = (key: string, vars?: Record<string, string | number>): string => translate('en', key, vars)

function detectionFixture(): unknown {
  return {
    sources: [
      { source: 'claude-code', available: true, importableCount: 3, sessions: [] },
      { source: 'codex', available: true, importableCount: 0, sessions: [] },
      { source: 'cursor', available: false, importableCount: 0, sessions: [] }
    ]
  }
}

function requestsFor(method: string): unknown[][] {
  return sendRequest.mock.calls.filter(([name]) => name === method)
}

function importButton(source: string): Promise<HTMLElement> {
  return screen.findByRole('button', { name: en('settings.import.source.importFrom', { source }) })
}

async function renderLoadedPanel(): Promise<void> {
  render(
    <LocaleProvider>
      <ImportPanel />
    </LocaleProvider>
  )
  const toggle = await screen.findByRole('switch', { name: en('settings.import.sync.label') })
  await waitFor(() => expect(toggle).toBeEnabled())
  await importButton('Claude Code')
}

async function confirmImport(source: string): Promise<void> {
  fireEvent.click(await importButton(source))
  const dialog = await screen.findByRole('dialog')
  fireEvent.click(within(dialog).getByRole('button', { name: en('settings.import.dialog.confirm') }))
  await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
}

describe('ImportPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notify = null
    runFailure = null
    settingsFixture = { syncEnabled: false, sources: ['codex'], syncIntervalMinutes: 720, workspaceOptOut: false }
    useToastStore.setState({ toasts: [] })
    sendRequest.mockImplementation(async (method: string, params: Record<string, unknown>) => {
      switch (method) {
        case 'import/settings/get':
          return { settings: settingsFixture }
        case 'import/settings/set':
          settingsFixture = { ...settingsFixture, ...params }
          return { settings: settingsFixture }
        case 'import/sessions/detect':
          return detectionFixture()
        case 'import/sessions/run':
          if (runFailure) throw runFailure
          return { importId: 'imp_1' }
        default:
          throw new Error(`Unexpected request: ${method}`)
      }
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest,
        onNotification: (handler) => {
          notify = handler as NotificationHandler
          return () => {
            notify = null
          }
        }
      }
    })
  })

  it('loads sync settings and detects every configured source on mount', async () => {
    await renderLoadedPanel()

    expect(requestsFor('import/settings/get')).toEqual([['import/settings/get', {}]])
    expect(requestsFor('import/sessions/detect')).toEqual([['import/sessions/detect', {}, expect.any(Number)]])
    expect(await importButton('Claude Code')).toBeEnabled()
    expect(await importButton('ChatGPT')).toBeDisabled()
    expect(
      screen.queryByRole('button', { name: en('settings.import.source.importFrom', { source: 'Cursor' }) })
    ).not.toBeInTheDocument()
  })

  it('saves the sync toggle through import/settings/set', async () => {
    await renderLoadedPanel()
    const toggle = screen.getByRole('switch', { name: en('settings.import.sync.label') })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('import/settings/set', { syncEnabled: true }))
    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))
  })

  it('registers the source for sync before running the import it confirms', async () => {
    await renderLoadedPanel()

    await confirmImport('Claude Code')

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods.indexOf('import/settings/set')).toBeLessThan(methods.indexOf('import/sessions/run'))
    expect(sendRequest).toHaveBeenCalledWith('import/settings/set', { sources: ['codex', 'claude-code'] })
    expect(sendRequest).toHaveBeenCalledWith('import/sessions/run', { sources: ['claude-code'] })
    expect(await importButton('Claude Code')).toHaveAttribute('aria-busy', 'true')
  })

  it('leaves sync settings alone when the source is already a sync source', async () => {
    settingsFixture = { ...settingsFixture, sources: ['claude-code'] }
    await renderLoadedPanel()

    await confirmImport('Claude Code')

    expect(requestsFor('import/settings/set')).toEqual([])
    expect(sendRequest).toHaveBeenCalledWith('import/sessions/run', { sources: ['claude-code'] })
  })

  it('shows the running pass when another import already owns the workspace', async () => {
    runFailure = Object.assign(new Error('An import pass is already running.'), { data: { code: 'import_busy' } })
    await renderLoadedPanel()

    await confirmImport('Claude Code')

    expect(await importButton('Claude Code')).toBeDisabled()
    expect(await importButton('ChatGPT')).toBeDisabled()
    act(() => {
      notify?.({
        method: 'import/sessions/progress',
        foreground: true,
        params: { importId: 'imp_sync', source: 'codex', completed: 1, total: 4 }
      })
    })
    expect(await importButton('ChatGPT')).toHaveAttribute('aria-busy', 'true')
  })

  it('re-detects, refreshes settings, and reports failures when an import completes', async () => {
    await renderLoadedPanel()
    expect(requestsFor('import/sessions/detect')).toHaveLength(1)

    act(() => {
      notify?.({
        method: 'import/sessions/completed',
        foreground: true,
        params: {
          importId: 'imp_1',
          trigger: 'manual',
          startedAt: '2026-09-23T07:00:00Z',
          completedAt: '2026-09-23T07:00:05Z',
          outcomes: [
            { source: 'claude-code', sourceId: 'a', status: 'imported', threadId: 'thread_import_claude-code_a' },
            { source: 'claude-code', sourceId: 'b', status: 'failed', errorCode: 'parse_failed' }
          ]
        }
      })
    })

    await waitFor(() => expect(requestsFor('import/sessions/detect')).toHaveLength(2))
    await waitFor(() => expect(requestsFor('import/settings/get')).toHaveLength(2))
    expect(useToastStore.getState().toasts.map((toast) => toast.type)).toEqual(['warning'])
  })
})
