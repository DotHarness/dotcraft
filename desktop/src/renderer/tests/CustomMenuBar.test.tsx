import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { CustomMenuBar } from '../components/layout/CustomMenuBar'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

const popupAll = vi.fn().mockResolvedValue(undefined)
const popupTopLevel = vi.fn().mockResolvedValue(undefined)
const toggleMaximize = vi.fn().mockResolvedValue(undefined)

function installApi(platform: 'win32' | 'linux', maximized: boolean): void {
  installDesktopApiMock({
    platform,
    initialLocale: 'zh-Hans',
    window: {
      isMaximized: vi.fn().mockResolvedValue(maximized),
      onMaximizedChange: () => () => undefined,
      toggleMaximize
    },
    menu: { popupAll, popupTopLevel },
    updates: {
      getState: vi.fn().mockResolvedValue({ status: 'idle', currentVersion: '' }),
      onStateChanged: () => () => undefined,
      onOpenDialog: () => () => undefined
    }
  })
}

beforeEach(() => {
  popupAll.mockClear()
  popupTopLevel.mockClear()
  toggleMaximize.mockClear()
})

afterEach(() => vi.restoreAllMocks())

describe('CustomMenuBar', () => {
  it('opens the existing application menu when top-level menus cannot fit', async () => {
    vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockImplementation(function (this: HTMLElement) {
      return this.classList.contains('dotcraft-custom-menu-bar') ? 480 : 0
    })
    vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function (this: HTMLElement) {
      if (this.className.includes('prefix')) return 150
      if (this.className.includes('menus')) return 250
      if (this.hasAttribute('data-window-controls')) return 138
      return 0
    })
    installApi('win32', false)
    render(<LocaleProvider loadSettings={false}><CustomMenuBar /></LocaleProvider>)
    await act(async () => {})

    fireEvent.mouseDown(screen.getByRole('button', { name: '菜单' }))

    expect(popupAll).toHaveBeenCalledOnce()
    expect(popupTopLevel).not.toHaveBeenCalled()
  })

  it('keeps Linux window controls with a restore action when maximized', async () => {
    installApi('linux', true)
    render(<LocaleProvider loadSettings={false}><CustomMenuBar /></LocaleProvider>)

    const restore = await waitFor(() => screen.getByRole('button', { name: 'Restore' }))
    fireEvent.click(restore)
    expect(toggleMaximize).toHaveBeenCalledOnce()
  })
})
