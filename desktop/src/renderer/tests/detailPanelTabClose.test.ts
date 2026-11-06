import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { PanelCheckpoint, PanelScope } from '../../shared/viewer/panelState'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { startDetailPanelPersistence } from '../stores/detailPanelPersistence'

const scope: PanelScope = { projectKey: '/fixture', threadId: 'thread-a' }
const store = () => useDetailPanelStore.getState()
const panel = () => store().getCurrentPanel()

function openBrowser(tabId: string): string {
  return store().openBrowser({ threadId: scope.threadId, tabId })
}

beforeEach(() => {
  store().reset()
  store().onWorkspaceSwitched(scope.projectKey)
  store().onThreadSwitched(scope.threadId)
})

describe('closing detail panel viewer tabs', () => {
  it('preserves a restored Plan selection and checkpoints when background viewers close', async () => {
    const checkpoint: PanelCheckpoint = {
      version: 1,
      tabs: ['terminal-a', 'terminal-b'].map(id => ({
        id, kind: 'terminal' as const, label: 'Terminal', cwd: '/fixture', hasStarted: false
      })),
      systemTabs: ['changes', 'plan'],
      activeTab: { kind: 'system', id: 'plan' },
      visible: true,
      fullView: true,
      width: 600
    }
    const update = vi.fn(async (_scope: PanelScope, _checkpoint: PanelCheckpoint | null) => {})
    const manager = startDetailPanelPersistence({ read: async () => checkpoint, update }, async () => [])
    try {
      await manager.hydrate(scope)
      update.mockClear()

      store().closeTab(scope.threadId, 'terminal-b')
      expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
      expect(panel().activeTabId).toBe('terminal-a')
      store().closeTab(scope.threadId, 'terminal-a')
      expect(panel()).toMatchObject({
        tabs: [], activeTabId: null, activeDetailTab: { kind: 'system', id: 'plan' },
        detailPanelPreferredVisible: true, detailPanelFullView: true
      })
      expect(update).toHaveBeenCalledTimes(2)
      for (const [savedScope, saved] of update.mock.calls) {
        expect(savedScope).toEqual(scope)
        expect(saved).toMatchObject({
          activeTab: { kind: 'system', id: 'plan' }, visible: true, fullView: true
        })
      }
    } finally {
      manager.dispose()
    }
  })

  it('preserves the selected viewer when another remembered viewer closes', () => {
    for (const id of ['x', 'y', 'z']) openBrowser(id)
    store().setActiveViewerTab('x')

    store().closeTab(scope.threadId, 'z')

    expect(panel().activeDetailTab).toEqual({ kind: 'viewer', id: 'x' })
    expect(panel().activeTabId).toBe('y')
    expect(panel().tabs.map(tab => tab.id)).toEqual(['x', 'y'])
  })

  it('selects a neighbor when the displayed viewer closes even if the remembered viewer differs', () => {
    for (const id of ['x', 'y', 'z']) openBrowser(id)
    store().setActiveViewerTab('y')
    store().closeTab(scope.threadId, 'y')
    expect(panel().activeDetailTab).toEqual({ kind: 'viewer', id: 'x' })
    expect(panel().activeTabId).toBe('x')

    store().closeTab(scope.threadId, 'x')
    store().setActiveDetailTab('plan')
    store().setActiveViewerTab('z')
    store().closeTab(scope.threadId, 'z')
    expect(panel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
  })

  it('hides an empty panel after its selected last viewer closes', () => {
    openBrowser('only')
    store().setActiveViewerTab('only')
    store().setDetailPanelFullView(true)

    store().closeTab(scope.threadId, 'only')

    expect(panel()).toMatchObject({
      tabs: [], activeTabId: null, activeDetailTab: { kind: 'launcher' },
      detailPanelPreferredVisible: false, detailPanelFullView: false
    })
  })

  it('still activates an existing file when explicit navigation removes its Files placeholder', () => {
    const request = { threadId: scope.threadId, absolutePath: '/fixture/file.ts', relativePath: 'file.ts', contentClass: 'text' as const }
    const fileId = store().openFile(request)
    store().openFiles({ threadId: scope.threadId, initialLabel: 'Files' })
    store().setActiveDetailTab('plan')

    store().openFile(request)

    expect(panel().activeDetailTab).toEqual({ kind: 'viewer', id: fileId })
    expect(panel().tabs.map(tab => tab.id)).toEqual([fileId])
  })
})
