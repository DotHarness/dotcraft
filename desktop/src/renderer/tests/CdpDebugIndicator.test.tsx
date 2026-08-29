import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { CdpDebugIndicator } from '../components/layout/CdpDebugIndicator'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

describe('CdpDebugIndicator', () => {
  beforeEach(() => {
    installDesktopApiMock({
      initialLocale: 'en',
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) }
    })
  })

  it('stays absent when the process did not enable CDP', () => {
    renderIndicator(false)

    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('discloses the active CDP capability', async () => {
    renderIndicator(true)

    const indicator = screen.getByRole('status', {
      name: 'CDP debugging is enabled.'
    })
    fireEvent.focus(indicator)

    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      'CDP debugging is enabled.'
    )
  })
})

function renderIndicator(enabled: boolean): void {
  render(
    <LocaleProvider>
      <CdpDebugIndicator enabled={enabled} />
    </LocaleProvider>
  )
}
