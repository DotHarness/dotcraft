import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SourceControlPanel } from '../components/settings/panels/SourceControlPanel'

const appServerSendRequest = vi.fn()
const appServerOnNotification = vi.fn(() => () => {})

const GIT_SNAPSHOT = {
  provider: 'git',
  effectiveProvider: 'git',
  status: 'notTested',
  workspacePath: 'fixtures/sample-app',
  capabilities: {
    gitCommit: true,
    perforceBinding: false,
    perforceChangelist: false,
    perforceShelve: false,
    perforceSubmit: false
  }
}

function renderPanel(): void {
  render(
    <LocaleProvider>
      <SourceControlPanel workspacePath="fixtures/sample-app" />
    </LocaleProvider>
  )
}

describe('SourceControlPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/get' || method === 'sourceControl/update') {
        return Promise.resolve(GIT_SNAPSHOT)
      }
      return Promise.resolve({})
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        appServer: {
          sendRequest: appServerSendRequest,
          onNotification: appServerOnNotification
        }
      }
    })
  })

  it('loads the binding snapshot and shows the provider cards (no Auto)', async () => {
    renderPanel()

    expect(await screen.findByRole('radio', { name: 'Git' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Perforce' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'None' })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'Auto' })).not.toBeInTheDocument()
    expect(appServerSendRequest).toHaveBeenCalledWith('sourceControl/get', {}, expect.any(Number))
  })

  it('reveals the Perforce connection form when Perforce is selected', async () => {
    renderPanel()

    fireEvent.click(await screen.findByRole('radio', { name: 'Perforce' }))

    // The Perforce-only connection fields appear.
    expect(screen.getByLabelText('Server (P4PORT)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Test Connection' })).toBeInTheDocument()
  })

  it('binds via sourceControl/update when Save is clicked', async () => {
    renderPanel()

    fireEvent.click(await screen.findByRole('radio', { name: 'Perforce' }))
    // No successful test yet -> the primary action is "Save Anyway".
    fireEvent.click(screen.getByRole('button', { name: 'Save Anyway' }))

    expect(appServerSendRequest).toHaveBeenCalledWith(
      'sourceControl/update',
      expect.objectContaining({ provider: 'perforce' }),
      expect.any(Number)
    )
  })
})
