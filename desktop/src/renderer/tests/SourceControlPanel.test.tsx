import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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

const P4_EXECUTABLE_NOT_FOUND = {
  status: 'error',
  code: 'P4ExecutableNotFound',
  summary: 'The p4 command was not found.',
  fallbackText: 'The p4 command was not found.',
  identity: {},
  workspace: { altRoots: [] },
  authentication: { loginRequired: false },
  diagnostics: { timeoutSeconds: 30, warningCount: 0, errorCode: 'P4ExecutableNotFound' },
  warnings: [],
  errors: [
    {
      code: 'P4ExecutableNotFound',
      fallbackText: 'The p4 command was not found.'
    }
  ]
}

const P4_CONNECTED = {
  status: 'connected',
  code: 'Connected',
  summary: 'Connected to Perforce.',
  fallbackText: 'Connected to Perforce.',
  identity: {
    serverAddress: 'ssl:p4:1666',
    user: 'alice',
    client: 'game-main-alice',
    connectionMode: 'manual'
  },
  workspace: {
    workspacePath: 'fixtures/sample-app',
    clientRoot: 'fixtures/sample-app',
    altRoots: [],
    mappingOk: true
  },
  authentication: { loginRequired: false, ticketStatus: 'valid' },
  diagnostics: { timeoutSeconds: 30, warningCount: 0, p4Version: 'P4/NTX64/2025.1' },
  warnings: [],
  errors: []
}

const P4_ONLINE_SNAPSHOT = {
  provider: 'perforce',
  effectiveProvider: 'perforce',
  connectionMode: 'manual',
  status: 'connected',
  workspacePath: 'fixtures/sample-app',
  perforce: {
    port: 'ssl:p4:1666',
    client: 'game-main-alice',
    user: 'alice',
    charset: '',
    p4ConfigName: '',
    p4ExecutablePath: '',
    timeoutSeconds: 30,
    online: true,
    autoOffline: true
  },
  capabilities: {
    gitCommit: false,
    perforceBinding: true,
    perforceChangelist: true,
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
    // No successful test yet -> the primary action is just "Save".
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/update',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({ online: false })
        }),
        expect.any(Number)
      )
    })
  })

  it('keeps failed Perforce tests saveable but persists the binding offline', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/test') return Promise.resolve(P4_EXECUTABLE_NOT_FOUND)
      if (method === 'sourceControl/get' || method === 'sourceControl/update') return Promise.resolve(GIT_SNAPSHOT)
      return Promise.resolve({})
    })
    renderPanel()

    fireEvent.click(await screen.findByRole('radio', { name: 'Perforce' }))
    expect(screen.getByText(/This Perforce binding will be saved offline/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Test Connection' }))

    expect(await screen.findAllByText('The p4 command was not found in the server environment. Set the p4 executable path or install the Perforce CLI.')).not.toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/update',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({ online: false })
        }),
        expect.any(Number)
      )
    })
  })

  it('allows successful Perforce tests to persist the binding online', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/test') return Promise.resolve(P4_CONNECTED)
      if (method === 'sourceControl/get' || method === 'sourceControl/update') return Promise.resolve(GIT_SNAPSHOT)
      return Promise.resolve({})
    })
    renderPanel()

    fireEvent.click(await screen.findByRole('radio', { name: 'Perforce' }))
    fireEvent.click(screen.getByRole('button', { name: 'Test Connection' }))

    expect(await screen.findByText('Connected to Perforce.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Save and Bind' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/update',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({ online: true })
        }),
        expect.any(Number)
      )
    })
  })

  it('preserves an unchanged online Perforce binding without requiring another test', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/get' || method === 'sourceControl/update') {
        return Promise.resolve(P4_ONLINE_SNAPSHOT)
      }
      return Promise.resolve({})
    })
    renderPanel()

    expect(await screen.findByRole('radio', { name: 'Perforce' })).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/update',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({ online: true })
        }),
        expect.any(Number)
      )
    })
  })

  it('saves a changed online Perforce connection offline until it is retested', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/get' || method === 'sourceControl/update') {
        return Promise.resolve(P4_ONLINE_SNAPSHOT)
      }
      return Promise.resolve({})
    })
    renderPanel()

    fireEvent.change(await screen.findByLabelText('Server (P4PORT)'), {
      target: { value: 'ssl:p4-alt:1666' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/update',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({
            port: 'ssl:p4-alt:1666',
            online: false
          })
        }),
        expect.any(Number)
      )
    })
  })

  it('uses the configured Perforce timeout for Test Connection requests', async () => {
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'sourceControl/test') return Promise.resolve(P4_CONNECTED)
      if (method === 'sourceControl/get' || method === 'sourceControl/update') return Promise.resolve(GIT_SNAPSHOT)
      return Promise.resolve({})
    })
    renderPanel()

    fireEvent.click(await screen.findByRole('radio', { name: 'Perforce' }))
    fireEvent.change(screen.getByLabelText('Timeout (seconds)'), {
      target: { value: '60' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Test Connection' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'sourceControl/test',
        expect.objectContaining({
          provider: 'perforce',
          perforce: expect.objectContaining({ timeoutSeconds: 60 })
        }),
        65_000
      )
    })
  })
})
