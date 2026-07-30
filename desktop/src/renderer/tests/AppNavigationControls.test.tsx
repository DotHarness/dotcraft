import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { AppNavigationControls } from '../components/layout/AppNavigationControls'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useAppNavigationStore, type AppNavigationLocation } from '../stores/appNavigationStore'

const first: AppNavigationLocation = {
  kind: 'conversation',
  threadId: null,
  detailVisible: false,
  activeDetailTab: { kind: 'launcher' },
  selectedChangedFile: null
}

beforeEach(() => {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      platform: 'win32',
      initialLocale: 'en',
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    }
  })
  useAppNavigationStore.getState().reset(first)
})

describe('AppNavigationControls', () => {
  it('exposes localized disabled buttons and enables Back when history grows', () => {
    render(
      <LocaleProvider>
        <AppNavigationControls />
      </LocaleProvider>
    )

    const back = screen.getByRole('button', { name: 'Back' })
    const forward = screen.getByRole('button', { name: 'Forward' })
    expect(back).toBeDisabled()
    expect(forward).toBeDisabled()

    act(() => {
      useAppNavigationStore.getState().push({ kind: 'settings', tab: 'general' })
    })
    expect(back).toBeEnabled()
    expect(forward).toBeDisabled()

    fireEvent.click(back)
    expect(back).toBeDisabled()
    expect(forward).toBeEnabled()
  })

  it('does not render on macOS', () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'darwin',
        initialLocale: 'en',
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
      }
    })

    render(
      <LocaleProvider>
        <AppNavigationControls />
      </LocaleProvider>
    )

    expect(screen.queryByTestId('app-navigation-controls')).not.toBeInTheDocument()
  })
})
