import { beforeEach, describe, expect, it } from 'vitest'
import { useComposerFileReferenceStore } from '../stores/composerFileReferenceStore'
import { useThreadStore } from '../stores/threadStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { welcomeScopeKey } from '../utils/detailPanelScope'

function selectScope(scopeId: string | null): void {
  useThreadStore.setState({ activeThreadId: scopeId?.startsWith('welcome:') ? null : scopeId })
  useViewerTabStore.setState({ welcomeScopeId: scopeId?.startsWith('welcome:') ? scopeId : null })
}

describe('pending file references across scopes', () => {
  beforeEach(() => {
    useComposerFileReferenceStore.setState({ pendingByScope: new Map() })
    selectScope(null)
  })

  it.each([
    ['two threads', 'thread-a', 'thread-b'],
    ['two welcome pages', welcomeScopeKey('project-a'), welcomeScopeKey('project-b')],
    ['a thread and a welcome page', 'thread-a', welcomeScopeKey('project-b')],
    ['no selected scope and a thread', null, 'thread-b']
  ])('retains both queues and their ordering for %s', (_scenario, scopeA, scopeB) => {
    selectScope(scopeA)
    useComposerFileReferenceStore.getState().request('a-first.ts')
    selectScope(scopeB)
    useComposerFileReferenceStore.getState().request('b-first.ts')
    selectScope(scopeA)
    useComposerFileReferenceStore.getState().request('a-second.ts')
    selectScope(scopeB)
    useComposerFileReferenceStore.getState().request('b-second.ts')

    expect(useComposerFileReferenceStore.getState().consume(scopeB)).toEqual(['b-first.ts', 'b-second.ts'])
    expect(useComposerFileReferenceStore.getState().consume(scopeA)).toEqual(['a-first.ts', 'a-second.ts'])
    expect(useComposerFileReferenceStore.getState().consume(scopeB)).toEqual([])
    expect(useComposerFileReferenceStore.getState().consume(scopeA)).toEqual([])
  })

  it('consuming an empty scope does not discard another scope\'s pending paths', () => {
    selectScope('thread-a')
    useComposerFileReferenceStore.getState().request('a.ts')
    expect(useComposerFileReferenceStore.getState().consume('thread-b')).toEqual([])
    expect(useComposerFileReferenceStore.getState().consume('thread-a')).toEqual(['a.ts'])
  })
})
