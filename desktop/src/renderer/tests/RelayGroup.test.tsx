import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { RelayGroup } from '../components/settings/panels/phones/RelayGroup'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useMobileStore } from '../stores/mobileStore'
import type { MobileGateway } from '../../shared/mobile'

const RELAY: NonNullable<MobileGateway['relay']> = { url: 'wss://relay.example.test', state: 'connected' }
const clearRelay = vi.fn()

beforeEach(() => {
  clearRelay.mockReset().mockResolvedValue({ ok: true, value: { state: 'on', port: 47610, relay: null, devices: [] } })
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: { initialLocale: 'en', mobile: { clearRelay } }
  })
  useMobileStore.setState({ gateway: { state: 'on', port: 47610, relay: RELAY } })
})

function mount() {
  return render(
    <LocaleProvider loadSettings={false}>
      <RelayGroup relay={RELAY} on />
      <ConfirmDialogHost />
    </LocaleProvider>
  )
}

function openMenu(): void {
  fireEvent.click(screen.getByRole('button', { name: `More actions for ${RELAY.url}` }))
}

it('opens the existing relay for editing from its overflow menu', () => {
  mount()
  openMenu()
  fireEvent.click(screen.getByRole('menuitem', { name: 'Edit' }))
  expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  expect(screen.getByRole('textbox', { name: 'Relay address' })).toHaveValue(RELAY.url)
})

it('requires confirmation before removing the relay and leaves it intact on cancel', async () => {
  mount()
  openMenu()
  fireEvent.click(screen.getByRole('menuitem', { name: 'Remove' }))
  expect(clearRelay).not.toHaveBeenCalled()
  await act(async () => fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' })))
  expect(clearRelay).not.toHaveBeenCalled()

  openMenu()
  fireEvent.click(screen.getByRole('menuitem', { name: 'Remove' }))
  await act(async () => fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Remove' })))
  expect(clearRelay).toHaveBeenCalledOnce()
  expect(useMobileStore.getState().gateway?.relay).toBeNull()
})
