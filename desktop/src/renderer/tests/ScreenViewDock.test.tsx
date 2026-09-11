import { useRef } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import type { ScreenViewStatus } from '../../shared/screenView'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ScreenViewDock } from '../components/conversation/screenView/ScreenViewDock'
import { useScreenViewStore } from '../stores/screenViewStore'

const settingsSet = vi.fn(async () => {})
const originalApi = window.api

function Harness({ status }: { status: ScreenViewStatus }): JSX.Element {
  const anchorRef = useRef<HTMLButtonElement | null>(null)
  return (
    <LocaleProvider loadSettings={false}>
      <button ref={anchorRef} type="button">
        launcher
      </button>
      <ScreenViewDock
        hostName="Studio PC"
        anchorRef={anchorRef}
        stream={{ setCanvas: () => undefined, state: status, aspect: 16 / 9 }}
      />
    </LocaleProvider>
  )
}

function renderDock(status: ScreenViewStatus = { kind: 'live' }): HTMLElement {
  render(<Harness status={status} />)
  return screen.getByLabelText('Move the screen view')
}

beforeEach(() => {
  settingsSet.mockClear()
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
  Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
  Element.prototype.setPointerCapture = vi.fn()
  Element.prototype.releasePointerCapture = vi.fn()
  window.api = { settings: { set: settingsSet }, screenView: { tune: vi.fn() } } as unknown as typeof window.api
  useScreenViewStore.setState({
    viewId: null,
    dockWidth: 360,
    dockPosition: { x: 400, y: 200 },
    requestedWidth: null
  })
})

afterEach(() => {
  window.api = originalApi
})

describe('ScreenViewDock', () => {
  it('drags from anywhere on the picture and remembers where it lands', () => {
    const dock = renderDock()

    fireEvent.pointerDown(dock, { button: 0, pointerId: 1, clientX: 500, clientY: 300 })
    fireEvent.pointerMove(dock, { pointerId: 1, clientX: 560, clientY: 340 })
    expect(dock.style.left).toBe('460px')
    expect(dock.style.top).toBe('240px')

    fireEvent.pointerUp(dock, { pointerId: 1, clientX: 560, clientY: 340 })
    expect(settingsSet).toHaveBeenCalledWith({
      screenViewDockWidth: 360,
      screenViewDockPosition: { x: 460, y: 240 }
    })
  })

  it('treats a press that barely moves as a click, not a drag', () => {
    const dock = renderDock()

    fireEvent.pointerDown(dock, { button: 0, pointerId: 1, clientX: 500, clientY: 300 })
    fireEvent.pointerMove(dock, { pointerId: 1, clientX: 503, clientY: 302 })
    fireEvent.pointerUp(dock, { pointerId: 1, clientX: 503, clientY: 302 })

    expect(dock.style.left).toBe('400px')
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('keeps the right edge pinned while the grip resizes', () => {
    const dock = renderDock()
    const grip = screen.getByRole('separator', { name: 'Resize the screen view' })

    fireEvent.pointerDown(grip, { button: 0, pointerId: 2, clientX: 400, clientY: 400 })
    fireEvent.pointerMove(grip, { pointerId: 2, clientX: 340, clientY: 400 })
    fireEvent.pointerUp(grip, { pointerId: 2, clientX: 340, clientY: 400 })

    expect(dock.style.width).toBe('420px')
    expect(dock.style.left).toBe('340px')
    expect(settingsSet).toHaveBeenCalledWith({
      screenViewDockWidth: 420,
      screenViewDockPosition: { x: 340, y: 200 }
    })
  })

  it('carries the machine, its state and the host detail in the tooltip and the picture name, never in the DOM', () => {
    const dock = renderDock({
      kind: 'unavailable',
      reason: 'captureFailed',
      detail: 'BitBlt failed (Win32 6)'
    })

    const expected = 'Studio PC · Capture failed · BitBlt failed (Win32 6)'
    expect(dock.title).toBe(expected)
    expect(screen.getByRole('img', { name: expected })).toBeInTheDocument()
    expect(screen.getByText('Studio PC')).toBeInTheDocument()
    expect(screen.queryByText('Capture failed')).toBeNull()
  })
})
