import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { BrowserToolbar, type BrowserToolbarProps } from '../components/detail/viewers/BrowserToolbar'
import { installDesktopApiMock } from './desktopApiMock'

const handlers = {
  onBack: vi.fn(),
  onForward: vi.fn(),
  onReload: vi.fn(),
  onStop: vi.fn(),
  onNavigate: vi.fn(),
  onOpenExternal: vi.fn(),
  onAddressDismiss: vi.fn(),
}

function Toolbar(props: Partial<BrowserToolbarProps>) {
  return (
    <LocaleProvider>
      <BrowserToolbar
        url="https://example.com/guide"
        loading={false}
        canGoBack={false}
        canGoForward
        {...handlers}
        {...props}
      />
    </LocaleProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
})

it('disables navigation from props and swaps Reload for Stop while loading', () => {
  const view = render(<Toolbar />)
  expect((screen.getByRole('button', { name: 'Back' }) as HTMLButtonElement).disabled).toBe(true)
  fireEvent.click(screen.getByRole('button', { name: 'Forward' }))
  expect(handlers.onForward).toHaveBeenCalledOnce()
  fireEvent.click(screen.getByRole('button', { name: 'Reload' }))
  expect(handlers.onReload).toHaveBeenCalledOnce()
  view.rerender(<Toolbar loading />)
  fireEvent.click(screen.getByRole('button', { name: 'Stop loading' }))
  expect(handlers.onStop).toHaveBeenCalledOnce()
})

it('submits the address on Enter and hands focus back on Escape', () => {
  render(<Toolbar />)
  const field = screen.getByRole('textbox') as HTMLInputElement
  expect(field.value).toBe('https://example.com/guide')
  fireEvent.change(field, { target: { value: 'example.org' } })
  fireEvent.submit(field.closest('form')!)
  expect(handlers.onNavigate).toHaveBeenCalledWith('example.org')
  fireEvent.keyDown(field, { key: 'Escape' })
  expect(handlers.onAddressDismiss).toHaveBeenCalledOnce()
})

it('opens the external browser and renders supplied controls', () => {
  render(<Toolbar annotate={<span data-testid="annotate" />} controls={<span data-testid="controls" />} />)
  const external = screen.getByRole('button', { name: 'Open in system browser' })
  fireEvent.click(external)
  expect(handlers.onOpenExternal).toHaveBeenCalledOnce()
  expect(screen.getByTestId('annotate')).toBeTruthy()
  expect(screen.getByTestId('controls')).toBeTruthy()
})
