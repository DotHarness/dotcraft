import { getRendererTestState, setRendererTestState } from './panelTestState'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { handleFindShortcut } from '../find/findShortcut'
import { registerFindSurface } from '../find/registry'
import { useFindStore } from '../stores/findStore'
import { useThreadStore } from '../stores/threadStore'
import { DETAIL_DEFAULT_WIDTH_RATIO, useUIStore } from '../stores/uiStore'

const ui = () => getRendererTestState()

beforeEach(() => {
  installDesktopApiMock({ platform: 'win32', window: { toggleMaximize: vi.fn().mockResolvedValue(false) } })
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 1916 })
  useDetailPanelStore.getState().reset()
  useThreadStore.getState().reset()
  useThreadStore.setState({ activeThreadId: 'thread-1' })
  setRendererTestState({
    activeMainView: 'conversation',
    sidebarPreferredCollapsed: false,
    sidebarCollapsed: false,
    detailPanelPreferredVisible: true,
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
    setRendererTestState({ detailPanelPreferredVisible: false, detailPanelVisible: false })

    ui().toggleDetailPanelFullView()

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().detailPanelFullView).toBe(true)
  })

  it('does not enter full view while the panel only shows the launcher', () => {
    setRendererTestState({ activeDetailTab: { kind: 'launcher' }, openSystemTabs: [] })

    ui().toggleDetailPanelFullView()

    expect(ui().detailPanelFullView).toBe(false)
  })

  it('preserves full-view preference while responsive layout suppresses the panel', () => {
    ui().setDetailPanelFullView(true)
    ui().setResponsiveLayout('no-detail')
    expect(ui().detailPanelFullView).toBe(true)
    expect(ui().detailPanelVisible).toBe(false)
    ui().setResponsiveLayout('full')
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('keeps full-view state when hiding the panel or leaving the conversation', () => {
    ui().setDetailPanelFullView(true)
    ui().toggleDetailPanel()
    ui().setActiveMainView('settings')
    expect(ui().detailPanelFullView).toBe(true)
  })

  it('clears full view when its last tab closes', () => {
    ui().setDetailPanelFullView(true)
    ui().closeSystemTab('changes')
    expect(ui().detailPanelFullView).toBe(false)
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
