// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AddTabPopupWindow } from '../components/detail/AddTabPopupWindow'
import type { AddTabPopupPayload } from '../../shared/addTabMenu'

const payload: AddTabPopupPayload = {
  x: 80,
  y: 44,
  anchor: {
    left: 80,
    top: 10,
    right: 108,
    bottom: 40
  },
  theme: 'dark',
  position: {
    left: 80,
    top: 44,
    width: 210
  },
  items: [
    { action: 'openFile', label: 'Open File', shortcut: 'Ctrl+P', enabled: true },
    { action: 'newBrowser', label: 'Browser', enabled: false },
    { action: 'newTerminal', label: 'Terminal', enabled: true }
  ]
}

beforeEach(() => {
  let payloadListener: ((payload: AddTabPopupPayload) => void) | null = null
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      menu: {
        getAddTabMenuPayload: vi.fn(async () => payload),
        onAddTabMenuPayload: vi.fn((listener: (payload: AddTabPopupPayload) => void) => {
          payloadListener = listener
          return () => {
            payloadListener = null
          }
        }),
        emitAddTabMenuPayload: (nextPayload: AddTabPopupPayload) => payloadListener?.(nextPayload),
        resolveAddTabMenu: vi.fn(async () => undefined)
      }
    }
  })
})

describe('AddTabPopupWindow', () => {
  it('renders themed menu items and resolves enabled choices', async () => {
    render(<AddTabPopupWindow />)

    fireEvent.click(await screen.findByRole('menuitem', { name: /Open File/ }))

    await waitFor(() => {
      expect(window.api.menu.resolveAddTabMenu).toHaveBeenCalledWith('openFile')
    })
  })

  it('keeps disabled items visible but inert', async () => {
    render(<AddTabPopupWindow />)

    const disabled = await screen.findByRole('menuitem', { name: 'Browser' })
    expect((disabled as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(disabled)

    expect(window.api.menu.resolveAddTabMenu).not.toHaveBeenCalled()
  })

  it('dismisses with null on Escape', async () => {
    render(<AddTabPopupWindow />)
    await screen.findByRole('menu')

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(window.api.menu.resolveAddTabMenu).toHaveBeenCalledWith(null)
    })
  })

  it('updates menu content from pushed payloads', async () => {
    window.api.menu.getAddTabMenuPayload = vi.fn(async () => null)
    render(<AddTabPopupWindow />)

    act(() => {
      ;(window.api.menu as typeof window.api.menu & {
        emitAddTabMenuPayload: (nextPayload: AddTabPopupPayload) => void
      }).emitAddTabMenuPayload({
        ...payload,
        theme: 'light',
        position: { left: 24, top: 30, width: 210 },
        items: [
          { action: 'openFile', label: 'Open Something', enabled: true },
          { action: 'newBrowser', label: 'Browser', enabled: true }
        ]
      })
    })

    expect(await screen.findByRole('menuitem', { name: 'Open Something' })).toBeTruthy()
    expect(screen.getByRole('menu').getAttribute('style')).toContain('left: 24px')
  })
})
