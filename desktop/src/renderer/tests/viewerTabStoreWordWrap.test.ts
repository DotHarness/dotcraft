import { beforeEach, describe, expect, it } from 'vitest'
import { useViewerTabStore } from '../stores/viewerTabStore'
import type { FileViewerTab } from '../../shared/viewer/types'

const store = () => useViewerTabStore.getState()
const THREAD = 'thread-ww'
const WS = '/home/user/project'

function fileTab(threadId: string, tabId: string): FileViewerTab | undefined {
  return store()
    .getThreadState(threadId)
    .tabs.find((t): t is FileViewerTab => t.id === tabId && t.kind === 'file')
}

beforeEach(() => {
  useViewerTabStore.setState({
    byThread: new Map(),
    currentThreadId: null,
    currentWorkspacePath: null
  })
})

describe('viewerTabStore.setWordWrap', () => {
  it('defaults to undefined and toggles the per-tab preference', () => {
    const id = store().openFile({
      threadId: THREAD,
      absolutePath: `${WS}/a.ts`,
      relativePath: 'a.ts',
      contentClass: 'text'
    })
    expect(fileTab(THREAD, id)?.wordWrap).toBeUndefined()

    store().setWordWrap(THREAD, id, false)
    expect(fileTab(THREAD, id)?.wordWrap).toBe(false)

    store().setWordWrap(THREAD, id, true)
    expect(fileTab(THREAD, id)?.wordWrap).toBe(true)
  })

  it('does not throw for an unknown tab id', () => {
    store().openFile({
      threadId: THREAD,
      absolutePath: `${WS}/a.ts`,
      relativePath: 'a.ts',
      contentClass: 'text'
    })
    expect(() => store().setWordWrap(THREAD, 'missing', false)).not.toThrow()
  })
})
