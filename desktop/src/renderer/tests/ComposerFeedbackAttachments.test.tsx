import { useVoiceStore } from '../voice/voiceStore'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ComposerFeedbackAttachments } from '../components/conversation/ComposerFeedbackAttachments'
import { useComposerContextStore } from '../stores/composerContextStore'
import { installDesktopApiMock } from './desktopApiMock'

const reply = {
  kind: 'responseAnnotation' as const,
  id: 'reply',
  threadId: 'task',
  turnId: 'turn',
  itemId: 'item',
  selectedText: 'Keep the original source.',
  comment: '',
}
const diff = {
  kind: 'diffAnnotation' as const,
  id: 'diff',
  path: '/src/file.ts',
  side: 'left' as const,
  startLine: 40,
  endLine: 42,
  selectedText: 'return title;',
  comment: 'Preserve whitespace',
}
const paste = {
  kind: 'pastedText' as const,
  id: 'paste',
  path: '/paste.txt',
  fileName: 'paste.txt',
  preview: 'notes',
}
function Fixture() {
  const contexts = useComposerContextStore((state) => state.getContexts('task'))
  return (
    <LocaleProvider>
      <ComposerFeedbackAttachments
        threadId="task"
        contexts={contexts.filter((context) => context.kind !== 'pastedText')}
      />
    </LocaleProvider>
  )
}
describe('feedback attachment summary', () => {
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
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
    })
    useComposerContextStore.setState({ byThread: {} })
    useComposerContextStore.getState().setContexts('task', [reply, diff, paste])
  })
  it('summarizes feedback and removes it without removing pasted files', async () => {
    render(<Fixture />)
    fireEvent.click(screen.getByRole('button', { name: '1 annotation, 1 comment' }))
    expect(await screen.findByText(reply.selectedText)).toBeTruthy()
    expect(screen.getByText(diff.comment)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Remove annotations' }))
    expect(useComposerContextStore.getState().getContexts('task')).toEqual([
      paste,
    ])
  })
  it('edits only the comment and preserves the original source and code range', async () => {
    render(<Fixture />)
    fireEvent.click(screen.getByRole('button', { name: '1 annotation, 1 comment' }))
    fireEvent.click(
      (await screen.findAllByRole('button', { name: 'Edit annotation' }))[1],
    )
    const editor = screen.getByRole('textbox', { name: 'Your comment' })
    fireEvent.change(editor, { target: { value: 'Keep this exact range' } })
    fireEvent.keyDown(editor, { key: 'Enter' })
    await waitFor(() =>
      expect(useComposerContextStore.getState().getContexts('task')).toEqual([
        reply,
        { ...diff, comment: 'Keep this exact range' },
        paste,
      ]),
    )
  })
})
