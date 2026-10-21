import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { ThreePanel } from '../components/layout/ThreePanel'
import { handleFindShortcut } from '../find/findShortcut'
import { registerFindSurface } from '../find/registry'
import { useFindStore } from '../stores/findStore'
import { useThreadStore } from '../stores/threadStore'
import { DETAIL_DEFAULT_WIDTH_RATIO, useUIStore } from '../stores/uiStore'

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

const ui = () => useUIStore.getState()

beforeEach(() => {
  installDesktopApiMock({ platform: 'win32', window: { toggleMaximize: vi.fn().mockResolvedValue(false) } })
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 1916 })
  Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverMock })
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  useUIStore.setState({
    activeMainView: 'conversation',
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false,
    detailPanelPreferredVisible: true,
    detailPanelPreferredVisibleByThread: {},
    detailPanelVisible: true,
    detailPanelFullView: false,
    detailPanelWidth: 600,
    detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
    responsiveLayout: 'full',
    activeDetailTab: { kind: 'system', id: 'changes' },
    openSystemTabs: ['changes'],
    lastActiveSystemTab: 'changes'
  })
})

describe('detail panel full view state', () => {
  it('reveals a hidden panel with content when entering full view', () => {
    useUIStore.setState({ detailPanelPreferredVisible: false, detailPanelVisible: false })

    ui().toggleDetailPanelFullView()

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().detailPanelFullView).toBe(true)
  })

  it('does not enter full view while the panel only shows the launcher', () => {
    useUIStore.setState({ activeDetailTab: { kind: 'launcher' }, openSystemTabs: [] })

    ui().toggleDetailPanelFullView()

    expect(ui().detailPanelFullView).toBe(false)
  })

  it('does not enter full view while the responsive layout suppresses the panel', () => {
    ui().setResponsiveLayout('no-detail')

    ui().toggleDetailPanelFullView()

    expect(ui().detailPanelFullView).toBe(false)
  })

  it.each([
    ['hiding the panel', () => ui().toggleDetailPanel()],
    ['leaving the conversation', () => ui().setActiveMainView('settings')],
    ['closing the last tab', () => ui().closeSystemTab('changes')],
    ['narrowing the window', () => ui().setResponsiveLayout('no-detail')],
    ['switching threads', () => ui().syncDetailPanelForThread('thread-2', 'viewer-1')]
  ])('exits full view when %s', (_name, action) => {
    ui().setDetailPanelFullView(true)
    expect(ui().detailPanelFullView).toBe(true)

    action()

    expect(ui().detailPanelFullView).toBe(false)
  })
})

describe('ThreePanel full view', () => {
  function renderThreePanel(): void {
    render(
      <ThreePanel
        sidebar={<div>Sidebar</div>}
        conversation={<textarea aria-label="Composer" defaultValue="draft" />}
        detail={<div>Detail</div>}
      />
    )
  }

  it('keeps the conversation mounted but inert and removes the detail resize handle', () => {
    renderThreePanel()
    expect(screen.getAllByRole('separator')).toHaveLength(2)

    act(() => ui().setDetailPanelFullView(true))

    const column = screen.getByTestId('conversation-column')
    expect(column).toHaveAttribute('inert')
    expect(screen.getByText('Detail')).toBeInTheDocument()
    expect(screen.getAllByRole('separator', { hidden: true })).toHaveLength(1)

    act(() => ui().setDetailPanelFullView(false))

    expect(column).not.toHaveAttribute('inert')
    expect(screen.getByLabelText('Composer')).toHaveValue('draft')
  })
})

describe('find shortcut', () => {
  let unregister: (() => void) | null = null

  afterEach(() => {
    unregister?.()
    unregister = null
    useFindStore.getState().closeFind()
  })

  it('leaves Ctrl+Shift+F to the full view shortcut', () => {
    unregister = registerFindSurface({
      id: 'surface',
      domain: 'file',
      priority: 0,
      getSegments: () => [],
      getContainer: () => null
    })

    const shifted = new KeyboardEvent('keydown', { key: 'F', ctrlKey: true, shiftKey: true })
    expect(handleFindShortcut(shifted)).toBe(false)
    expect(useFindStore.getState().open).toBe(false)

    const plain = new KeyboardEvent('keydown', { key: 'f', ctrlKey: true, cancelable: true })
    expect(handleFindShortcut(plain)).toBe(true)
    expect(useFindStore.getState().open).toBe(true)
  })
})
