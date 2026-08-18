import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ErrorScreen } from '../components/ErrorScreen'
import { useConnectionStore } from '../stores/connectionStore'
import { installDesktopApiMock } from './desktopApiMock'

function renderErrorScreen(onOpenSettings = vi.fn()) {
  render(
    <LocaleProvider>
      <ErrorScreen onOpenSettings={onOpenSettings} />
    </LocaleProvider>
  )
  return onOpenSettings
}

describe('ErrorScreen', () => {
  let retryConnection: ReturnType<typeof vi.fn>

  beforeEach(() => {
    useConnectionStore.getState().reset()
    retryConnection = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({
      appServer: { retryConnection },
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    })
  })

  it('opens Settings instead of retrying for invalid remote config', () => {
    const onOpenSettings = vi.fn()
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'Remote WebSocket URL is invalid.',
      errorType: 'remote-config-invalid'
    })

    renderErrorScreen(onOpenSettings)

    fireEvent.click(screen.getByRole('button', { name: 'Open Settings' }))
    expect(onOpenSettings).toHaveBeenCalledOnce()
    expect(retryConnection).not.toHaveBeenCalled()
  })

  it('retries a generic startup error without restarting the managed AppServer', async () => {
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'Managed AppServer failed during startup.'
    })

    renderErrorScreen()

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => {
      expect(retryConnection).toHaveBeenCalledWith({ restartManaged: false })
    })
  })

  it('retries a handshake timeout by restarting the managed AppServer', async () => {
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'AppServer is not responding. Restart?',
      errorType: 'handshake-timeout'
    })

    renderErrorScreen()

    fireEvent.click(screen.getByRole('button', { name: 'Restart' }))

    await waitFor(() => {
      expect(retryConnection).toHaveBeenCalledWith({ restartManaged: true })
    })
  })

  it('disables retry while pending and re-enables after failure with details', async () => {
    let rejectRetry: (error: Error) => void = () => {}
    retryConnection.mockReturnValueOnce(new Promise((_resolve, reject) => {
      rejectRetry = reject
    }))
    useConnectionStore.getState().setStatus({
      status: 'error',
      errorMessage: 'Managed AppServer failed during startup.'
    })

    renderErrorScreen()

    const retryButton = screen.getByRole('button', { name: 'Retry' })
    fireEvent.click(retryButton)

    await waitFor(() => {
      expect(retryButton).toBeDisabled()
    })

    rejectRetry(new Error('Hub still unavailable'))

    await waitFor(() => {
      expect(retryButton).not.toBeDisabled()
    })
    expect(screen.getByText(/Retry failed: Hub still unavailable/)).toBeInTheDocument()
  })
})
