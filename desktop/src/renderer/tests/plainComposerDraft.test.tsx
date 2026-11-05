import { beforeEach, describe, expect, it, vi } from 'vitest'
import { hydratePlainComposerDrafts, readPlainComposerDraft, savePlainComposerDraft } from '../utils/plainComposerDraft'

describe('composer draft persistence outside a window session', () => {
  beforeEach(() => { localStorage.clear() })

  it('restores application drafts into a fresh window and persists edits by key', () => {
    const set = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', { configurable: true, value: { settings: { set } } })
    hydratePlainComposerDrafts({ thread: 'Saved draft', empty: '' })
    expect(readPlainComposerDraft('thread')).toBe('Saved draft')
    savePlainComposerDraft('thread', 'Changed draft')
    expect(set).toHaveBeenCalledWith({ composerTextDrafts: { thread: 'Changed draft' } })
    savePlainComposerDraft('thread', '')
    expect(readPlainComposerDraft('thread')).toBe('')
    expect(set).toHaveBeenLastCalledWith({ composerTextDrafts: { thread: '' } })
  })
})
