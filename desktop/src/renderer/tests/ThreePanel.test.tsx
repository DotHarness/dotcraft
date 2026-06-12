import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
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

function getSidebarFrame(): HTMLElement {
  return screen.getByText('Sidebar').parentElement as HTMLElement
}

function getMainSurface(): HTMLElement {
  return screen.getByText('Conversation').parentElement?.parentElement as HTMLElement
}

function getDetailFrame(): HTMLElement {
  return screen.getByText('Detail').parentElement as HTMLElement
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
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'win32',
        window: {
          toggleMaximize
        }
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
    const sidebarFrame = getSidebarFrame()
    const mainSurface = getMainSurface()

    expect(separator).toHaveStyle({
      position: 'absolute',
      width: 'var(--resize-divider-hit-width)',
      left: '236px'
    })
    expect(sidebarFrame.style.zIndex).toBe('')
    expect(separator.style.backgroundColor).toBe('transparent')
    expect(separator.querySelector('.drag-handle__line')).not.toBeInTheDocument()
    expect(separator.childElementCount).toBe(0)
    expect(mainSurface.style.getPropertyValue('--main-surface-left-border')).toBe(
      'var(--glass-border-strong)'
    )

    fireEvent.pointerEnter(separator)
    expect(separator).toHaveAttribute('data-active', 'true')
    expect(mainSurface.style.getPropertyValue('--main-surface-left-border')).toBe(
      'var(--resize-divider-active)'
    )

    fireEvent.pointerDown(separator, { clientX: 240 })
    fireEvent.pointerLeave(separator)
    expect(separator).toHaveAttribute('data-active', 'true')
    expect(sidebarFrame.style.transition).toBe('none')
    fireEvent.pointerMove(document, { clientX: 292 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().sidebarWidth).toBe(292)
    expect(sidebarFrame.style.transition).toBe('width 200ms ease-out, min-width 200ms ease-out')
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

    const { container } = render(
      <div style={{ width: '1000px', height: '600px' }}>
        <ThreePanel
          sidebar={<div>Sidebar</div>}
          conversation={<div>Conversation</div>}
          detail={<div>Detail</div>}
        />
      </div>
    )

    expect(container.querySelector('.drag-handle--sidebar')).not.toBeInTheDocument()
  })

  it('renders a draggable macOS safe area above the sidebar rail', () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        ...window.api,
        platform: 'darwin'
      }
    })

    renderThreePanel()

    const safeArea = screen.getByTestId('mac-sidebar-safe-area') as HTMLDivElement
    expect(safeArea).toHaveStyle({ height: '24px' })
    expect(safeArea.style.flexShrink).toBe('0')
  })

  it('toggles maximize when the macOS safe area is double-clicked', () => {
    const toggleMaximize = vi.fn().mockResolvedValue(false)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        ...window.api,
        platform: 'darwin',
        window: {
          toggleMaximize
        }
      }
    })

    renderThreePanel()

    fireEvent.doubleClick(screen.getByTestId('mac-sidebar-safe-area'))

    expect(toggleMaximize).toHaveBeenCalledTimes(1)
  })

  it('accumulates repeated detail panel drag deltas without snapping back', () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    renderThreePanel()

    const separators = screen.getAllByRole('separator')
    const detailFrame = getDetailFrame()
    expect(separators).toHaveLength(2)
    expect(separators[1]).toHaveStyle({
      position: 'absolute',
      width: 'var(--resize-divider-hit-width)',
      right: '596px'
    })
    expect(separators[1].style.backgroundColor).toBe('transparent')
    expect(separators[1].querySelector('.drag-handle__line')).not.toBeInTheDocument()
    expect(detailFrame.style.getPropertyValue('--detail-divider-border')).toBe(
      'var(--glass-border)'
    )

    fireEvent.pointerEnter(separators[1])
    expect(detailFrame.style.getPropertyValue('--detail-divider-border')).toBe(
      'var(--resize-divider-active)'
    )

    fireEvent.pointerDown(separators[1], { clientX: 1000 })
    fireEvent.pointerLeave(separators[1])
    expect(detailFrame.style.transition).toBe('none')
    expect(detailFrame.style.getPropertyValue('--detail-divider-border')).toBe(
      'var(--resize-divider-active)'
    )
    fireEvent.pointerMove(document, { clientX: 1010 })
    fireEvent.pointerMove(document, { clientX: 1020 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().detailPanelWidth).toBe(580)
    expect(useUIStore.getState().detailPanelWidthRatio).toBeCloseTo(580 / 1676, 6)
    expect(detailFrame).toHaveStyle({ width: '580px' })
    expect(detailFrame.style.transition).toBe('width 200ms ease-out, min-width 200ms ease-out')
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
    const detailFrame = getDetailFrame()

    fireEvent.pointerDown(separators[1], { clientX: 1000 })
    fireEvent.pointerMove(document, { clientX: -100 })
    fireEvent.pointerUp(document)

    expect(useUIStore.getState().detailPanelWidth).toBe(760)
    expect(useUIStore.getState().detailPanelWidthRatio).toBeCloseTo(760 / 1160, 6)
    expect(detailFrame).toHaveStyle({ width: '760px' })
    expect(detailFrame.style.transition).toBe('width 200ms ease-out, min-width 200ms ease-out')
  })

  it('scales the visible detail panel proportionally when the main surface narrows', () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useUIStore.setState({
      detailPanelPreferredVisible: true,
      detailPanelVisible: true
    })

    renderThreePanel()

    const detailFrame = getDetailFrame()
    expect(detailFrame).toHaveStyle({ width: '600px' })

    act(() => {
      resizeObserverCallback?.(
        [{ contentRect: { width: 1156 } } as ResizeObserverEntry],
        {} as ResizeObserver
      )
    })

    expect(useUIStore.getState().detailPanelWidth).toBe(600)
    expect(useUIStore.getState().detailPanelWidthRatio).toBe(DETAIL_DEFAULT_WIDTH_RATIO)
    expect(detailFrame).toHaveStyle({ width: '414px' })
  })
})
