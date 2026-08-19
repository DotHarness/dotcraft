import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { ThreePanel, resolveDetailPanelWidth } from '../components/layout/ThreePanel'
import { useThreadStore } from '../stores/threadStore'
import {
  DETAIL_DEFAULT_WIDTH_RATIO,
  DETAIL_MIN_WIDTH,
  SIDEBAR_MIN_WIDTH,
  useUIStore
} from '../stores/uiStore'

let resizeObserverCallback: ResizeObserverCallback | undefined

class ResizeObserverMock {
  constructor(callback: ResizeObserverCallback) {
    resizeObserverCallback = callback
  }

  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

function renderThreePanel(): ReturnType<typeof render> {
  return render(
    <div style={{ width: '1000px', height: '600px' }}>
      <ThreePanel
        sidebar={<div>Sidebar</div>}
        conversation={<div>Conversation</div>}
        detail={<div>Detail</div>}
      />
    </div>
  )
}

describe('resolveDetailPanelWidth', () => {
  it('keeps the 600px preferred width at a common full-screen width', () => {
    expect(resolveDetailPanelWidth(600, DETAIL_DEFAULT_WIDTH_RATIO, 1676, true)).toBe(600)
  })

  it('scales the default width proportionally when the full layout is windowed', () => {
    expect(resolveDetailPanelWidth(600, DETAIL_DEFAULT_WIDTH_RATIO, 1156, true)).toBe(414)
  })

  it('caps the preferred width to preserve the conversation minimum width', () => {
    expect(resolveDetailPanelWidth(900, 900 / 1156, 1156, true)).toBe(756)
  })

  it('preserves a user preference below the available width cap', () => {
    expect(resolveDetailPanelWidth(360, 360 / 1156, 1156, true)).toBe(360)
  })

  it('never shrinks below the detail panel minimum width', () => {
    expect(resolveDetailPanelWidth(600, DETAIL_DEFAULT_WIDTH_RATIO, 500, true)).toBe(DETAIL_MIN_WIDTH)
  })

  it('uses the fallback width before the main surface is measured', () => {
    expect(resolveDetailPanelWidth(600, DETAIL_DEFAULT_WIDTH_RATIO, null, true)).toBe(600)
  })

  it('returns zero while the detail panel is hidden', () => {
    expect(resolveDetailPanelWidth(600, DETAIL_DEFAULT_WIDTH_RATIO, 1156, false)).toBe(0)
  })
})

describe('ThreePanel sidebar resize', () => {
  beforeEach(() => {
    const toggleMaximize = vi.fn().mockResolvedValue(false)
    installDesktopApiMock({
      platform: 'win32',
      window: {
        toggleMaximize
      }
    })
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: 1916
    })
    Object.defineProperty(window, 'ResizeObserver', {
      configurable: true,
      writable: true,
      value: ResizeObserverMock
    })
    resizeObserverCallback = undefined
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      detailPanelWidth: 600,
      detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
      responsiveLayout: 'full'
    })
  })

  it('resizes the sidebar by dragging the divider between sidebar and conversation', () => {
    renderThreePanel()

    const separator = screen.getByRole('separator')

    fireEvent.pointerDown(separator, { clientX: 240 })
    fireEvent.pointerMove(document, { clientX: 292 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().sidebarWidth).toBe(292)
  })

  it('reveals the sidebar divider glow only while the divider is hovered or dragged', () => {
    renderThreePanel()

    const glow = screen.getByTestId('sidebar-divider-glow')
    const separator = screen.getByRole('separator')

    // Rest state: the left edge stays the plain frame hairline, glow hidden.
    expect(glow.style.opacity).toBe('0')

    // Hover the divider: the center-bright gradient fades in.
    fireEvent.pointerEnter(separator)
    expect(glow.style.opacity).toBe('1')

    // Leaving the divider restores the rest state.
    fireEvent.pointerLeave(separator)
    expect(glow.style.opacity).toBe('0')

    // Dragging keeps the glow visible even without hover.
    fireEvent.pointerDown(separator, { clientX: 240 })
    expect(glow.style.opacity).toBe('1')
    fireEvent.pointerUp(document)
    expect(glow.style.opacity).toBe('0')
  })

  it('keeps the sidebar above its minimum width while dragging', () => {
    renderThreePanel()

    const separator = screen.getByRole('separator')
    fireEvent.pointerDown(separator, { clientX: 240 })
    fireEvent.pointerMove(document, { clientX: -400 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().sidebarWidth).toBe(SIDEBAR_MIN_WIDTH)
  })

  it('does not show the sidebar divider while the sidebar is collapsed', () => {
    useUIStore.setState({
      sidebarPreferredCollapsed: true,
      sidebarCollapsed: true
    })

    render(
      <div style={{ width: '1000px', height: '600px' }}>
        <ThreePanel
          sidebar={<div>Sidebar</div>}
          conversation={<div>Conversation</div>}
          detail={<div>Detail</div>}
        />
      </div>
    )

    expect(screen.queryByRole('separator')).not.toBeInTheDocument()
  })

  it('toggles maximize when the macOS safe area is double-clicked', () => {
    const toggleMaximize = vi.fn().mockResolvedValue(false)
    installDesktopApiMock({
      ...window.api,
      platform: 'darwin',
      window: {
        toggleMaximize
      }
    })

    renderThreePanel()

    fireEvent.doubleClick(screen.getByTestId('mac-sidebar-safe-area'))

    expect(toggleMaximize).toHaveBeenCalledTimes(1)
  })

  it('keeps the detail boundary mounted on the animated panel edge', () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderThreePanel()

    const shell = screen.getByTestId('detail-panel-shell')
    const divider = screen.getByTestId('detail-divider-line')

    expect(shell).toContainElement(divider)
    expect(shell.style.width).toBe('0px')
    expect(divider.style.opacity).toBe('0')
    expect(screen.queryByText('Detail')).not.toBeInTheDocument()
    expect(screen.getAllByRole('separator')).toHaveLength(1)

    act(() => {
      useUIStore.getState().setDetailPanelVisible(true)
    })

    expect(screen.getByTestId('detail-divider-line')).toBe(divider)
    expect(shell.style.width).not.toBe('0px')
    expect(divider.style.left).toBe('0px')
    expect(divider.style.opacity).toBe('1')
    expect(screen.getByText('Detail')).toBeInTheDocument()
    expect(shell).toContainElement(screen.getAllByRole('separator')[1])

    act(() => {
      useUIStore.getState().setDetailPanelVisible(false)
    })

    expect(screen.getByTestId('detail-divider-line')).toBe(divider)
    expect(shell.style.width).toBe('0px')
    expect(divider.style.opacity).toBe('0')
    expect(divider.style.transition).toContain('200ms')
    expect(screen.getAllByRole('separator')).toHaveLength(1)

    act(() => {
      useUIStore.getState().setDetailPanelVisible(true)
    })

    expect(screen.getByTestId('detail-divider-line')).toBe(divider)
    expect(divider.style.opacity).toBe('1')
    expect(screen.getAllByRole('separator')).toHaveLength(2)
  })

  it('accumulates repeated detail panel drag deltas without snapping back', () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    renderThreePanel()

    const separators = screen.getAllByRole('separator')
    expect(separators).toHaveLength(2)

    fireEvent.pointerDown(separators[1], { clientX: 1000 })
    fireEvent.pointerMove(document, { clientX: 1010 })
    fireEvent.pointerMove(document, { clientX: 1020 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().detailPanelWidth).toBe(580)
    expect(useUIStore.getState().detailPanelWidthRatio).toBeCloseTo(580 / 1676, 6)
  })

  it('expands the detail panel up to the dynamic maximum width while dragging left', () => {
    Object.defineProperty(window, 'innerWidth', {
      configurable: true,
      writable: true,
      value: 1400
    })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    renderThreePanel()

    const separators = screen.getAllByRole('separator')

    fireEvent.pointerDown(separators[1], { clientX: 1000 })
    fireEvent.pointerMove(document, { clientX: -100 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().detailPanelWidth).toBe(760)
    expect(useUIStore.getState().detailPanelWidthRatio).toBeCloseTo(760 / 1160, 6)
  })

  it('scales the visible detail panel proportionally when the main surface narrows', () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    renderThreePanel()

    act(() => {
      resizeObserverCallback?.(
        [{ contentRect: { width: 1156 } } as ResizeObserverEntry],
        {} as ResizeObserver
      )
    })

    expect(useUIStore.getState().detailPanelWidth).toBe(600)
    expect(useUIStore.getState().detailPanelWidthRatio).toBe(DETAIL_DEFAULT_WIDTH_RATIO)
  })
})
