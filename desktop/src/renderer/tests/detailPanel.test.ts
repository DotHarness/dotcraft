import { beforeEach, describe, expect, it } from 'vitest'
import { useUIStore } from '../stores/uiStore'
import { useThreadStore } from '../stores/threadStore'

const ui = () => useUIStore.getState()

beforeEach(() => {
  useUIStore.setState({
    detailPanelPreferredVisible: true,
    detailPanelPreferredVisibleByThread: {},
    selectedChangeKey: null,
    activeDetailTab: { kind: 'system', id: 'changes' },
    openSystemTabs: ['changes'],
    lastActiveSystemTab: 'changes',
    detailPanelVisible: true,
    responsiveLayout: 'full'
  })
  useThreadStore.setState({ activeThreadId: null })
})

describe('detail panel tab focus', () => {
  it('shows the panel on the Changes tab with the selected row', () => {
    useUIStore.setState({
      detailPanelVisible: false,
      activeDetailTab: { kind: 'system', id: 'plan' }
    })

    ui().showChangesForKey('turn-1::a')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(ui().selectedChangeKey).toBe('turn-1::a')
  })

  it('reveals the panel for system and viewer tabs and remembers the last system tab', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({ detailPanelVisible: false })

    ui().setActiveDetailTab('plan')
    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })

    ui().setActiveViewerTab('vtab-123')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-123' })
    expect(ui().lastActiveSystemTab).toBe('plan')

    ui().closeViewerTab()
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })
})

describe('optional system tabs', () => {
  beforeEach(() => {
    useUIStore.setState({ openSystemTabs: [], activeDetailTab: { kind: 'launcher' } })
  })

  it('opens system tabs in canonical order and falls back through remaining tabs on close', () => {
    ui().setActiveDetailTab('plan')
    ui().setActiveDetailTab('changes')
    expect(ui().openSystemTabs).toEqual(['changes', 'plan'])

    ui().setActiveDetailTab('plan')
    ui().closeSystemTab('changes')
    expect(ui().openSystemTabs).toEqual(['plan'])
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })

    ui().setActiveDetailTab('changes')
    ui().closeSystemTab('changes')
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })

    ui().closeSystemTab('plan', 'vtab-9')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-9' })
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('auto-hides the panel and saves the choice when the last tab closes', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({ detailPanelPreferredVisibleByThread: { 'thread-a': true } })

    ui().setActiveDetailTab('plan')
    ui().setActiveViewerTab('vtab-2')
    ui().closeViewerTab()
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(ui().detailPanelVisible).toBe(true)

    ui().closeSystemTab('plan')
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('hides the panel when internal synchronization empties it without revealing', () => {
    useUIStore.setState({ activeDetailTab: { kind: 'viewer', id: 'vtab-from-prev-thread' } })

    ui().closeViewerTab({ reveal: false })

    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('resetDetailTabs clears open tabs and per-thread visibility', () => {
    ui().setActiveDetailTab('changes')
    useUIStore.setState({ detailPanelPreferredVisibleByThread: { 'thread-a': true } })

    ui().resetDetailTabs()

    expect(ui().openSystemTabs).toEqual([])
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisibleByThread).toEqual({})
    expect(ui().detailPanelVisible).toBe(false)
  })
})

describe('per-thread detail panel visibility', () => {
  beforeEach(() => {
    useUIStore.setState({ openSystemTabs: [], activeDetailTab: { kind: 'launcher' } })
  })

  it.each([
    { kind: 'launcher' as const },
    { kind: 'viewer' as const, id: 'viewer-from-previous-thread' },
    { kind: 'system' as const, id: 'changes' as const }
  ])('hides an empty incoming thread from a previous $kind state', (activeDetailTab) => {
    useUIStore.setState({ activeDetailTab })

    ui().syncDetailPanelForThread('thread-empty', null)

    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('shows a viewer tab only for a thread saved as visible', () => {
    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelVisible).toBe(false)

    useUIStore.setState({ detailPanelPreferredVisibleByThread: { 'thread-b': true } })
    ui().syncDetailPanelForThread('thread-b', 'viewer-thread-b')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-b' })
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(true)

    ui().syncDetailPanelForThread('thread-b', null)
    expect(ui().detailPanelPreferredVisibleByThread['thread-b']).toBe(true)
    expect(ui().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('keeps A hidden after switching through visible B', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({ detailPanelPreferredVisibleByThread: { 'thread-a': true, 'thread-b': true } })

    ui().toggleDetailPanel()
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)

    useThreadStore.setState({ activeThreadId: 'thread-b' })
    ui().syncDetailPanelForThread('thread-b', 'viewer-thread-b')
    expect(ui().detailPanelVisible).toBe(true)

    useThreadStore.setState({ activeThreadId: 'thread-a' })
    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer-thread-a' })
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('restores the last active system tab only for a thread saved as visible', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      openSystemTabs: ['changes', 'plan'],
      lastActiveSystemTab: 'changes',
      activeDetailTab: { kind: 'viewer', id: 'viewer-from-previous-thread' },
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    ui().syncDetailPanelForThread('thread-a', null)

    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(ui().detailPanelVisible).toBe(true)
  })

  it('records automatic and manual visibility changes for the active thread', () => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      autoShowReasons: new Set()
    })

    expect(ui().maybeAutoShowForReason('turn:one')).toBe(true)
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)

    ui().toggleDetailPanel()
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)

    ui().setActiveDetailTab('plan')
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)
  })

  it('preserves saved visibility while responsive layout suppresses the panel', () => {
    useUIStore.setState({
      responsiveLayout: 'no-detail',
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    ui().syncDetailPanelForThread('thread-a', 'viewer-thread-a')

    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(false)

    ui().setResponsiveLayout('full')
    expect(ui().detailPanelVisible).toBe(true)
  })
})
