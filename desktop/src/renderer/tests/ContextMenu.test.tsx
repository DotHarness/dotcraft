import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { ContextMenu } from '../components/ui/ContextMenu'

describe('ContextMenu', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

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

  it('opens a submenu to the left of its parent item near the right edge', () => {
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
      left: '-199px',
      top: '8px'
    })
  })

  it('opens a submenu to the right of its parent item when space is available', () => {
    setViewport(1024, 768)

    render(
      <ContextMenu
        position={{ x: 280, y: 16 }}
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
      left: '199px',
      top: '8px'
    })
  })

  it.each([
    {
      side: 'right',
      position: { x: 280, y: 16 },
      anchor: { clientX: 350, clientY: 40 },
      crossing: { clientX: 420, clientY: 60 },
      submenuRect: domRect(479, 24, 200, 120)
    },
    {
      side: 'left',
      position: { x: 900, y: 16 },
      anchor: { clientX: 850, clientY: 40 },
      crossing: { clientX: 830, clientY: 60 },
      submenuRect: domRect(617, 24, 200, 120)
    }
  ])('keeps a $side-opening submenu while the pointer crosses its prediction cone', ({
    position,
    anchor,
    crossing,
    submenuRect
  }) => {
    vi.useFakeTimers()
    setViewport(1024, 768)
    renderPredictionMenu(position)

    const forkItem = screen.getByRole('menuitem', { name: 'Fork' })
    const deleteItem = screen.getByRole('menuitem', { name: 'Delete' })
    fireEvent.mouseEnter(forkItem, anchor)

    const submenu = screen.getAllByRole('menu')[1]
    vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(submenuRect)
    fireEvent.mouseEnter(deleteItem, crossing)

    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()
    act(() => vi.advanceTimersByTime(279))
    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()

    act(() => vi.advanceTimersByTime(1))
    expect(screen.queryByRole('menuitem', { name: 'Fork into local' })).not.toBeInTheDocument()
  })

  it('closes the current submenu immediately when the pointer moves outside the prediction cone', () => {
    vi.useFakeTimers()
    setViewport(1024, 768)
    renderPredictionMenu({ x: 280, y: 16 })

    const forkItem = screen.getByRole('menuitem', { name: 'Fork' })
    fireEvent.mouseEnter(forkItem, { clientX: 350, clientY: 40 })

    const submenu = screen.getAllByRole('menu')[1]
    vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(domRect(479, 24, 200, 120))
    fireEvent.mouseEnter(screen.getByRole('menuitem', { name: 'Delete' }), {
      clientX: 330,
      clientY: 60
    })

    expect(screen.queryByRole('menuitem', { name: 'Fork into local' })).not.toBeInTheDocument()
  })

  it('cancels a guarded action when the pointer enters the submenu', () => {
    vi.useFakeTimers()
    setViewport(1024, 768)
    renderPredictionMenu({ x: 280, y: 16 })

    const forkItem = screen.getByRole('menuitem', { name: 'Fork' })
    fireEvent.mouseEnter(forkItem, { clientX: 350, clientY: 40 })

    const submenu = screen.getAllByRole('menu')[1]
    vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(domRect(479, 24, 200, 120))
    fireEvent.mouseEnter(screen.getByRole('menuitem', { name: 'Delete' }), {
      clientX: 420,
      clientY: 60
    })
    fireEvent.mouseEnter(submenu)
    act(() => vi.advanceTimersByTime(280))

    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()
  })

  it('cancels a guarded action and switches submenus immediately when clicked', () => {
    vi.useFakeTimers()
    setViewport(1024, 768)
    renderPredictionMenu({ x: 280, y: 16 })

    const forkItem = screen.getByRole('menuitem', { name: 'Fork' })
    fireEvent.mouseEnter(forkItem, { clientX: 350, clientY: 40 })

    const submenu = screen.getAllByRole('menu')[1]
    vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(domRect(479, 24, 200, 120))
    fireEvent.mouseEnter(screen.getByRole('menuitem', { name: 'Delete' }), {
      clientX: 420,
      clientY: 60
    })
    fireEvent.click(screen.getByRole('menuitem', { name: 'Move' }))

    expect(screen.queryByRole('menuitem', { name: 'Fork into local' })).not.toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Move to project' })).toBeInTheDocument()

    act(() => vi.advanceTimersByTime(280))
    expect(screen.getByRole('menuitem', { name: 'Move to project' })).toBeInTheDocument()
  })
})

function renderPredictionMenu(position: { x: number; y: number }): void {
  render(
    <ContextMenu
      position={position}
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
        },
        {
          label: 'Move',
          onClick: vi.fn(),
          submenu: [
            {
              label: 'Move to project',
              onClick: vi.fn()
            }
          ]
        },
        {
          label: 'Delete',
          onClick: vi.fn()
        }
      ]}
    />
  )
}

function domRect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    x: left,
    y: top,
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    toJSON: () => ({})
  }
}
