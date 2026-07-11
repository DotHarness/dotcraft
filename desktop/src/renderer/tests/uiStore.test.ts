import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import {
  DETAIL_DEFAULT_MAIN_SURFACE_WIDTH,
  DETAIL_DEFAULT_WIDTH,
  DETAIL_DEFAULT_WIDTH_RATIO,
  useUIStore
} from '../stores/uiStore'

describe('uiStore defaults', () => {
  it('starts with the detail panel hidden', () => {
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('starts with a wider default detail panel width', () => {
    expect(useUIStore.getState().detailPanelWidth).toBe(DETAIL_DEFAULT_WIDTH)
    expect(useUIStore.getState().detailPanelWidth).toBe(600)
  })

  it('starts with a proportional default detail panel width preference', () => {
    expect(useUIStore.getState().detailPanelWidthRatio).toBe(DETAIL_DEFAULT_WIDTH_RATIO)
    expect(useUIStore.getState().detailPanelWidthRatio).toBe(
      DETAIL_DEFAULT_WIDTH / DETAIL_DEFAULT_MAIN_SURFACE_WIDTH
    )
  })

  it('updates the detail panel width fallback and ratio together', () => {
    useUIStore.getState().setDetailPanelWidth(580, 1676)

    expect(useUIStore.getState().detailPanelWidth).toBe(580)
    expect(useUIStore.getState().detailPanelWidthRatio).toBeCloseTo(580 / 1676, 6)
  })

  it("tracks renderer requests to open What's New", () => {
    const before = useUIStore.getState().whatsNewOpenRequestSeq

    useUIStore.getState().requestOpenWhatsNew()

    expect(useUIStore.getState().whatsNewOpenRequestSeq).toBe(before + 1)
  })

  it('starts with thinking content hidden', () => {
    expect(useUIStore.getState().showThinkingContent).toBe(false)
  })

  it('starts with sidebar project, pinned, and chat sections expanded', () => {
    expect(useUIStore.getState().projectsSectionCollapsed).toBe(false)
    expect(useUIStore.getState().pinnedSectionCollapsed).toBe(false)
    expect(useUIStore.getState().chatsSectionCollapsed).toBe(false)
  })
})

const settingsSet = vi.fn()

describe('uiStore sidebar section preferences', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsSet.mockResolvedValue(undefined)
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: {
        api: {
          settings: {
            set: settingsSet
          }
        }
      }
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

  it('updates and persists the Projects section collapse preference', () => {
    useUIStore.getState().setProjectsSectionCollapsed(true)

    expect(useUIStore.getState().projectsSectionCollapsed).toBe(true)
    expect(settingsSet).toHaveBeenLastCalledWith({ projectsSectionCollapsed: true })

    useUIStore.getState().setProjectsSectionCollapsed(false)

    expect(useUIStore.getState().projectsSectionCollapsed).toBe(false)
    expect(settingsSet).toHaveBeenLastCalledWith({ projectsSectionCollapsed: false })
  })

  it('updates and persists the Pinned section collapse preference', () => {
    useUIStore.getState().setPinnedSectionCollapsed(true)

    expect(useUIStore.getState().pinnedSectionCollapsed).toBe(true)
    expect(settingsSet).toHaveBeenLastCalledWith({ pinnedSectionCollapsed: true })

    useUIStore.getState().setPinnedSectionCollapsed(false)

    expect(useUIStore.getState().pinnedSectionCollapsed).toBe(false)
    expect(settingsSet).toHaveBeenLastCalledWith({ pinnedSectionCollapsed: false })
  })

  it('updates and persists the Chats section collapse preference', () => {
    useUIStore.getState().setChatsSectionCollapsed(true)

    expect(useUIStore.getState().chatsSectionCollapsed).toBe(true)
    expect(settingsSet).toHaveBeenLastCalledWith({ chatsSectionCollapsed: true })

    useUIStore.getState().setChatsSectionCollapsed(false)

    expect(useUIStore.getState().chatsSectionCollapsed).toBe(false)
    expect(settingsSet).toHaveBeenLastCalledWith({ chatsSectionCollapsed: false })
  })
})

describe('uiStore goToNewChat', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'settings',
      welcomeDraft: null,
      welcomeDraftsByWorkspace: {},
      welcomeDraftWorkspacePath: null,
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      detailPanelWidth: DETAIL_DEFAULT_WIDTH,
      detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      responsiveLayout: 'full'
    })
  })

  it('clears active thread and routes to conversation view', () => {
    useThreadStore.getState().setActiveThreadId('thread-123')

    useUIStore.getState().goToNewChat()

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('keeps welcome drafts scoped by workspace', () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Draft A',
      images: [],
      files: [],
      mode: 'agent',
      model: 'Default',
      contextWindow: { mode: 'max' }
    }, '/workspace/a')
    useUIStore.getState().setWelcomeDraft({
      text: 'Draft B',
      images: [],
      files: [],
      mode: 'plan',
      model: 'gpt-test'
    }, '/workspace/b')

    useUIStore.getState().setWelcomeDraftWorkspace('/workspace/a')
    expect(useUIStore.getState().welcomeDraft?.text).toBe('Draft A')
    expect(useUIStore.getState().welcomeDraft?.contextWindow).toEqual({ mode: 'max' })

    useUIStore.getState().setWelcomeDraftWorkspace('/workspace/b')
    expect(useUIStore.getState().welcomeDraft?.text).toBe('Draft B')

    useUIStore.getState().clearWelcomeDraft('/workspace/b')
    expect(useUIStore.getState().getWelcomeDraftForWorkspace('/workspace/a')?.text).toBe('Draft A')
    expect(useUIStore.getState().getWelcomeDraftForWorkspace('/workspace/b')).toBeNull()
  })
})

