import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import { DetailPanelLauncher } from '../components/detail/DetailPanelLauncher'
import { LocaleProvider } from '../contexts/LocaleContext'

afterEach(cleanup)

function renderLauncher(canOpenSideChat: boolean): void {
  render(<LocaleProvider loadSettings={false}>
    <DetailPanelLauncher onAction={() => undefined} canOpenWorkspaceTab systemTabsAvailable canOpenSideChat={canOpenSideChat} />
  </LocaleProvider>)
}

it('offers Side chat only while one can be opened', () => {
  renderLauncher(false)
  expect(screen.queryByRole('button', { name: /Side chat/ })).toBeNull()
  cleanup()
  renderLauncher(true)
  expect((screen.getByRole('button', { name: /Side chat/ }) as HTMLButtonElement).disabled).toBe(false)
})
