import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PanelCheckpoint, PanelScope } from '../../shared/viewer/panelState'
import { panelScopeKey } from '../../shared/viewer/panelState'
import type { ClassifyResult, FileViewerTab } from '../../shared/viewer/types'
import { useFileViewerRestore } from '../hooks/useFileViewerRestore'
import { startDetailPanelPersistence } from '../stores/detailPanelPersistence'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useThreadStore } from '../stores/threadStore'
import { openWorkspaceFileViewer } from '../utils/conversationDeepLink'
import { installDesktopApiMock } from './desktopApiMock'

const scope: PanelScope = { projectKey: '/project-a', threadId: 'thread-a' }
const store = () => useDetailPanelStore.getState()
const managers: ReturnType<typeof startDetailPanelPersistence>[] = []
const authorizeFile = vi.fn(async ({ absolutePath }: { absolutePath: string; workspacePath: string }) => ({ absolutePath }))
const classify = vi.fn<({ absolutePath }: { absolutePath: string }) => Promise<ClassifyResult>>()
const facts: ClassifyResult = { contentClass: 'text', mime: 'text/plain', sizeBytes: 42, mtimeMs: 1 }

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: Error) => void
  const promise = new Promise<T>((done, fail) => {
    resolve = done
    reject = fail
  })
  return { promise, resolve, reject }
}

function file(id = 'report', absolutePath = '/external/report.md'): FileViewerTab {
  return {
    id, kind: 'file', label: `${id}.md`, absolutePath, relativePath: absolutePath,
    contentClass: 'text', navigationHint: { line: 7, column: 2, fragment: 'details' }, wordWrap: false
  }
}

async function hydrate(tabs: FileViewerTab[] = [file()]) {
  const saved: PanelCheckpoint = {
    version: 1, tabs, systemTabs: ['plan'], activeTab: { kind: 'viewer', id: tabs[0].id },
    visible: true, fullView: false, width: 640
  }
  const update = vi.fn(async (_scope: PanelScope, _checkpoint: PanelCheckpoint | null) => {})
  const manager = startDetailPanelPersistence({ read: async () => saved, update }, async () => [])
  managers.push(manager)
  await manager.hydrate(scope)
  return { update }
}

function mountFile(id = 'report') {
  return renderHook(() => {
    const tab = useDetailPanelStore(state => state.getCurrentPanel().tabs.find(item => item.id === id))
    return useFileViewerRestore(tab)
  })
}

beforeEach(() => {
  store().reset()
  store().onWorkspaceSwitched(scope.projectKey)
  store().onThreadSwitched(scope.threadId)
  useThreadStore.getState().reset()
  authorizeFile.mockReset()
  authorizeFile.mockImplementation(async ({ absolutePath }) => ({ absolutePath }))
  classify.mockReset()
  classify.mockResolvedValue(facts)
  installDesktopApiMock({ workspace: { viewer: { authorizeFile, classify } } })
})

afterEach(() => {
  cleanup()
  for (const manager of managers.splice(0)) manager.dispose()
})

