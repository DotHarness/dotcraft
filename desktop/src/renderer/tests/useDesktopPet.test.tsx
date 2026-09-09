// @vitest-environment jsdom
import { act, fireEvent, render } from '@testing-library/react'
import { useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PetEvent } from '../../shared/desktopPet'
import { useDesktopPet } from '../components/desktopPet/useDesktopPet'

function Harness(): JSX.Element {
  const root = useRef<HTMLDivElement>(null)
  useDesktopPet(root, true, true)
  return <div ref={root} data-testid="root"><span className="composer-mascot-jelly" data-testid="mascot" /></div>
}

describe('desktop pet source handoff', () => {
  const originalApi = window.api
  let emit: (event: PetEvent) => void
  const command = vi.fn(async () => {})

  beforeEach(() => {
    command.mockClear()
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 300 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 300 })
    window.api = { desktopPet: { command, onEvent: callback => { emit = callback; return () => {} } } } as typeof window.api
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    window.api = originalApi
    document.documentElement.removeAttribute('data-desktop-pet-detached')
  })

  it('releases the source pointer and visual state when handoff recovery arrives', () => {
    const view = render(<Harness />)
    const root = view.getByTestId('root')
    const mascot = view.getByTestId('mascot')
    const releasePointerCapture = vi.fn()
    Object.assign(root, {
      setPointerCapture: vi.fn(),
      hasPointerCapture: vi.fn(() => true),
      releasePointerCapture
    })
    mascot.getBoundingClientRect = () => ({ x: 100, y: 100, width: 44, height: 44, top: 100, right: 144, bottom: 144, left: 100, toJSON: () => ({}) })

    fireEvent.pointerDown(mascot, { button: 0, pointerId: 7, clientX: 122, clientY: 122 })
    fireEvent.pointerMove(root, { pointerId: 7, clientX: 285, clientY: 122 })

    expect(command).toHaveBeenCalledWith(expect.objectContaining({ type: 'detach', pointerHeld: true }))
    expect(root).toHaveAttribute('data-pet-owner')
    expect(mascot.style.translate).not.toBe('')

    act(() => emit({ type: 'ownership', detached: false }))

    expect(releasePointerCapture).toHaveBeenCalledWith(7)
    expect(root).not.toHaveAttribute('data-pet-owner')
    expect(root).not.toHaveAttribute('data-pet-drag')
    expect(mascot.style.translate).toBe('')
  })
})
