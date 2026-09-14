import { useVoiceStore } from '../voice/voiceStore'
import {
  act,
  fireEvent,
  render,
  renderHook,
  screen,
} from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ContextCommentEditor } from '../components/conversation/ContextCommentEditor'
import { useCommentVoice } from '../components/conversation/useCommentVoice'
import {
  appendVoiceTranscript,
  isAvailableComposerVoiceOrigin,
} from '../voice/composerDraftBridge'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { installDesktopApiMock } from './desktopApiMock'

beforeEach(() => {
  useVoiceStore.setState({
    initialized: true,
    recording: null,
    finalizing: null,
    snapshot: {
      model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null },
      sessions: [],
      capacity: 2,
    },
  })
  installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } })
})

it('shows dictation for an empty new comment and a save action after typing', () => {
  const save = vi.fn()
  render(
    <LocaleProvider>
      <ContextCommentEditor onSave={save} onCancel={() => {}} />
    </LocaleProvider>,
  )
  expect(
    screen.getByRole('button', { name: 'Click to dictate or hold' }),
  ).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull()
  const input = screen.getByRole('textbox', { name: 'Your comment' })
  fireEvent.change(input, { target: { value: 'Shorten this heading' } })
  expect(
    screen.queryByRole('button', { name: 'Click to dictate or hold' }),
  ).toBeNull()
  const submit = screen.getByRole('button', { name: 'Save' })
  expect(submit.querySelector('svg.lucide-check')).not.toBeNull()
  fireEvent.click(submit)
  expect(save).toHaveBeenCalledWith('Shorten this heading')
})

it('routes transcription only to the comment and releases its temporary origin on close', async () => {
  const update = vi.fn()
  useComposerDraftStore
    .getState()
    .saveDraft('task', {
      text: 'Keep task draft',
      segments: [],
      images: [],
      files: [],
    })
  const hook = renderHook(() => useCommentVoice('Review', update))
  const origin = hook.result.current.origin
  await act(async () => {
    await appendVoiceTranscript(origin, 'this selection', false)
  })
  expect(update).toHaveBeenCalledWith('Review this selection')
  expect(useComposerDraftStore.getState().getDraft('task')?.text).toBe(
    'Keep task draft',
  )
  hook.unmount()
  expect(isAvailableComposerVoiceOrigin(origin)).toBe(false)
  expect(useComposerDraftStore.getState().getDraft(origin)).toBeNull()
})
