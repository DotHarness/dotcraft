import { beforeEach, describe, expect, it } from 'vitest'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { isDetailPanelVisible } from '../stores/detailPanelPresentation'
import { useUIStore } from '../stores/uiStore'

const store = () => useDetailPanelStore.getState()
const panel = () => store().getCurrentPanel()

beforeEach(() => {
  store().reset()
  store().onWorkspaceSwitched('/project-a')
  store().onThreadSwitched('thread-a')
  useUIStore.setState({ responsiveLayout: 'full', activeMainView: 'conversation', selectedChangeKey: null })
})

describe('detail panel focus and closing', () => {
  it('shows Changes with its selected row through the shared UI action', () => {
    store().setActiveDetailTab('plan')
    store().setDetailPanelVisible(false)
    useUIStore.getState().showChangesForKey('turn-1::a')
    expect(isDetailPanelVisible()).toBe(true)
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useUIStore.getState().selectedChangeKey).toBe('turn-1::a')
  })

  it('reveals system and viewer tabs and remembers the last system tab', () => {
    store().setActiveDetailTab('plan')
    store().setActiveViewerTab('viewer')
    expect(panel().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer' })
    expect(panel().lastActiveSystemTab).toBe('plan')
    store().closeViewerTab()
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })

  it('orders system tabs canonically and selects remaining tabs on close', () => {
    store().setActiveDetailTab('plan')
    store().setActiveDetailTab('changes')
    expect(panel().openSystemTabs).toEqual(['changes', 'plan'])
    store().closeSystemTab('changes')
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    store().closeSystemTab('plan', 'viewer')
    expect(panel().activeDetailTab).toEqual({ kind: 'viewer', id: 'viewer' })
    expect(isDetailPanelVisible()).toBe(true)
  })

  it('hides the panel after the final tab closes', () => {
    store().setActiveDetailTab('plan')
    store().closeSystemTab('plan')
    expect(panel().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(panel().detailPanelPreferredVisible).toBe(false)
  })

  it('does not reveal a hidden panel when switching tabs without reveal', () => {
    store().setActiveDetailTab('plan', { reveal: false })
    store().setActiveViewerTab('viewer', { reveal: false })
    expect(isDetailPanelVisible()).toBe(false)
    store().closeViewerTab({ reveal: false })
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(isDetailPanelVisible()).toBe(false)
  })

  it('hides an empty panel when the last viewer closes internally', () => {
    store().setActiveViewerTab('viewer')
    store().closeViewerTab({ reveal: false })
    expect(panel().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(isDetailPanelVisible()).toBe(false)
  })
})

describe('scope state', () => {
  it('opens a new thread without inheriting the previous panel selection', () => {
    store().openBrowser({ threadId: 'thread-a' })
    store().setActiveDetailTab('changes')
    store().onThreadSwitched('thread-b')
    expect(panel().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(isDetailPanelVisible()).toBe(false)
  })

  it('restores each thread system selection and full-view preference', () => {
    store().openBrowser({ threadId: 'thread-a' })
    store().setActiveDetailTab('plan')
    store().setDetailPanelFullView(true)
    store().onThreadSwitched('thread-b')
    store().setActiveDetailTab('board')
    store().onThreadSwitched('thread-a')
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(panel().openSystemTabs).toEqual(['plan'])
    expect(panel().detailPanelFullView).toBe(true)
  })

  it('keeps A hidden while switching through visible B', () => {
    store().setActiveDetailTab('changes')
    store().toggleDetailPanel()
    store().onThreadSwitched('thread-b')
    store().setActiveDetailTab('plan')
    expect(isDetailPanelVisible()).toBe(true)
    store().onThreadSwitched('thread-a')
    expect(isDetailPanelVisible()).toBe(false)
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
  })

  it('preserves a deliberately opened empty launcher on return', () => {
    store().setDetailPanelVisible(true)
    store().onThreadSwitched('thread-b')
    store().onThreadSwitched('thread-a')
    expect(panel().activeDetailTab).toEqual({ kind: 'launcher' })
    expect(isDetailPanelVisible()).toBe(true)
  })

  it('isolates identical thread ids across projects and deletes only the owning scope', () => {
    const first = store().openBrowser({ threadId: 'thread-a' })
    store().setActiveDetailTab('plan')
    store().onWorkspaceSwitched('/project-b')
    const second = store().openBrowser({ threadId: 'thread-a' })
    store().setActiveViewerTab(second)
    store().onThreadDeleted('thread-a', { projectKey: '/project-a' })
    expect(store().getThreadState('thread-a', '/project-a').tabs).toEqual([])
    expect(store().getThreadState('thread-a', '/project-b').tabs.map(tab => tab.id)).toEqual([second])
    expect(store().getScopeForTab(first)).toBeNull()
  })

  it('retains user visibility while responsive layout suppresses presentation', () => {
    store().setActiveDetailTab('plan')
    useUIStore.getState().setResponsiveLayout('no-detail')
    expect(panel().detailPanelPreferredVisible).toBe(true)
    expect(isDetailPanelVisible()).toBe(false)
    useUIStore.getState().setResponsiveLayout('full')
    expect(isDetailPanelVisible()).toBe(true)
  })

  it('keeps automatic reveal reasons scoped to their conversation', () => {
    expect(store().maybeAutoShowForReason('turn:one')).toBe(true)
    expect(store().maybeAutoShowForReason('turn:one')).toBe(false)
    store().onThreadSwitched('thread-b')
    expect(store().maybeAutoShowForReason('turn:one')).toBe(true)
    store().resetAutoShowReasons()
    expect(store().getCurrentPanel().autoShowReasons.size).toBe(0)
  })
})
