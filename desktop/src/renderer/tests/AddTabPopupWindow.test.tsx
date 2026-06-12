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
  locale: 'ja',
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
  document.documentElement.lang = 'en'
})

describe('AddTabPopupWindow', () => {
  it('renders themed menu items and resolves enabled choices', async () => {
    const onResolve = vi.fn()
    render(<AddTabPopupWindow payload={payload} onResolve={onResolve} />)

    await screen.findByRole('menu')

    fireEvent.click(await screen.findByRole('menuitem', { name: /Open File/ }))

    await waitFor(() => {
      expect(onResolve).toHaveBeenCalledWith('openFile')
    })
  })

  it('keeps disabled items visible but inert', async () => {
    const onResolve = vi.fn()
    render(<AddTabPopupWindow payload={payload} onResolve={onResolve} />)

    const disabled = await screen.findByRole('menuitem', { name: 'Browser' })
    expect((disabled as HTMLButtonElement).disabled).toBe(true)
    fireEvent.click(disabled)

    expect(onResolve).not.toHaveBeenCalled()
  })

  it('dismisses with null on Escape', async () => {
    const onResolve = vi.fn()
    render(<AddTabPopupWindow payload={payload} onResolve={onResolve} />)
    await screen.findByRole('menu')

    fireEvent.keyDown(window, { key: 'Escape' })

    await waitFor(() => {
      expect(onResolve).toHaveBeenCalledWith(null)
    })
  })

  it('updates menu content from rerendered payloads', async () => {
    const onResolve = vi.fn()
    const { rerender } = render(<AddTabPopupWindow payload={null} onResolve={onResolve} />)

    act(() => {
      rerender(
        <AddTabPopupWindow
          payload={{
        ...payload,
        theme: 'light',
        locale: 'ko',
        position: { left: 24, top: 30, width: 210 },
        items: [
          { action: 'openFile', label: 'Open Something', enabled: true },
          { action: 'newBrowser', label: 'Browser', enabled: true }
        ]
          }}
          onResolve={onResolve}
        />
      )
    })

    expect(await screen.findByRole('menuitem', { name: 'Open Something' })).toBeTruthy()
    expect(screen.getByRole('menu').getAttribute('style')).toContain('left: 24px')
  })

  it('closes when clicking the backdrop', async () => {
    const onResolve = vi.fn()
    render(<AddTabPopupWindow payload={payload} onResolve={onResolve} />)

    fireEvent.mouseDown(screen.getByRole('presentation'))

    expect(onResolve).toHaveBeenCalledWith(null)
  })
})
