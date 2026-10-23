import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import {
  DETAIL_DEFAULT_WIDTH,
  DETAIL_DEFAULT_WIDTH_RATIO,
  useUIStore
} from '../stores/uiStore'

const ui = () => useUIStore.getState()

describe('uiStore detail panel width', () => {
  it('updates the detail panel width fallback and ratio together', () => {
    ui().setDetailPanelWidth(580, 1676)

    expect(ui().detailPanelWidth).toBe(580)
    expect(ui().detailPanelWidthRatio).toBeCloseTo(580 / 1676, 6)
  })
})

const settingsSet = vi.fn()

describe('uiStore sidebar section preferences', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsSet.mockResolvedValue(undefined)
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: { api: { settings: { set: settingsSet } } }
    })
    useUIStore.setState({
      projectsSectionCollapsed: false,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: false
    })
  })

  afterEach(() => {
    Reflect.deleteProperty(globalThis, 'window')
  })

  it.each([
    ['projectsSectionCollapsed', 'setProjectsSectionCollapsed'],
    ['pinnedSectionCollapsed', 'setPinnedSectionCollapsed'],
    ['chatsSectionCollapsed', 'setChatsSectionCollapsed']
  ] as const)('updates and persists %s', (key, setter) => {
    for (const value of [true, false]) {
      ui()[setter](value)
      expect(ui()[key]).toBe(value)
      expect(settingsSet).toHaveBeenLastCalledWith({ [key]: value })
    }
  })
})

describe('uiStore welcome state', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'settings',
      welcomeDraft: null,
      welcomeDraftsByWorkspace: {},
      welcomeDraftWorkspacePath: null
    })
    ui().setPendingWelcomeTurn(null)
  })

  it('goToNewChat clears active thread and routes to conversation view', () => {
    useThreadStore.getState().setActiveThreadId('thread-123')

    ui().goToNewChat()

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(ui().activeMainView).toBe('conversation')
  })

  it('keeps welcome drafts scoped by workspace', () => {
    ui().setWelcomeDraft({
      text: 'Draft A',
      images: [],
      files: [],
      mode: 'agent',
      providerId: 'provider-a',
      model: 'Default',
    }, '/workspace/a')
    ui().setWelcomeDraft({
      text: 'Draft B',
      images: [],
      files: [],
      mode: 'plan',
      model: 'gpt-test'
    }, '/workspace/b')

    ui().setWelcomeDraftWorkspace('/workspace/a')
    expect(ui().welcomeDraft?.text).toBe('Draft A')
    expect(ui().welcomeDraft?.providerId).toBe('provider-a')

    ui().setWelcomeDraftWorkspace('/workspace/b')
    expect(ui().welcomeDraft?.text).toBe('Draft B')

    ui().clearWelcomeDraft('/workspace/b')
    expect(ui().getWelcomeDraftForWorkspace('/workspace/a')?.text).toBe('Draft A')
    expect(ui().getWelcomeDraftForWorkspace('/workspace/b')).toBeNull()
  })

  it('consumes pending welcome turns only for their thread as first-turn transport data', () => {
    const inputParts = [{ type: 'text' as const, text: 'hello' }]
    ui().setPendingWelcomeTurn({ threadId: 'thread-message', text: 'hello', inputParts })
    expect(ui().consumePendingWelcomeTurnIfMatch('other-thread')).toBeNull()
    expect(ui().consumePendingWelcomeTurnIfMatch('thread-message')).toEqual({ text: 'hello', inputParts })
    expect(ui().consumePendingWelcomeTurnIfMatch('thread-message')).toBeNull()

    ui().setPendingWelcomeTurn({ threadId: 'thread-goal', text: 'review the branch', inputParts, sentAsGoal: true })
    expect(ui().consumePendingWelcomeTurnIfMatch('thread-goal')?.sentAsGoal).toBe(true)
  })
})

describe('uiStore responsive panel preferences', () => {
  beforeEach(() => {
    useThreadStore.setState({ activeThreadId: 'thread-a' })
    useUIStore.setState({
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      detailPanelPreferredVisible: true,
      detailPanelPreferredVisibleByThread: { 'thread-a': true },
      detailPanelVisible: true,
      detailPanelWidth: DETAIL_DEFAULT_WIDTH,
      detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
      responsiveLayout: 'full',
      activeDetailTab: { kind: 'system', id: 'changes' },
      lastActiveSystemTab: 'changes',
      selectedChangeKey: null,
      autoShowReasons: new Set<string>()
    })
  })

  it('preserves a manually hidden detail panel across breakpoint changes', () => {
    ui().setDetailPanelVisible(false)

    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)

    ui().setResponsiveLayout('collapsed')
    ui().setResponsiveLayout('full')

    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)
  })

  it('restores the preferred sidebar expansion after leaving the collapsed breakpoint', () => {
    ui().setSidebarCollapsed(false)
    ui().setResponsiveLayout('collapsed')

    expect(ui().sidebarCollapsed).toBe(true)

    ui().setResponsiveLayout('full')

    expect(ui().sidebarPreferredCollapsed).toBe(false)
    expect(ui().sidebarCollapsed).toBe(false)
  })

  it('auto-opening the detail panel updates the stored preference', () => {
    ui().setDetailPanelVisible(false)
    ui().setResponsiveLayout('no-detail')

    ui().showChangesForKey('src/foo.ts')

    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelVisible).toBe(false)

    ui().setResponsiveLayout('full')

    expect(ui().detailPanelVisible).toBe(true)
    expect(ui().selectedChangeKey).toBe('src/foo.ts')
  })

  it('records one-shot auto-show reasons until reset', () => {
    ui().setDetailPanelVisible(false)
    expect(ui().maybeAutoShowForReason('link:thread-1:item-2')).toBe(true)
    expect(ui().maybeAutoShowForReason('link:thread-1:item-2')).toBe(false)
    expect(ui().detailPanelPreferredVisible).toBe(true)

    ui().resetAutoShowReasons()
    expect(ui().autoShowReasons.size).toBe(0)
    expect(ui().maybeAutoShowForReason('link:thread-1:item-2')).toBe(true)
  })

  it('switches detail tabs without revealing the panel only when asked', () => {
    ui().setDetailPanelVisible(false)

    ui().setActiveDetailTab('plan', { reveal: false })
    expect(ui().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(ui().lastActiveSystemTab).toBe('plan')

    ui().setActiveViewerTab('vtab-hidden', { reveal: false })
    expect(ui().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-hidden' })
    expect(ui().detailPanelPreferredVisible).toBe(false)
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(false)
    expect(ui().detailPanelVisible).toBe(false)

    ui().setActiveDetailTab('plan')
    expect(ui().detailPanelPreferredVisible).toBe(true)
    expect(ui().detailPanelPreferredVisibleByThread['thread-a']).toBe(true)
    expect(ui().detailPanelVisible).toBe(true)
  })
})
