import { describe, expect, it, vi } from 'vitest'
import { useComposerDraftStore } from '../stores/composerDraftStore'

import {
  appendVoiceTranscript,
  appendTranscriptToDraft,
  captureComposerVoiceSubmitter,
  isAvailableComposerVoiceOrigin,
  registerComposerVoiceTarget,
  releaseComposerVoiceSubmitter,
  retainComposerVoiceSubmitter
} from './composerDraftBridge'

describe('appendTranscriptToDraft', () => {
  it('trims and inserts one separating space', () => {
    const result = appendTranscriptToDraft({
      text: 'Existing',
      segments: [{ type: 'text', value: 'Existing' }],
      images: [],
      files: []
    }, '  spoken words  ')

    expect(result.text).toBe('Existing spoken words')
    expect(result.segments).toEqual([{ type: 'text', value: 'Existing spoken words' }])
  })

  it('preserves structured segments and attachments', () => {
    const image = { tempPath: 'image.png', dataUrl: 'data:image/png;base64,AA==', fileName: 'image.png', mimeType: 'image/png' }
    const file = { path: 'README.md', name: 'README.md' }
    const result = appendTranscriptToDraft({
      text: '@README.md',
      segments: [{ type: 'file', relativePath: 'README.md' }],
      images: [image],
      files: [file]
    }, 'describe this')

    expect(result.segments).toEqual([
      { type: 'file', relativePath: 'README.md' },
      { type: 'text', value: ' describe this' }
    ])
    expect(result.images).toEqual([image])
    expect(result.files).toEqual([file])
  })

  it('does not add a second space after whitespace', () => {
    const result = appendTranscriptToDraft({
      text: 'Existing ',
      segments: [{ type: 'text', value: 'Existing ' }],
      images: [],
      files: []
    }, 'words')
    expect(result.text).toBe('Existing words')
  })
})

describe('composer voice origins', () => {
  it('accepts only mounted Composer origins', () => {
    expect(isAvailableComposerVoiceOrigin('welcome-composer:workspace')).toBe(false)
    expect(isAvailableComposerVoiceOrigin('agent-builder-intro')).toBe(false)
    expect(isAvailableComposerVoiceOrigin('ordinary-unmounted-thread')).toBe(false)

    const unregister = registerComposerVoiceTarget('mounted-draft', {
      capture: () => ({ text: '', segments: [], images: [], files: [] }),
      apply: () => {},
      submit: async () => {}
    })
    expect(isAvailableComposerVoiceOrigin('mounted-draft')).toBe(true)
    unregister()
    expect(isAvailableComposerVoiceOrigin('mounted-draft')).toBe(false)
  })

  it('retains explicit send after the originating Composer unmounts', async () => {
    const submit = vi.fn(async () => {})
    const unregister = registerComposerVoiceTarget('thread-send', {
      capture: () => ({
        text: 'Existing',
        segments: [{ type: 'text', value: 'Existing' }],
        images: [],
        files: []
      }),
      apply: () => {},
      submit
    })
    const retained = captureComposerVoiceSubmitter('thread-send')
    expect(retained).not.toBeNull()
    retainComposerVoiceSubmitter('session-send', 'thread-send', retained!)
    useComposerDraftStore.getState().saveDraft('thread-send', {
      text: 'Existing',
      segments: [{ type: 'text', value: 'Existing' }],
      images: [],
      files: []
    })
    unregister()

    await appendVoiceTranscript('thread-send', ' spoken ', true, 'session-send')

    expect(submit).toHaveBeenCalledWith(expect.objectContaining({ text: 'Existing spoken' }))
    releaseComposerVoiceSubmitter('session-send')
    useComposerDraftStore.getState().clearDraft('thread-send')
  })
})
