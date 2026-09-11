import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import {
  ScreenViewLauncher,
  type ScreenViewLauncherProps
} from '../components/conversation/screenView/ScreenViewLauncher'

function renderLauncher(overrides: Partial<ScreenViewLauncherProps> = {}): void {
  render(
    <LocaleProvider loadSettings={false}>
      <ScreenViewLauncher
        hostId="peer-1"
        hostName="Studio PC"
        online
        capabilities={['tools', 'screen-v1']}
        open={false}
        live={false}
        onToggle={() => undefined}
        {...overrides}
      />
    </LocaleProvider>
  )
}

function launcher(): HTMLElement | null {
  return screen.queryByRole('button', { name: /Studio PC/ })
}

describe('ScreenViewLauncher', () => {
  it('shows for an online routed machine that declared screen-v1', () => {
    renderLauncher()
    expect(launcher()).not.toBeNull()
  })

  it('stays hidden without a route', () => {
    renderLauncher({ hostId: null })
    expect(launcher()).toBeNull()
  })

  it('stays hidden while the machine is offline', () => {
    renderLauncher({ online: false })
    expect(launcher()).toBeNull()
  })

  it('stays hidden when the machine did not declare screen-v1', () => {
    renderLauncher({ capabilities: ['tools'] })
    expect(launcher()).toBeNull()
  })

  it('stays visible, and neutral, while a view is open after the machine drops', () => {
    renderLauncher({ online: false, open: true })
    expect(launcher()).not.toBeNull()
    expect(launcher()).not.toHaveAttribute('data-active')
  })

  it('takes the accent while the open view is live', () => {
    renderLauncher({ open: true, live: true })
    expect(launcher()).toHaveAttribute('data-active')
  })
})
