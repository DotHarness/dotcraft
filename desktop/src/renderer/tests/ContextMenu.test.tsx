import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { ContextMenu } from '../components/ui/ContextMenu'

describe('ContextMenu', () => {
  function setViewport(width: number, height: number): void {
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: width
    })
    Object.defineProperty(window, 'innerHeight', {
      configurable: true,
      writable: true,
      value: height
    })
  }

  it('opens a submenu when its parent item is clicked', () => {
    const onClose = vi.fn()
    const onForkLocal = vi.fn()

    render(
      <ContextMenu
        position={{ x: 16, y: 16 }}
        onClose={onClose}
        items={[
          {
            label: 'Fork',
            onClick: vi.fn(),
            submenu: [
              {
                label: 'Fork into local',
                onClick: onForkLocal
              }
            ]
          }
        ]}
      />
    )

    fireEvent.click(screen.getByRole('menuitem', { name: 'Fork' }))

    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('menuitem', { name: 'Fork into local' }))

    expect(onForkLocal).toHaveBeenCalledTimes(1)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('positions a submenu inside the parent menu when opening near the right edge', () => {
    setViewport(1024, 768)

    render(
      <ContextMenu
        position={{ x: 900, y: 16 }}
        onClose={vi.fn()}
        items={[
          {
            label: 'Fork',
            onClick: vi.fn(),
            submenu: [
              {
                label: 'Fork into local',
                onClick: vi.fn()
              }
            ]
          }
        ]}
      />
    )

    fireEvent.click(screen.getByRole('menuitem', { name: 'Fork' }))

    const submenu = screen.getAllByRole('menu')[1]
    expect(submenu).toHaveStyle({
      position: 'absolute',
      left: '-196px'
    })
  })
})
