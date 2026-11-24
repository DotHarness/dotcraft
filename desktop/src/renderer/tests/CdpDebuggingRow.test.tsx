import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { CdpDebuggingRow } from '../components/settings/panels/CdpDebuggingRow'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'

const set = vi.fn()

function mount(cdpDebugging: boolean) {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: { initialLocale: 'en', settings: { get: vi.fn().mockResolvedValue({ cdpDebugging }), set } }
  })
  return render(
    <LocaleProvider loadSettings={false}>
      <CdpDebuggingRow />
      <ConfirmDialogHost />
    </LocaleProvider>
  )
}

beforeEach(() => {
  set.mockReset().mockResolvedValue(undefined)
})

it('turns CDP on only after the warning is confirmed', async () => {
  mount(false)
  const toggle = screen.getByRole('switch', { name: 'CDP debugging' })

  fireEvent.click(toggle)
  fireEvent.click(await screen.findByRole('button', { name: 'Cancel' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  expect(set).not.toHaveBeenCalled()
  expect(toggle).toHaveAttribute('aria-checked', 'false')

  fireEvent.click(toggle)
  fireEvent.click(await screen.findByRole('button', { name: 'Turn on CDP debugging' }))
  await waitFor(() => expect(set).toHaveBeenCalledWith({ cdpDebugging: true }))
})

it('turns CDP off without a warning', async () => {
  mount(true)
  const toggle = screen.getByRole('switch', { name: 'CDP debugging' })
  await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))

  fireEvent.click(toggle)

  await waitFor(() => expect(set).toHaveBeenCalledWith({ cdpDebugging: false }))
  expect(screen.queryByRole('dialog')).toBeNull()
})
