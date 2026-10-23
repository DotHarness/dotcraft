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

const drafts = () => useComposerDraftStore.getState()

describe('composerDraftStore', () => {
  beforeEach(() => {
    useComposerDraftStore.setState({ draftsByThread: {} })
  })

  it('saves, reads, and clears drafts isolated per thread', () => {
    expect(drafts().getDraft('thread-a')).toBeNull()

    drafts().saveDraft('thread-a', { text: 'A draft', segments: [], images: [], files: [] })
    drafts().saveDraft('thread-b', { text: 'B draft', segments: [], images: [], files: [] })
    drafts().saveDraft('', { text: 'x', segments: [], images: [], files: [] })

    expect(drafts().getDraft('thread-a')?.text).toBe('A draft')
    expect(typeof drafts().getDraft('thread-a')?.updatedAt).toBe('number')
    expect(Object.keys(drafts().draftsByThread)).toEqual(['thread-a', 'thread-b'])

    drafts().clearDraft('thread-a')
    expect(drafts().getDraft('thread-a')).toBeNull()
    expect(drafts().getDraft('thread-b')?.text).toBe('B draft')
  })

  it('copies arrays so later mutations do not leak into the store', () => {
    const images = [image()]
    drafts().saveDraft('thread-1', { text: 't', segments: [], images, files: [] })
    images.push(image())
    expect(drafts().getDraft('thread-1')?.images).toHaveLength(1)
  })

  it('threadComposerDraftHasContent treats blank text without attachments as empty', () => {
    expect(threadComposerDraftHasContent({ text: '   ', images: [], files: [] })).toBe(false)
    expect(threadComposerDraftHasContent({ text: 'hi', images: [], files: [] })).toBe(true)
    expect(threadComposerDraftHasContent({ text: '', images: [image()], files: [] })).toBe(true)
  })
})
