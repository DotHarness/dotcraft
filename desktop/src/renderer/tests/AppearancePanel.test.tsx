// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AppearancePanel } from '../components/settings/panels/AppearancePanel'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const setZoomFactor = vi.fn()

beforeEach(() => {
  vi.clearAllMocks()
  for (const attr of ['data-theme', 'data-reduce-motion', 'data-pointer-cursors', 'data-translucent-sidebar']) {
    document.documentElement.removeAttribute(attr)
  }
  settingsGet.mockResolvedValue({})
  settingsSet.mockResolvedValue(undefined)
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn()
    }))
  })
  installDesktopApiMock({
    platform: 'win32',
    window: { setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined), setZoomFactor },
    settings: { get: settingsGet, set: settingsSet }
  })
  useUIStore.setState({ diffMarkers: 'color' })
})

async function renderPanel(): Promise<void> {
  render(
    <LocaleProvider>
      <AppearancePanel />
    </LocaleProvider>
  )
  await waitFor(() => expect(settingsGet).toHaveBeenCalled())
}

describe('AppearancePanel', () => {
  it('persists the theme mode and applies it to the document', async () => {
    await renderPanel()
    fireEvent.click(screen.getByRole('button', { name: 'Dark' }))
    expect(settingsSet).toHaveBeenCalledWith({ theme: 'dark' })
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })

  it('persists reduce motion and reflects it on the document', async () => {
    await renderPanel()
    fireEvent.click(screen.getByRole('button', { name: 'On' }))
    expect(settingsSet).toHaveBeenCalledWith({ reduceMotion: 'on' })
    expect(document.documentElement.getAttribute('data-reduce-motion')).toBe('on')
  })

  it('persists diff markers and updates the UI store', async () => {
    await renderPanel()
    const group = screen.getByRole('group', { name: 'Diff markers' })
    fireEvent.click(within(group).getAllByRole('button')[1])
    expect(settingsSet).toHaveBeenCalledWith({ diffMarkers: 'sign' })
    expect(useUIStore.getState().diffMarkers).toBe('sign')
  })

  it('persists the pointer cursor preference (default on, toggled off)', async () => {
    await renderPanel()
    // Pointer cursors default to on, so the switch starts checked; clicking turns it off.
    fireEvent.click(screen.getByRole('switch', { name: 'Use pointer cursors' }))
    expect(settingsSet).toHaveBeenCalledWith({ pointerCursors: false })
    expect(document.documentElement.getAttribute('data-pointer-cursors')).toBe('false')
  })

  it('clears the accent override when Default is chosen', async () => {
    settingsGet.mockResolvedValue({ accent: '#3e8c64' })
    await renderPanel()
    fireEvent.click(screen.getByRole('button', { name: 'Default accent color' }))
    expect(settingsSet).toHaveBeenCalledWith({ accent: '' })
  })

  it('persists the UI font size as an interface-zoom factor and applies it via the renderer', async () => {
    await renderPanel()
    fireEvent.click(screen.getByRole('button', { name: 'Increase UI font size' }))
    // Default is 14px (zoom 1); +1px -> 15px, persisted/applied as the 15/14 zoom factor.
    expect(settingsSet).toHaveBeenCalledWith({ interfaceZoom: 15 / 14 })
    expect(setZoomFactor).toHaveBeenCalledWith(15 / 14)
  })

  it('persists the translucent sidebar preference (default on, toggled off)', async () => {
    await renderPanel()
    fireEvent.click(screen.getByRole('switch', { name: 'Translucent sidebar' }))
    expect(settingsSet).toHaveBeenCalledWith({ translucentSidebar: false })
    expect(document.documentElement.getAttribute('data-translucent-sidebar')).toBe('false')
  })
})
