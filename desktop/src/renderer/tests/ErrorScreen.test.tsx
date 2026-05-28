import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ErrorScreen } from '../components/ErrorScreen'
import { useConnectionStore } from '../stores/connectionStore'

function renderErrorScreen(onOpenSettings = vi.fn()) {
  render(
    <LocaleProvider>
      <ErrorScreen onOpenSettings={onOpenSettings} />
    </LocaleProvider>
  )
  return onOpenSettings
}

describe('ErrorScreen', () => {
  beforeEach(() => {
    useConnectionStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: vi.fn().mockResolvedValue({ locale: 'en' })
        }
      }
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
  })
})