describe('uiStore pending welcome turn', () => {
  beforeEach(() => {
    useUIStore.getState().setPendingWelcomeTurn(null)
  })

  it('preserves approval policy when consuming the pending welcome turn', () => {
    useUIStore.getState().setPendingWelcomeTurn({
      threadId: 'thread-approval',
      text: 'hello',
      approvalPolicy: 'autoApprove'
    })

    const pending = useUIStore.getState().consumePendingWelcomeTurnIfMatch('thread-approval')

    expect(pending?.approvalPolicy).toBe('autoApprove')
  })

  it('preserves reasoning when consuming the pending welcome turn', () => {
    const reasoning = {
      enabled: true,
      effort: 'high' as const,
      output: 'summary' as const
    }

    useUIStore.getState().setPendingWelcomeTurn({
      threadId: 'thread-reasoning',
      text: 'hello',
      reasoning
    })

    const pending = useUIStore.getState().consumePendingWelcomeTurnIfMatch('thread-reasoning')

    expect(pending?.reasoning).toEqual(reasoning)
  })

  it('preserves contextWindow when consuming the pending welcome turn', () => {
    useUIStore.getState().setPendingWelcomeTurn({
      threadId: 'thread-context',
      text: 'hello',
      contextWindow: { mode: 'max' }
    })

    const pending = useUIStore.getState().consumePendingWelcomeTurnIfMatch('thread-context')

    expect(pending?.contextWindow).toEqual({ mode: 'max' })
  })

  it('preserves sentAsGoal when consuming the pending welcome turn', () => {
    useUIStore.getState().setPendingWelcomeTurn({
      threadId: 'thread-goal',
      text: 'review the branch',
      sentAsGoal: true
    })

    const pending = useUIStore.getState().consumePendingWelcomeTurnIfMatch('thread-goal')

    expect(pending?.sentAsGoal).toBe(true)
  })
})

describe('uiStore responsive panel preferences', () => {
  beforeEach(() => {
    useUIStore.setState({
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      detailPanelPreferredVisible: true,
      detailPanelVisible: true,
      detailPanelWidth: DETAIL_DEFAULT_WIDTH,
      detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
      responsiveLayout: 'full',
      activeDetailTab: { kind: 'system', id: 'changes' },
      lastActiveSystemTab: 'changes',
      selectedChangedFile: null,
      autoShowReasons: new Set<string>()
    })
  })

  it('preserves a manually hidden detail panel when layout stays full', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('restores the preferred detail visibility after leaving a narrow breakpoint', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    useUIStore.getState().setResponsiveLayout('collapsed')

    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('restores the preferred sidebar expansion after leaving the collapsed breakpoint', () => {
    useUIStore.getState().setSidebarCollapsed(false)
    useUIStore.getState().setResponsiveLayout('collapsed')

    expect(useUIStore.getState().sidebarCollapsed).toBe(true)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().sidebarPreferredCollapsed).toBe(false)
    expect(useUIStore.getState().sidebarCollapsed).toBe(false)
  })

  it('auto-opening the detail panel updates the stored preference', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    useUIStore.getState().setResponsiveLayout('no-detail')

    useUIStore.getState().showChangesForFile('src/foo.ts')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)

    useUIStore.getState().setResponsiveLayout('full')

    expect(useUIStore.getState().detailPanelVisible).toBe(true)
    expect(useUIStore.getState().selectedChangedFile).toBe('src/foo.ts')
  })

  it('records one-shot auto-show reasons', () => {
    useUIStore.getState().setDetailPanelVisible(false)
    const first = useUIStore.getState().maybeAutoShowForReason('link:thread-1:item-2')
    const second = useUIStore.getState().maybeAutoShowForReason('link:thread-1:item-2')
    expect(first).toBe(true)
    expect(second).toBe(false)
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
  })

  it('clears one-shot auto-show reasons', () => {
    useUIStore.getState().maybeAutoShowForReason('plan:auto')
    expect(useUIStore.getState().autoShowReasons.size).toBe(1)
    useUIStore.getState().resetAutoShowReasons()
    expect(useUIStore.getState().autoShowReasons.size).toBe(0)
  })

  it('can switch system detail tabs without revealing the panel', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveDetailTab('plan', { reveal: false })

    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(useUIStore.getState().lastActiveSystemTab).toBe('plan')
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('can switch viewer tabs without revealing the panel', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveViewerTab('vtab-hidden', { reveal: false })

    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: 'vtab-hidden' })
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('reveals the panel by default when switching detail tabs explicitly', () => {
    useUIStore.getState().setDetailPanelVisible(false)

    useUIStore.getState().setActiveDetailTab('plan')

    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
    expect(useUIStore.getState().detailPanelVisible).toBe(true)
  })
})
