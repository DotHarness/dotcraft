import { beforeEach, describe, expect, it } from 'vitest'
import {
  useComposerDraftStore,
  threadComposerDraftHasContent
} from '../stores/composerDraftStore'
import type { ImageAttachment } from '../types/conversation'

function image(): ImageAttachment {
  return {
    tempPath: 'C:\\temp\\image.png',
    dataUrl: 'data:image/png;base64,AAAA',
    fileName: 'image.png',
    mimeType: 'image/png'
  }
}

describe('composerDraftStore', () => {
  beforeEach(() => {
    useComposerDraftStore.setState({ draftsByThread: {} })
  })

  it('saves, reads, and clears a per-thread draft', () => {
    const store = useComposerDraftStore.getState()
    expect(store.getDraft('thread-1')).toBeNull()

    store.saveDraft('thread-1', { text: 'hello', segments: [], images: [], files: [] })
    const draft = useComposerDraftStore.getState().getDraft('thread-1')
    expect(draft?.text).toBe('hello')
    expect(typeof draft?.updatedAt).toBe('number')

    useComposerDraftStore.getState().clearDraft('thread-1')
    expect(useComposerDraftStore.getState().getDraft('thread-1')).toBeNull()
  })

  it('keeps drafts isolated per thread', () => {
    const store = useComposerDraftStore.getState()
    store.saveDraft('thread-a', { text: 'A draft', segments: [], images: [], files: [] })
    store.saveDraft('thread-b', { text: 'B draft', segments: [], images: [], files: [] })

    expect(useComposerDraftStore.getState().getDraft('thread-a')?.text).toBe('A draft')
    expect(useComposerDraftStore.getState().getDraft('thread-b')?.text).toBe('B draft')

    useComposerDraftStore.getState().clearDraft('thread-a')
    expect(useComposerDraftStore.getState().getDraft('thread-a')).toBeNull()
    expect(useComposerDraftStore.getState().getDraft('thread-b')?.text).toBe('B draft')
  })

  it('copies arrays so later mutations do not leak into the store', () => {
    const segments: never[] = []
    const images = [image()]
    useComposerDraftStore.getState().saveDraft('thread-1', { text: 't', segments, images, files: [] })
    images.push(image())
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.images).toHaveLength(1)
  })

  it('ignores an empty thread id', () => {
    useComposerDraftStore.getState().saveDraft('', { text: 'x', segments: [], images: [], files: [] })
    expect(Object.keys(useComposerDraftStore.getState().draftsByThread)).toHaveLength(0)
  })

  it('threadComposerDraftHasContent treats blank text without attachments as empty', () => {
    expect(threadComposerDraftHasContent({ text: '   ', images: [], files: [] })).toBe(false)
    expect(threadComposerDraftHasContent({ text: 'hi', images: [], files: [] })).toBe(true)
    expect(threadComposerDraftHasContent({ text: '', images: [image()], files: [] })).toBe(true)
  })
})
