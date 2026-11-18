import { createRef } from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { ApplicationMenuSnapshot } from '../../shared/applicationMenu'
import { ApplicationMenuBar } from '../components/layout/ApplicationMenuBar'
import { LocaleProvider } from '../contexts/LocaleContext'
import { installDesktopApiMock } from './desktopApiMock'

const snapshot: ApplicationMenuSnapshot = {
  file: [{ type: 'item', commandId: 1, label: 'New Window', enabled: true, accelerator: 'Ctrl+Shift+N' }],
  edit: [
    { type: 'item', commandId: 2, label: 'Undo', enabled: true, accelerator: 'Ctrl+Z' },
    { type: 'separator' },
    { type: 'item', commandId: 3, label: 'Copy', enabled: true, accelerator: 'Ctrl+C' }
  ],
  view: [],
  window: [],
  help: []
}

function renderMenuBar() {
  const invokeItem = vi.fn().mockResolvedValue(undefined)
  installDesktopApiMock({
    settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
    menu: { getApplicationMenu: vi.fn().mockResolvedValue(snapshot), invokeItem }
  })
  render(
    <LocaleProvider>
      <textarea aria-label="Composer" />
      <ApplicationMenuBar menusRef={createRef()} collapsed={false} />
    </LocaleProvider>
  )
  return { invokeItem }
}

describe('ApplicationMenuBar', () => {
  it('switches open menus on hover only after one is opened', async () => {
    renderMenuBar()
    const file = await screen.findByRole('button', { name: 'File' })
    const edit = screen.getByRole('button', { name: 'Edit' })

    fireEvent.mouseEnter(edit)
    expect(screen.queryByRole('menu')).toBeNull()

    fireEvent.mouseDown(file)
    expect(await screen.findByRole('menuitem', { name: 'New Window' })).toBeTruthy()

    fireEvent.mouseEnter(edit)
    expect(await screen.findByRole('menuitem', { name: 'Undo' })).toBeTruthy()
    expect(screen.queryByRole('menuitem', { name: 'New Window' })).toBeNull()
    expect(edit.getAttribute('aria-expanded')).toBe('true')

    fireEvent.mouseDown(edit)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('returns focus to the previous element before invoking a native command', async () => {
    const { invokeItem } = renderMenuBar()
    const composer = screen.getByRole('textbox', { name: 'Composer' })
    composer.focus()

    fireEvent.mouseDown(await screen.findByRole('button', { name: 'Edit' }))
    const copy = await screen.findByRole('menuitem', { name: 'Copy' })
    invokeItem.mockImplementation(() => {
      expect(document.activeElement).toBe(composer)
      return Promise.resolve()
    })
    act(() => copy.focus())
    fireEvent.click(copy)

    expect(invokeItem).toHaveBeenCalledWith(3)
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('moves between menus with the arrow keys', async () => {
    renderMenuBar()
    fireEvent.keyDown(await screen.findByRole('button', { name: 'File' }), { key: 'ArrowDown' })
    expect(await screen.findByRole('menuitem', { name: 'New Window' })).toBeTruthy()

    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'ArrowRight' })
    expect(await screen.findByRole('menuitem', { name: 'Undo' })).toBeTruthy()
  })
})