describe('restored file viewers', () => {
  it('prepares only the presented file before allowing content to load and refreshes its file facts', async () => {
    const authorization = deferred<{ absolutePath: string }>()
    const classification = deferred<ClassifyResult>()
    authorizeFile.mockReturnValueOnce(authorization.promise)
    classify.mockReturnValueOnce(classification.promise)
    const original = file('report', '/external/link.md')
    const other = file('other', '/external/other.md')
    await hydrate([original, other])
    expect(authorizeFile).not.toHaveBeenCalled()
    expect(store().getCurrentPanel().tabs).toEqual([
      expect.objectContaining({ id: original.id, restore: 'required' }),
      expect.objectContaining({ id: other.id, restore: 'required' })
    ])

    const viewer = mountFile()
    expect(viewer.result.current).toBe(true)
    expect(authorizeFile).toHaveBeenCalledExactlyOnceWith({
      absolutePath: original.absolutePath, workspacePath: scope.projectKey
    })
    expect(classify).not.toHaveBeenCalled()
    await act(async () => {
      authorization.resolve({ absolutePath: '/external/resolved.md' })
      await authorization.promise
    })
    expect(classify).toHaveBeenCalledExactlyOnceWith({ absolutePath: '/external/resolved.md' })
    expect(viewer.result.current).toBe(true)
    await act(async () => {
      classification.resolve({ ...facts, contentClass: 'image', sizeBytes: 2048 })
      await classification.promise
    })

    expect(viewer.result.current).toBe(false)
    expect(store().getCurrentPanel()).toMatchObject({
      activeDetailTab: { kind: 'viewer', id: original.id },
      tabs: [
        {
          id: original.id, absolutePath: '/external/resolved.md', contentClass: 'image', sizeBytes: 2048,
          navigationHint: original.navigationHint, wordWrap: false, restore: 'none'
        },
        { id: other.id, absolutePath: other.absolutePath, restore: 'required' }
      ]
    })
    expect(authorizeFile).toHaveBeenCalledTimes(1)
  })

  it('isolates an unavailable file and retries it in the same tab without persisting errors', async () => {
    const authorization = deferred<{ absolutePath: string }>()
    authorizeFile.mockReturnValueOnce(authorization.promise)
    const { update } = await hydrate([file(), file('other', '/external/other.md')])
    const failedViewer = mountFile()
    await act(async () => {
      authorization.reject(new Error('File is no longer available'))
      await authorization.promise.catch(() => {})
    })
    expect(failedViewer.result.current).toBe(false)
    expect(store().getCurrentPanel()).toMatchObject({
      activeDetailTab: { kind: 'viewer', id: 'report' },
      tabs: [
        { id: 'report', restore: 'none', errorMessage: expect.stringContaining('File is no longer available') },
        { id: 'other', restore: 'required' }
      ]
    })
    const checkpoint = update.mock.calls.at(-1)?.[1]
    expect(checkpoint?.tabs.every(tab => !('restore' in tab) && !('errorMessage' in tab))).toBe(true)
    failedViewer.unmount()
    const authorizationOfOther = deferred<{ absolutePath: string }>()
    authorizeFile.mockReturnValueOnce(authorizationOfOther.promise)
    const otherViewer = mountFile('other')
    expect(otherViewer.result.current).toBe(true)
    await act(async () => {
      authorizationOfOther.resolve({ absolutePath: '/external/other.md' })
      await authorizationOfOther.promise
    })
    expect(otherViewer.result.current).toBe(false)
    expect(store().getCurrentPanel().tabs[1]).toMatchObject({ id: 'other', restore: 'none', sizeBytes: 42 })
    otherViewer.unmount()
    await openWorkspaceFileViewer({
      threadId: scope.threadId, workspacePath: scope.projectKey,
      absolutePath: '/external/report.md', forceNew: false, hint: { line: 12 }
    })
    expect(store().getCurrentPanel().tabs.map(tab => tab.id)).toEqual(['report', 'other'])
    expect(store().getCurrentPanel().tabs[0]).toMatchObject({
      id: 'report', sizeBytes: 42, navigationHint: { line: 12 }, restore: 'none'
    })
    expect(store().getCurrentPanel().tabs[0].errorMessage).toBeUndefined()
    expect(mountFile().result.current).toBe(false)
    expect(authorizeFile).toHaveBeenCalledTimes(3)
  })

  it.each(['close', 'delete'] as const)('does not resurrect a file when its owner is removed by %s during preparation', async action => {
    const authorization = deferred<{ absolutePath: string }>()
    authorizeFile.mockReturnValueOnce(authorization.promise)
    await hydrate()
    mountFile()
    act(() => {
      if (action === 'close') store().closeTab(scope.threadId, 'report')
      else store().onThreadDeleted(scope.threadId)
    })
    await act(async () => {
      authorization.resolve({ absolutePath: '/external/report.md' })
      await authorization.promise
    })
    expect(store().getPanel(scope.threadId, scope.projectKey).tabs).toEqual([])
    if (action === 'delete') expect(store().byThread.has(panelScopeKey(scope))).toBe(false)
  })

  it('cancels an unmounted restore without changing another project and retries when its owner returns', async () => {
    const firstAuthorization = deferred<{ absolutePath: string }>()
    authorizeFile.mockReturnValueOnce(firstAuthorization.promise)
    await hydrate()
    const firstViewer = mountFile()
    expect(firstViewer.result.current).toBe(true)
    firstViewer.unmount()
    act(() => {
      store().onWorkspaceSwitched('/project-b')
      store().onThreadSwitched('thread-b')
      store().openFile({
        threadId: 'thread-b', absolutePath: '/project-b/ready.md', relativePath: 'ready.md', contentClass: 'text'
      })
    })
    const otherPanel = store().getCurrentPanel()
    await act(async () => {
      firstAuthorization.resolve({ absolutePath: '/external/report.md' })
      await firstAuthorization.promise
    })
    expect(store().getCurrentPanel()).toBe(otherPanel)
    expect(store().getPanel(scope.threadId, scope.projectKey).tabs[0]).toMatchObject({ restore: 'required' })

    act(() => {
      store().onWorkspaceSwitched(scope.projectKey)
      store().onThreadSwitched(scope.threadId)
    })
    const secondAuthorization = deferred<{ absolutePath: string }>()
    authorizeFile.mockReturnValueOnce(secondAuthorization.promise)
    const secondViewer = mountFile()
    expect(secondViewer.result.current).toBe(true)
    await act(async () => {
      secondAuthorization.resolve({ absolutePath: '/external/report.md' })
      await secondAuthorization.promise
    })
    expect(secondViewer.result.current).toBe(false)
    expect(authorizeFile.mock.calls).toEqual([
      [{ absolutePath: '/external/report.md', workspacePath: scope.projectKey }],
      [{ absolutePath: '/external/report.md', workspacePath: scope.projectKey }]
    ])
    expect(store().getPanel('thread-b', '/project-b')).toBe(otherPanel)
  })

})
